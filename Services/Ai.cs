using Anthropic;
using Anthropic.Models.Beta.Messages;

namespace Foundrmind.Services;

public record AiRequest(string System, string User, int MaxTokens = 32000, bool WebSearch = false, string? Effort = null);

public record AiResult(string Text, string Status, int InputTokens, int OutputTokens, string? Error = null);

/// <summary>
/// Thin wrapper over the Claude Messages API (official Anthropic SDK, streaming).
/// Uses server-side refusal fallbacks so a classifier refusal on Opus 5 is retried on a fallback model automatically.
/// </summary>
public class Ai
{
    private readonly AnthropicClient? _client;
    public string Model { get; }
    public bool IsConfigured => _client != null;

    public Ai(IConfiguration config)
    {
        var key = config["ANTHROPIC_API_KEY"];
        Model = config["ANTHROPIC_MODEL"] is { Length: > 0 } m ? m : "claude-opus-5";
        if (!string.IsNullOrWhiteSpace(key))
            _client = new AnthropicClient { ApiKey = key };
    }

    public async Task<AiResult> StreamAsync(AiRequest req, Func<string, Task> onText, CancellationToken ct = default)
    {
        if (_client == null)
            return new AiResult("", "manual", 0, 0, "AI is not configured (ANTHROPIC_API_KEY missing).");

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
        int inTok = 0, outTok = 0;
        string stop = "end_turn";
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
                    if (md.Delta.StopReason is { } sr) stop = sr.ToString();
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new AiResult(sb.ToString(), "error", inTok, outTok, ex.Message);
        }

        if (stop.Contains("refusal", StringComparison.OrdinalIgnoreCase))
            return new AiResult(sb.ToString(), "refused", inTok, outTok, "The model declined this request. Try rephrasing it.");
        return new AiResult(sb.ToString(), "done", inTok, outTok);
    }
}
