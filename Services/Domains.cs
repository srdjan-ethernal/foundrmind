using System.Net;
using System.Text.RegularExpressions;
using Foundrmind.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Foundrmind.Services;

/// <summary>
/// Custom domains for published pages. A verified host serves only its business's pages:
/// "/" → the chosen home page, "/{slug}" → that page, "/p/{slug}/…" → page, lead form and thank-you.
/// Everything else (the app, login, API) returns 404 on customer domains.
/// </summary>
public partial class Domains(IDbContextFactory<AppDb> dbf, IMemoryCache cache, IConfiguration cfg)
{
    public record Resolved(int ProjectId, string? HomeSlug, HashSet<string> Slugs);

    [GeneratedRegex(@"^(?=.{4,253}$)([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$")]
    private static partial Regex HostPattern();

    public static string? Normalize(string input)
    {
        var h = input.Trim().ToLowerInvariant();
        h = Regex.Replace(h, @"^https?://", "");
        h = h.Split('/')[0].Split(':')[0].TrimEnd('.');
        return HostPattern().IsMatch(h) ? h : null;
    }

    /// <summary>The hostname customers point their CNAME at (PAGES_TARGET, else the PUBLIC_BASE_URL host).</summary>
    public string Target => cfg["PAGES_TARGET"] is { Length: > 0 } t ? t
        : Uri.TryCreate(cfg["PUBLIC_BASE_URL"], UriKind.Absolute, out var u) ? u.Host : "your-foundrmind-host";

    public string? PublicIp => cfg["PUBLIC_IP"] is { Length: > 0 } ip ? ip : null;

    public bool IsAppHost(string host)
    {
        if (host is "localhost" or "127.0.0.1") return true;
        if (Uri.TryCreate(cfg["PUBLIC_BASE_URL"], UriKind.Absolute, out var u) && string.Equals(u.Host, host, StringComparison.OrdinalIgnoreCase)) return true;
        return string.Equals(Target, host, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<Resolved?> ResolveAsync(string host)
    {
        return await cache.GetOrCreateAsync("domain:" + host, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            await using var db = await dbf.CreateDbContextAsync();
            var d = await db.CustomDomains.Where(x => x.Host == host && x.VerifiedAt != null)
                .Select(x => new { x.ProjectId, x.HomePageId }).FirstOrDefaultAsync();
            if (d == null) return null;
            var pages = await db.Pages.Where(p => p.ProjectId == d.ProjectId && p.IsPublished).Select(p => new { p.Id, p.Slug }).ToListAsync();
            var home = pages.FirstOrDefault(p => p.Id == d.HomePageId)?.Slug ?? pages.FirstOrDefault()?.Slug;
            return new Resolved(d.ProjectId, home, pages.Select(p => p.Slug).ToHashSet());
        });
    }

    public void Invalidate(string host) => cache.Remove("domain:" + host);

    /// <summary>Maps a request path on a customer domain to an internal /p/… path, or null for 404.</summary>
    public static string? MapPath(Resolved r, string path)
    {
        if (path is "" or "/") return r.HomeSlug == null ? null : $"/p/{r.HomeSlug}";
        var parts = path.Trim('/').Split('/');
        if (parts.Length == 1 && r.Slugs.Contains(parts[0])) return $"/p/{parts[0]}";
        if (parts[0] == "p" && parts.Length is 2 or 3 && r.Slugs.Contains(parts[1]) && (parts.Length == 2 || parts[2] is "lead" or "thanks"))
            return path;
        return null;
    }

    /// <summary>DNS check: the host must resolve to the same address as our target (CNAME) or to PUBLIC_IP (A record).</summary>
    public async Task<(bool Ok, string Detail)> VerifyDnsAsync(string host)
    {
        IPAddress[] theirs;
        try { theirs = await Dns.GetHostAddressesAsync(host); }
        catch { return (false, $"{host} doesn't resolve yet. DNS changes can take up to an hour."); }

        var ours = new List<IPAddress>();
        if (PublicIp != null && IPAddress.TryParse(PublicIp, out var ip)) ours.Add(ip);
        try { ours.AddRange(await Dns.GetHostAddressesAsync(Target)); } catch { }

        if (ours.Count == 0) return (false, "Server address isn't configured (PAGES_TARGET / PUBLIC_IP).");
        return theirs.Any(ours.Contains)
            ? (true, "DNS is pointing to Foundrmind.")
            : (false, $"{host} points to {string.Join(", ", theirs.Select(a => a.ToString()))}, not to Foundrmind yet.");
    }
}
