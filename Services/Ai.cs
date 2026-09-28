using Anthropic;
using Anthropic.Models.Beta.Messages;
using Foundrmind.Data;
using Microsoft.EntityFrameworkCore;

namespace Foundrmind.Services;

/// <param name="Source">What triggered the call (module key, "demo", "crm-followup"); used for cost reporting.</param>
/// <param name="UserId">Whose quota/spend this is (the project owner); null for the public demo.</param>
public record AiRequest(string System, string User, string Source, int? UserId, int MaxTokens = 32000, bool WebSearch = false, string? Effort = null);

public record AiResult(string Text, string Status, int InputTokens, int OutputTokens, string? Error = null, decimal CostUsd = 0);

/// <summary>
/// Thin wrapper over the Claude Messages API (official Anthropic SDK, streaming).
/// Uses server-side refusal fallbacks so a classifier refusal on Opus 5 is retried on a fallback model automatically.
/// Every call is priced and logged to AiUsage, and a site-wide daily budget (AI_DAILY_BUDGET_USD) stops new calls when reached.
/// </summary>
public class Ai
{
    private readonly AnthropicClient? _client;
    private readonly IDbContextFactory<AppDb> _dbf;
    private readonly ILogger<Ai> _log;
    public string Model { get; }
    public decimal DailyBudgetUsd { get; }
    public bool IsConfigured => _client != null;

    // USD per million tokens (input, output). Web search is billed per 1,000 searches.
    static readonly Dictionary<string, (decimal In, decimal Out)> Prices = new()
    {
        ["claude-opus-5"] = (5m, 25m),
        ["claude-opus-4-8"] = (5m, 25m),
        ["claude-sonnet-5"] = (2m, 10m),
        ["claude-haiku-4-5"] = (1m, 5m),
    };
    const decimal WebSearchPer1000 = 10m;

    public Ai(IConfiguration config, IDbContextFactory<AppDb> dbf, ILogger<Ai> log)
    {
        _dbf = dbf;
        _log = log;
        var key = config["ANTHROPIC_API_KEY"];
        Model = config["ANTHROPIC_MODEL"] is { Length: > 0 } m ? m : "claude-opus-5";
        DailyBudgetUsd = decimal.TryParse(config["AI_DAILY_BUDGET_USD"], System.Globalization.CultureInfo.InvariantCulture, out var b) ? b : 0m;
        if (!string.IsNullOrWhiteSpace(key))
            _client = new AnthropicClient { ApiKey = key };
    }

    public decimal Cost(int inputTokens, int outputTokens, int webSearches = 0)
    {
        var (pin, pout) = Prices.TryGetValue(Model, out var p) ? p : (5m, 25m);
        return inputTokens * pin / 1_000_000m + outputTokens * pout / 1_000_000m + webSearches * WebSearchPer1000 / 1000m;
    }

    public async Task<decimal> SpentTodayAsync()
    {
        await using var db = await _dbf.CreateDbContextAsync();
        var today = DateTime.UtcNow.Date;
        return await db.AiUsage.Where(u => u.CreatedAt >= today).SumAsync(u => (decimal?)u.CostUsd) ?? 0m;
    }

    public async Task<AiResult> StreamAsync(AiRequest req, Func<string, Task> onText, CancellationToken ct = default)
    {
        if (_client == null)
            return new AiResult("", "manual", 0, 0, "AI is not configured (ANTHROPIC_API_KEY missing).");

        if (DailyBudgetUsd > 0 && await SpentTodayAsync() >= DailyBudgetUsd)
        {
            _log.LogWarning("Daily AI budget of ${Budget} reached; refusing {Source}", DailyBudgetUsd, req.Source);
            return new AiResult("", "error", 0, 0, "We're at capacity for today. Please try again tomorrow, or contact support if this is urgent.");
        }

        var tools = new List<BetaToolUnion>();
        if (req.WebSearch)
            tools.Add(new BetaWebSearchTool20260209 { MaxUses = 5 });

        var p = new MessageCreateParams
        {
            Model = Model,
            MaxTokens = req.MaxTokens,
            System = req.System,
            Messages = new List<BetaMessageParam> { new() { Role = Role.User, Content = req.User } },
            Betas = ["server-side-fallback-2026-06-01"],
            Fallbacks = new List<BetaFallbackParam> { new() { Model = "claude-opus-4-8" } },
        };
        if (tools.Count > 0) p = p with { Tools = tools };
        if (req.Effort != null) p = p with { OutputConfig = new BetaOutputConfig { Effort = req.Effort } };

        var sb = new System.Text.StringBuilder();
        int inTok = 0, outTok = 0, searches = 0;
        string stop = "end_turn";
        string? error = null;
        try
        {
            await foreach (var ev in _client.Beta.Messages.CreateStreaming(p, ct))
            {
                if (ev.TryPickStart(out var start))
                    inTok = (int)start.Message.Usage.InputTokens;
                else if (ev.TryPickContentBlockDelta(out var d) && d.Delta.TryPickText(out var t))
                {
                    sb.Append(t.Text);
                    await onText(t.Text);
                }
                else if (ev.TryPickDelta(out var md))
                {
                    outTok = (int)md.Usage.OutputTokens;
                    if (md.Usage.InputTokens is { } it && it > 0) inTok = (int)it;
                    if (md.Usage.ServerToolUse?.WebSearchRequests is { } ws) searches = (int)ws;
                    if (md.Delta.StopReason is { } sr) stop = sr.ToString();
                }
            }
        }
        catch (OperationCanceledException) { error = "Stopped."; }
        catch (Exception ex) { error = ex.Message; }

        var cost = Cost(inTok, outTok, searches);
        await LogAsync(req, inTok, outTok, searches, cost);

        if (error != null)
            return new AiResult(sb.ToString(), "error", inTok, outTok, error, cost);
        if (stop.Contains("refusal", StringComparison.OrdinalIgnoreCase))
            return new AiResult(sb.ToString(), "refused", inTok, outTok, "The model declined this request. Try rephrasing it.", cost);
        return new AiResult(sb.ToString(), "done", inTok, outTok, null, cost);
    }

    async Task LogAsync(AiRequest req, int inTok, int outTok, int searches, decimal cost)
    {
        if (inTok == 0 && outTok == 0) return;
        try
        {
            await using var db = await _dbf.CreateDbContextAsync();
            db.AiUsage.Add(new AiUsage
            {
                Source = req.Source, UserId = req.UserId, Model = Model,
                InputTokens = inTok, OutputTokens = outTok, WebSearches = searches, CostUsd = cost,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to log AI usage");
        }
    }
}
