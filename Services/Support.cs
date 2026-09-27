using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Foundrmind.Data;
using Markdig;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace Foundrmind.Services;

public static class Passwords
{
    const int Iterations = 210_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2") return false;
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, int.Parse(parts[1]), HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

public static class Md
{
    // Raw HTML in model output is escaped (DisableHtml) so AI text can never inject script into the app.
    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();

    public static MarkupString Render(string markdown) => new(Markdown.ToHtml(markdown ?? "", Pipeline));
}

public static class UserExt
{
    public static int? UserId(this ClaimsPrincipal? u)
        => int.TryParse(u?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}

/// <summary>Monthly AI-run quota per plan. Counts module runs + CRM drafts (logged as ModuleRuns).</summary>
public class Usage(IDbContextFactory<AppDb> dbf)
{
    public async Task<(int used, int limit)> GetAsync(int userId)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var plan = await db.Users.Where(u => u.Id == userId).Select(u => u.Plan).FirstAsync();
        var since = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var used = await db.ModuleRuns.CountAsync(r => r.Project!.UserId == userId && r.CreatedAt >= since && r.Status != "manual");
        return (used, Plans.MonthlyRuns(plan));
    }
}

public static class Slug
{
    public static string From(string s)
    {
        var slug = Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length > 40) slug = slug[..40].Trim('-');
        return slug.Length == 0 ? "page" : slug;
    }
}

/// <summary>Per-IP and global daily caps for the public landing-page demo so it cannot run up the API bill.</summary>
public class DemoLimiter
{
    readonly ConcurrentDictionary<string, int> _perIp = new();
    int _global;
    DateOnly _day = DateOnly.FromDateTime(DateTime.UtcNow);
    readonly object _lock = new();
    public int PerIpLimit { get; init; } = 3;
    public int GlobalLimit { get; init; } = 300;

    public bool TryTake(string ip)
    {
        lock (_lock)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (today != _day) { _day = today; _perIp.Clear(); _global = 0; }
            if (_global >= GlobalLimit) return false;
            var n = _perIp.GetValueOrDefault(ip);
            if (n >= PerIpLimit) return false;
            _perIp[ip] = n + 1;
            _global++;
            return true;
        }
    }
}
