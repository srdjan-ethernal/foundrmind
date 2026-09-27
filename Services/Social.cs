using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Foundrmind.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Foundrmind.Services;

public record OAuthTokens(string AccessToken, string RefreshToken, DateTime? ExpiresAt);
public record SocialProfile(string ExternalId, string DisplayName);
public record PublishResult(bool Ok, string Url, string Error);

public interface ISocialProvider
{
    string Key { get; }            // linkedin | x
    string Channel { get; }        // matches ScheduledPost.Channel
    string Name { get; }
    int? MaxLength { get; }
    bool IsConfigured { get; }
    string SetupHint { get; }
    string AuthorizeUrl(string redirectUri, string state, string codeChallenge);
    Task<(OAuthTokens Tokens, SocialProfile Profile)> ExchangeAsync(string code, string redirectUri, string codeVerifier, CancellationToken ct);
    Task<OAuthTokens?> RefreshAsync(string refreshToken, CancellationToken ct);
    Task<PublishResult> PublishAsync(string accessToken, string externalId, string text, CancellationToken ct);
}

/// <summary>LinkedIn "Share on LinkedIn" + "Sign In with LinkedIn using OpenID Connect" products.</summary>
public class LinkedInProvider(IConfiguration cfg, IHttpClientFactory http) : ISocialProvider
{
    public string Key => "linkedin";
    public string Channel => "LinkedIn";
    public string Name => "LinkedIn";
    public int? MaxLength => 3000;
    string? ClientId => cfg["LINKEDIN_CLIENT_ID"];
    string? Secret => cfg["LINKEDIN_CLIENT_SECRET"];
    string ApiVersion => cfg["LINKEDIN_API_VERSION"] is { Length: 6 } v ? v : "202606";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(Secret);
    public string SetupHint => "Set LINKEDIN_CLIENT_ID and LINKEDIN_CLIENT_SECRET (LinkedIn developer app with 'Share on LinkedIn' and 'Sign In with LinkedIn using OpenID Connect').";

    public string AuthorizeUrl(string redirectUri, string state, string codeChallenge) =>
        QueryHelpers.AddQueryString("https://www.linkedin.com/oauth/v2/authorization", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
            ["scope"] = "openid profile w_member_social",
        });

    public async Task<(OAuthTokens, SocialProfile)> ExchangeAsync(string code, string redirectUri, string codeVerifier, CancellationToken ct)
    {
        var c = http.CreateClient();
        var res = await c.PostAsync("https://www.linkedin.com/oauth/v2/accessToken", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = ClientId!,
            ["client_secret"] = Secret!,
        }), ct);
        var tok = await Json(res, ct);
        var access = tok["access_token"]!.GetValue<string>();
        var expires = tok["expires_in"] is { } e ? DateTime.UtcNow.AddSeconds(e.GetValue<int>()) : (DateTime?)null;
        var refresh = tok["refresh_token"]?.GetValue<string>() ?? "";

        var req = new HttpRequestMessage(HttpMethod.Get, "https://api.linkedin.com/v2/userinfo");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        var me = await Json(await c.SendAsync(req, ct), ct);
        return (new OAuthTokens(access, refresh, expires), new SocialProfile(me["sub"]!.GetValue<string>(), me["name"]?.GetValue<string>() ?? "LinkedIn member"));
    }

    public async Task<OAuthTokens?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken)) return null; // most LinkedIn apps get 60-day tokens without refresh
        var res = await http.CreateClient().PostAsync("https://www.linkedin.com/oauth/v2/accessToken", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = ClientId!,
            ["client_secret"] = Secret!,
        }), ct);
        if (!res.IsSuccessStatusCode) return null;
        var tok = await Json(res, ct);
        return new OAuthTokens(tok["access_token"]!.GetValue<string>(), tok["refresh_token"]?.GetValue<string>() ?? refreshToken,
            tok["expires_in"] is { } e ? DateTime.UtcNow.AddSeconds(e.GetValue<int>()) : null);
    }

    public async Task<PublishResult> PublishAsync(string accessToken, string externalId, string text, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["author"] = $"urn:li:person:{externalId}",
            ["commentary"] = EscapeLittleText(text),
            ["visibility"] = "PUBLIC",
            ["distribution"] = new JsonObject
            {
                ["feedDistribution"] = "MAIN_FEED",
                ["targetEntities"] = new JsonArray(),
                ["thirdPartyDistributionChannels"] = new JsonArray(),
            },
            ["lifecycleState"] = "PUBLISHED",
            ["isReshareDisabledByAuthor"] = false,
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.linkedin.com/rest/posts")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.Add("LinkedIn-Version", ApiVersion);
        req.Headers.Add("X-Restli-Protocol-Version", "2.0.0");
        var res = await http.CreateClient().SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
            return new PublishResult(false, "", $"LinkedIn {(int)res.StatusCode}: {Trunc(await res.Content.ReadAsStringAsync(ct))}");
        var urn = res.Headers.TryGetValues("x-restli-id", out var v) ? v.First() : "";
        return new PublishResult(true, urn.Length > 0 ? $"https://www.linkedin.com/feed/update/{urn}/" : "", "");
    }

    /// <summary>LinkedIn's "little text" format treats these characters as markup; escape them so the post reads as written.</summary>
    static string EscapeLittleText(string s) => Regex.Replace(s, @"([\\|{}@\[\]()<>#*_~])", @"\$1");

    internal static async Task<JsonNode> Json(HttpResponseMessage res, CancellationToken ct)
    {
        var s = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)res.StatusCode}: {Trunc(s)}");
        return JsonNode.Parse(s)!;
    }

    internal static string Trunc(string s) => s.Length > 300 ? s[..300] + "…" : s;
}

/// <summary>X API v2 with OAuth 2.0 Authorization Code + PKCE (confidential client).</summary>
public class XProvider(IConfiguration cfg, IHttpClientFactory http) : ISocialProvider
{
    public string Key => "x";
    public string Channel => "X";
    public string Name => "X (Twitter)";
    public int? MaxLength => 280;
    string? ClientId => cfg["X_CLIENT_ID"];
    string? Secret => cfg["X_CLIENT_SECRET"];
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(Secret);
    public string SetupHint => "Set X_CLIENT_ID and X_CLIENT_SECRET (X developer app, OAuth 2.0, type 'Web App', read and write).";

    public string AuthorizeUrl(string redirectUri, string state, string codeChallenge) =>
        QueryHelpers.AddQueryString("https://x.com/i/oauth2/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = "tweet.read tweet.write users.read offline.access",
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        });

    HttpRequestMessage TokenRequest(Dictionary<string, string> form)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.x.com/2/oauth2/token") { Content = new FormUrlEncodedContent(form) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{Secret}")));
        return req;
    }

    static OAuthTokens Tokens(JsonNode tok, string fallbackRefresh = "") => new(
        tok["access_token"]!.GetValue<string>(),
        tok["refresh_token"]?.GetValue<string>() ?? fallbackRefresh,
        tok["expires_in"] is { } e ? DateTime.UtcNow.AddSeconds(e.GetValue<int>()) : null);

    public async Task<(OAuthTokens, SocialProfile)> ExchangeAsync(string code, string redirectUri, string codeVerifier, CancellationToken ct)
    {
        var c = http.CreateClient();
        var tok = await LinkedInProvider.Json(await c.SendAsync(TokenRequest(new()
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
            ["client_id"] = ClientId!,
        }), ct), ct);
        var tokens = Tokens(tok);

        var req = new HttpRequestMessage(HttpMethod.Get, "https://api.x.com/2/users/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var me = (await LinkedInProvider.Json(await c.SendAsync(req, ct), ct))["data"]!;
        return (tokens, new SocialProfile(me["id"]!.GetValue<string>(), "@" + me["username"]!.GetValue<string>()));
    }

    public async Task<OAuthTokens?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken)) return null;
        var res = await http.CreateClient().SendAsync(TokenRequest(new()
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = ClientId!,
        }), ct);
        if (!res.IsSuccessStatusCode) return null;
        return Tokens(JsonNode.Parse(await res.Content.ReadAsStringAsync(ct))!, refreshToken);
    }

    public async Task<PublishResult> PublishAsync(string accessToken, string externalId, string text, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.x.com/2/tweets")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { text }), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var res = await http.CreateClient().SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            return new PublishResult(false, "", $"X {(int)res.StatusCode}: {LinkedInProvider.Trunc(body)}");
        var id = JsonNode.Parse(body)?["data"]?["id"]?.GetValue<string>() ?? "";
        return new PublishResult(true, id.Length > 0 ? $"https://x.com/i/web/status/{id}" : "", "");
    }
}

/// <summary>Connects accounts, keeps tokens fresh and publishes scheduled posts.</summary>
public class SocialService(IEnumerable<ISocialProvider> providers, IDbContextFactory<AppDb> dbf, IDataProtectionProvider dp, ILogger<SocialService> log)
{
    readonly IDataProtector _protector = dp.CreateProtector("Foundrmind.SocialTokens.v1");
    public IReadOnlyList<ISocialProvider> Providers { get; } = providers.ToList();

    public ISocialProvider? ByKey(string key) => Providers.FirstOrDefault(p => p.Key == key);
    public ISocialProvider? ByChannel(string channel) => Providers.FirstOrDefault(p => p.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase));

    /// <summary>Channels we can post to automatically (given a connected account). Others are posted manually from the calendar.</summary>
    public bool IsAutoChannel(string channel) => ByChannel(channel) is { IsConfigured: true };

    public static (string Verifier, string Challenge) Pkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public string Protect(string s) => s.Length == 0 ? "" : _protector.Protect(s);
    public string Unprotect(string s) => s.Length == 0 ? "" : _protector.Unprotect(s);

    public async Task SaveAccountAsync(int userId, ISocialProvider p, OAuthTokens t, SocialProfile profile)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var acc = await db.SocialAccounts.FirstOrDefaultAsync(a => a.UserId == userId && a.Provider == p.Key);
        if (acc == null) { acc = new SocialAccount { UserId = userId, Provider = p.Key }; db.SocialAccounts.Add(acc); }
        acc.ExternalId = profile.ExternalId;
        acc.DisplayName = profile.DisplayName;
        acc.AccessTokenEnc = Protect(t.AccessToken);
        acc.RefreshTokenEnc = Protect(t.RefreshToken);
        acc.ExpiresAt = t.ExpiresAt;
        await db.SaveChangesAsync();
    }

    /// <summary>Publishes one post now. Updates the post's status; never throws.</summary>
    public async Task<PublishResult> PublishAsync(int postId, CancellationToken ct = default)
    {
        await using var db = await dbf.CreateDbContextAsync(ct);
        var post = await db.Posts.Include(p => p.Project).FirstOrDefaultAsync(p => p.Id == postId, ct);
        if (post == null) return new PublishResult(false, "", "Post not found.");
        var provider = ByChannel(post.Channel);
        PublishResult result;
        if (provider == null)
            result = new PublishResult(false, "", $"{post.Channel} can't be posted automatically. Copy it and mark it as posted.");
        else if (!provider.IsConfigured)
            result = new PublishResult(false, "", $"{provider.Name} isn't set up on this server yet.");
        else if (provider.MaxLength is { } max && post.Text.Length > max)
            result = new PublishResult(false, "", $"Too long for {provider.Name}: {post.Text.Length}/{max} characters.");
        else
        {
            var acc = await db.SocialAccounts.FirstOrDefaultAsync(a => a.UserId == post.Project!.UserId && a.Provider == provider.Key, ct);
            if (acc == null)
                result = new PublishResult(false, "", $"Connect your {provider.Name} account first.");
            else
            {
                try
                {
                    var token = await AccessTokenAsync(db, acc, provider, ct);
                    result = token == null
                        ? new PublishResult(false, "", $"Your {provider.Name} connection expired. Reconnect it.")
                        : await provider.PublishAsync(token, acc.ExternalId, post.Text, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning(ex, "Publishing post {PostId} failed", postId);
                    result = new PublishResult(false, "", ex.Message);
                }
            }
        }

        post.Attempts++;
        if (result.Ok)
        {
            post.Status = PostStatus.Posted;
            post.PostedAt = DateTime.UtcNow;
            post.ExternalUrl = result.Url;
            post.Error = "";
        }
        else
        {
            post.Status = PostStatus.Failed;
            post.Error = result.Error;
        }
        await db.SaveChangesAsync(ct);
        return result;
    }

    async Task<string?> AccessTokenAsync(AppDb db, SocialAccount acc, ISocialProvider p, CancellationToken ct)
    {
        if (acc.ExpiresAt == null || acc.ExpiresAt > DateTime.UtcNow.AddMinutes(2))
            return Unprotect(acc.AccessTokenEnc);
        var fresh = await p.RefreshAsync(Unprotect(acc.RefreshTokenEnc), ct);
        if (fresh == null) return null;
        acc.AccessTokenEnc = Protect(fresh.AccessToken);
        acc.RefreshTokenEnc = Protect(fresh.RefreshToken);
        acc.ExpiresAt = fresh.ExpiresAt;
        await db.SaveChangesAsync(ct);
        return fresh.AccessToken;
    }
}

/// <summary>Publishes due scheduled posts every minute.</summary>
public class PostScheduler(IServiceProvider sp, ILogger<PostScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                var social = sp.GetRequiredService<SocialService>();
                var dbf = sp.GetRequiredService<IDbContextFactory<AppDb>>();
                await using var db = await dbf.CreateDbContextAsync(stop);
                var now = DateTime.UtcNow;
                var due = await db.Posts.Where(p => p.Status == PostStatus.Scheduled && p.ScheduledAt <= now)
                    .OrderBy(p => p.ScheduledAt).Select(p => new { p.Id, p.Channel }).Take(50).ToListAsync(stop);
                foreach (var p in due.Where(p => social.IsAutoChannel(p.Channel)))
                {
                    var r = await social.PublishAsync(p.Id, stop);
                    log.LogInformation("Scheduled post {PostId} → {Result}", p.Id, r.Ok ? "posted" : r.Error);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Post scheduler tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stop));
    }
}
