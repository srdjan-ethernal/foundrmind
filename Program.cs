using System.Security.Claims;
using System.Text.RegularExpressions;
using Foundrmind.Components;
using Foundrmind.Data;
using Foundrmind.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Postgres in production (Azure Database for PostgreSQL or a Postgres container on Hetzner), SQLite locally.
var conn = builder.Configuration.GetConnectionString("Default") ?? "Data Source=foundrmind.db";
builder.Services.AddDbContextFactory<AppDb>(o =>
{
    if (conn.Contains("Host=", StringComparison.OrdinalIgnoreCase)) o.UseNpgsql(conn);
    else o.UseSqlite(conn);
});

// Persist auth-cookie keys so users stay logged in across container restarts/redeploys.
if (builder.Configuration["DATA_PROTECTION_PATH"] is { Length: > 0 } dpPath)
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(dpPath)).SetApplicationName("Foundrmind");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
        o.Cookie.Name = "fm_auth";
        o.Cookie.SameSite = SameSiteMode.Lax;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddSingleton<Ai>();
builder.Services.AddSingleton<DemoLimiter>();
builder.Services.AddScoped<Usage>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ISocialProvider, LinkedInProvider>();
builder.Services.AddSingleton<ISocialProvider, XProvider>();
builder.Services.AddSingleton<SocialService>();
builder.Services.AddSingleton<Billing>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<Domains>();
builder.Services.AddHostedService<PostScheduler>();

// Behind Caddy / Azure front ends: trust X-Forwarded-* so we see the real client IP and scheme.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDb>>().CreateDbContext();
    // Postgres: versioned migrations. SQLite (local dev): create schema directly; delete the .db file after model changes.
    if (db.Database.IsNpgsql()) db.Database.Migrate();
    else db.Database.EnsureCreated();
}

app.UseForwardedHeaders();

// Customer domains: only that business's published pages are reachable there (see Domains).
app.Use(async (ctx, next) =>
{
    var host = ctx.Request.Host.Host.ToLowerInvariant();
    var domains = ctx.RequestServices.GetRequiredService<Domains>();
    if (domains.IsAppHost(host)) { await next(); return; }
    var resolved = await domains.ResolveAsync(host);
    if (resolved == null) { await next(); return; }
    var mapped = Domains.MapPath(resolved, ctx.Request.Path.Value ?? "/");
    if (mapped == null) { ctx.Response.StatusCode = StatusCodes.Status404NotFound; return; }
    ctx.Request.Path = mapped;
    await next();
});
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (app.Configuration["DISABLE_HTTPS_REDIRECT"] != "1") app.UseHttpsRedirection();

app.UseRouting(); // explicit, so the custom-domain path rewrite above happens before routing
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// Social account OAuth: /connect/{provider}?returnUrl=/app/p/1/social → provider → /connect/{provider}/callback
app.MapGet("/connect/{provider}", (string provider, string? returnUrl, HttpContext ctx, SocialService social, IDataProtectionProvider dp) =>
{
    var p = social.ByKey(provider);
    if (p == null || !p.IsConfigured) return Results.BadRequest("This network isn't set up on the server.");
    var (verifier, challenge) = SocialService.Pkce();
    var state = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
    var back = returnUrl is { Length: > 0 } r && r.StartsWith("/app/") ? r : "/app";
    var payload = System.Text.Json.JsonSerializer.Serialize(new OAuthState(state, verifier, provider, back));
    ctx.Response.Cookies.Append("fm_oauth", dp.CreateProtector("Foundrmind.OAuthState").Protect(payload),
        new CookieOptions { HttpOnly = true, Secure = ctx.Request.IsHttps, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromMinutes(10) });
    return Results.Redirect(p.AuthorizeUrl(RedirectUri(ctx, provider), state, challenge));
}).RequireAuthorization();

app.MapGet("/connect/{provider}/callback", async (string provider, string? code, string? state, string? error, HttpContext ctx,
    SocialService social, IDataProtectionProvider dp, ILogger<Program> log) =>
{
    var raw = ctx.Request.Cookies["fm_oauth"];
    ctx.Response.Cookies.Delete("fm_oauth");
    OAuthState? saved = null;
    try { saved = raw == null ? null : System.Text.Json.JsonSerializer.Deserialize<OAuthState>(dp.CreateProtector("Foundrmind.OAuthState").Unprotect(raw)); }
    catch (System.Security.Cryptography.CryptographicException) { }
    var back = saved?.ReturnUrl ?? "/app";
    var sep = back.Contains('?') ? '&' : '?';
    var p = social.ByKey(provider);
    var uid = ctx.User.UserId();
    if (p == null || saved == null || saved.Provider != provider || saved.State != state || uid == null)
        return Results.Redirect($"{back}{sep}social_error=expired");
    if (error != null || code == null)
        return Results.Redirect($"{back}{sep}social_error=denied");
    try
    {
        var (tokens, profile) = await p.ExchangeAsync(code, RedirectUri(ctx, provider), saved.Verifier, ctx.RequestAborted);
        await social.SaveAccountAsync(uid.Value, p, tokens, profile);
        return Results.Redirect($"{back}{sep}connected={provider}");
    }
    catch (Exception ex)
    {
        log.LogWarning(ex, "OAuth exchange with {Provider} failed", provider);
        return Results.Redirect($"{back}{sep}social_error=failed");
    }
}).RequireAuthorization();

// Billing: forms on /app/billing post here (antiforgery-validated), we redirect to Stripe-hosted pages.
app.MapPost("/billing/checkout/{plan}", async (string plan, HttpContext ctx, Billing billing, IDbContextFactory<AppDb> dbf,
    Microsoft.AspNetCore.Antiforgery.IAntiforgery af) =>
{
    await af.ValidateRequestAsync(ctx);
    if (!billing.IsConfigured || billing.PriceFor(plan) == null) return Results.Redirect("/app/billing?error=unavailable");
    await using var db = await dbf.CreateDbContextAsync();
    var user = await db.Users.FindAsync(ctx.User.UserId());
    if (user == null) return Results.Redirect("/login");
    if (user.StripeSubscriptionId.Length > 0) return Results.Redirect("/app/billing?error=has_subscription");
    try { return Results.Redirect(await billing.CreateCheckoutAsync(user, plan, BaseUrl(ctx))); }
    catch (Stripe.StripeException ex) { app.Logger.LogError(ex, "Stripe checkout failed"); return Results.Redirect("/app/billing?error=stripe"); }
}).RequireAuthorization();

app.MapPost("/billing/portal", async (HttpContext ctx, Billing billing, IDbContextFactory<AppDb> dbf,
    Microsoft.AspNetCore.Antiforgery.IAntiforgery af) =>
{
    await af.ValidateRequestAsync(ctx);
    await using var db = await dbf.CreateDbContextAsync();
    var user = await db.Users.FindAsync(ctx.User.UserId());
    if (!billing.IsConfigured || user == null || user.StripeCustomerId.Length == 0) return Results.Redirect("/app/billing?error=unavailable");
    try { return Results.Redirect(await billing.CreatePortalAsync(user, BaseUrl(ctx))); }
    catch (Stripe.StripeException ex) { app.Logger.LogError(ex, "Stripe portal failed"); return Results.Redirect("/app/billing?error=stripe"); }
}).RequireAuthorization();

app.MapPost("/stripe/webhook", async (HttpRequest req, Billing billing) =>
{
    using var reader = new StreamReader(req.Body);
    var json = await reader.ReadToEndAsync();
    return await billing.HandleWebhookAsync(json, req.Headers["Stripe-Signature"].ToString()) ? Results.Ok() : Results.BadRequest();
}).DisableAntiforgery();

// Caddy on-demand TLS asks here before issuing a certificate for a hostname.
app.MapGet("/internal/tls-ask", async (string? domain, Domains domains) =>
{
    var host = Domains.Normalize(domain ?? "");
    if (host == null) return Results.NotFound();
    return domains.IsAppHost(host) || await domains.ResolveAsync(host) != null ? Results.Ok() : Results.NotFound();
});

app.MapPost("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
});

// Public landing-page demo: streams an instant niche snapshot as plain text.
app.MapPost("/api/demo", async (HttpContext ctx, Ai ai, DemoLimiter limiter, DemoInput input) =>
{
    var idea = (input.Idea ?? "").Trim();
    if (idea.Length < 8 || idea.Length > 600)
        return Results.BadRequest("Describe your idea in 8–600 characters.");

    ctx.Response.ContentType = "text/plain; charset=utf-8";
    ctx.Response.Headers.CacheControl = "no-store";

    if (!ai.IsConfigured)
    {
        foreach (var word in Demo.Sample(idea).Split(' '))
        {
            await ctx.Response.WriteAsync(word + " ");
            await ctx.Response.Body.FlushAsync();
            await Task.Delay(18);
        }
        return Results.Empty;
    }

    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    if (!limiter.TryTake(ip))
    {
        await ctx.Response.WriteAsync("You've used today's free demos. Create a free account to keep going — it takes 20 seconds.");
        return Results.Empty;
    }

    await ai.StreamAsync(new AiRequest(Demo.System, Demo.Task(idea), "demo", null, MaxTokens: 1800, Effort: "low"), async t =>
    {
        await ctx.Response.WriteAsync(t);
        await ctx.Response.Body.FlushAsync();
    }, ctx.RequestAborted);
    return Results.Empty;
}).DisableAntiforgery();

// Published funnel pages. Served under a CSP sandbox (opaque origin) so AI-generated HTML can never touch app cookies.
app.MapGet("/p/{slug}", async (string slug, IDbContextFactory<AppDb> dbf, HttpContext ctx) =>
{
    await using var db = await dbf.CreateDbContextAsync();
    var page = await db.Pages.FirstOrDefaultAsync(p => p.Slug == slug && p.IsPublished);
    if (page == null) return Results.NotFound();
    page.Views++;
    await db.SaveChangesAsync();

    ctx.Response.Headers.ContentSecurityPolicy = "sandbox allow-forms allow-scripts allow-popups allow-popups-to-escape-sandbox";
    return Results.Content(WireLeadForm(page.Html, slug), "text/html; charset=utf-8");
});

app.MapPost("/p/{slug}/lead", async (string slug, HttpRequest req, IDbContextFactory<AppDb> dbf) =>
{
    var form = await req.ReadFormAsync();
    if (!string.IsNullOrEmpty(form["_hp"])) return Results.Redirect($"/p/{slug}/thanks"); // bot
    var email = form["email"].ToString().Trim();
    if (email.Length is < 3 or > 200 || !email.Contains('@')) return Results.BadRequest("Please enter a valid email.");

    await using var db = await dbf.CreateDbContextAsync();
    var page = await db.Pages.FirstOrDefaultAsync(p => p.Slug == slug && p.IsPublished);
    if (page == null) return Results.NotFound();
    db.Leads.Add(new Lead
    {
        ProjectId = page.ProjectId,
        Name = Trunc(form["name"].ToString(), 120),
        Email = email,
        Source = $"Page /p/{slug}",
        Notes = Trunc(form["message"].ToString(), 2000),
        NextFollowUp = DateTime.UtcNow.Date.AddDays(1),
    });
    await db.SaveChangesAsync();
    return Results.Redirect($"/p/{slug}/thanks");
}).DisableAntiforgery();

app.MapGet("/p/{slug}/thanks", (string slug) => Results.Content($$"""
    <!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
    <title>Thank you</title><style>body{font-family:system-ui,sans-serif;display:grid;place-items:center;min-height:100vh;margin:0;background:#0b1020;color:#e8ecff}
    .c{text-align:center;padding:24px}a{color:#9fb4ff}</style></head>
    <body><div class="c"><h1>Thank you! 🎉</h1><p>We got your details and will be in touch shortly.</p><p><a href="/p/{{slug}}">← Back</a></p></div></body></html>
    """, "text/html; charset=utf-8"));

app.Run();

static string BaseUrl(HttpContext ctx)
{
    var baseUrl = ctx.RequestServices.GetRequiredService<IConfiguration>()["PUBLIC_BASE_URL"]?.TrimEnd('/');
    return string.IsNullOrEmpty(baseUrl) ? $"{ctx.Request.Scheme}://{ctx.Request.Host}" : baseUrl;
}

static string RedirectUri(HttpContext ctx, string provider) => $"{BaseUrl(ctx)}/connect/{provider}/callback";

static string Trunc(string s, int max) => s.Length > max ? s[..max] : s.Trim();

static string WireLeadForm(string html, string slug)
{
    return Regex.Replace(html, @"<form\b[^>]*data-foundrmind-lead[^>]*>", m =>
    {
        var tag = Regex.Replace(m.Value, @"\s(action|method)\s*=\s*(""[^""]*""|'[^']*'|\S+)", "", RegexOptions.IgnoreCase);
        tag = tag.Insert(tag.Length - 1, $" method=\"post\" action=\"/p/{slug}/lead\"");
        return tag + "<input type=\"text\" name=\"_hp\" tabindex=\"-1\" autocomplete=\"off\" style=\"position:absolute;left:-9999px\" aria-hidden=\"true\">";
    }, RegexOptions.IgnoreCase);
}

record DemoInput(string? Idea);
record OAuthState(string State, string Verifier, string Provider, string ReturnUrl);
