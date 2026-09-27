# Foundrmind

AI co-founder SaaS: describe a business once, then research the niche, publish a landing page (leads flow into a built-in CRM), and generate content, email sequences, product outlines and YouTube scripts, all from one shared business brief.

**Stack:** .NET 10 Blazor Web App (static SSR landing + interactive server app) · EF Core (SQLite locally, PostgreSQL in production) · official Anthropic C# SDK (`claude-opus-5`, streaming, server-side refusal fallback to `claude-opus-4-8`, live web search in the Niche Profiler).

## Run locally

```bash
dotnet run --launch-profile http   # http://localhost:5145
```

Without `ANTHROPIC_API_KEY` the app runs in **manual mode**: each module shows the full prompt to paste into claude.ai and accepts the answer back. The landing-page demo streams a canned sample.

To enable AI locally (never commit the key):

```bash
dotnet user-secrets init
dotnet user-secrets set ANTHROPIC_API_KEY "<your key>"
```

## Configuration

| Setting | Purpose |
|---|---|
| `ANTHROPIC_API_KEY` | Claude API key (required for AI) |
| `ANTHROPIC_MODEL` | Override model (default `claude-opus-5`) |
| `ConnectionStrings__Default` | `Host=...` → PostgreSQL, otherwise SQLite file (`Data Source=foundrmind.db`) |
| `DATA_PROTECTION_PATH` | Persistent folder for auth-cookie keys (set in Docker image to `/data/keys`) |
| `DISABLE_HTTPS_REDIRECT` | `1` when TLS is terminated by a proxy (Caddy, Azure) |

Schema is created on first start (`EnsureCreated`). Switch to EF migrations before the first schema change in production.

## Deploy: Hetzner (VPS + Docker)

1. Create a Hetzner Cloud server (CX22 or larger, Ubuntu 24.04) and install Docker.
2. Point the domain's A/AAAA records at the server.
3. On the server:
   ```bash
   git clone <repo> foundrmind && cd foundrmind
   cp .env.example .env    # set DOMAIN, POSTGRES_PASSWORD, ANTHROPIC_API_KEY
   docker compose up -d --build
   ```
   Caddy obtains HTTPS certificates automatically. Postgres data and cookie keys live in named volumes.
4. Backups: `docker compose exec db pg_dump -U foundrmind foundrmind > backup.sql` (cron it), or enable Hetzner server backups.

## Deploy: Azure

- **Database:** Azure Database for PostgreSQL – Flexible Server (Burstable B1ms is enough to start).
- **App:** Azure Container Apps or App Service for Containers, built from the `Dockerfile` (push to Azure Container Registry).
- **Settings:** `ConnectionStrings__Default` (with `SSL Mode=Require`), `ANTHROPIC_API_KEY` as a secret, `DATA_PROTECTION_PATH` pointing at a mounted Azure Files share (or keep `/data/keys` on a persistent volume).
- Custom domain + managed certificate in the portal.

## Structure

- `Components/Pages/Home.razor`: landing page with the live no-signup demo (`POST /api/demo`, per-IP and daily caps)
- `Components/Pages/App/*`: dashboard, onboarding wizard, launch path, module runner, CRM, published pages
- `Services/Modules.cs`: module definitions and prompts; `Services/Ai.cs`: Claude streaming client
- `Program.cs`: auth, DB, demo endpoint, published pages `/p/{slug}` (CSP-sandboxed) and lead capture `/p/{slug}/lead`
