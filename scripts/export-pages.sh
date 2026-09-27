#!/usr/bin/env bash
# Exports the landing page as static HTML into docs/ for GitHub Pages.
# Requires the app running locally: dotnet run --launch-profile http
set -euo pipefail
cd "$(dirname "$0")/.."
BASE="${BASE:-http://localhost:5145}"
OUT=docs
mkdir -p "$OUT"

curl -fsS "$BASE/" -o "$OUT/index.html"
cp wwwroot/app.css wwwroot/favicon.svg "$OUT/"
touch "$OUT/.nojekyll"

export BANNER='<div style="position:relative;z-index:30;background:#ffb547;color:#1a1300;text-align:center;padding:8px 16px;font:600 14px/1.4 system-ui,sans-serif">Static preview: the live app (sign-up, modules, CRM) is not deployed yet. The demo below shows a sample result.</div>'

perl -0pi -e '
  s#<!--Blazor:.*?-->##gs;
  s#<base href="/" />##g;
  s#<script[^>]*src="[^"]*(blazor\.web|ReconnectModal|app\.[a-z0-9]+\.js)[^"]*"[^>]*></script>##g;
  s#<script type="importmap">.*?</script>##gs;
  s#<link[^>]*rel="modulepreload"[^>]*>##g;
  s#<link rel="stylesheet" href="Foundrmind\.[^"]*\.styles\.css"[^>]*>##g;
  s#<blazor-focus-on-navigate[^>]*></blazor-focus-on-navigate>##g;
  s#<dialog id="components-reconnect-modal".*?</dialog>##gs;
  s#<div id="blazor-error-ui".*?</div>##gs;
  s#href="app\.[a-z0-9]+\.css"#href="app.css"#g;
  s#href="favicon\.[a-z0-9]+\.svg"#href="favicon.svg"#g;
  s#href="/(signup|login)"#href="\#demo"#g;
  s#href="/(terms|privacy)"#href="\#faq"#g;
  s#href="/"#href="./"#g;
  s#<body>#<body>$ENV{BANNER}#;
' "$OUT/index.html"

echo "Exported to $OUT/ ($(wc -c < "$OUT/index.html") bytes)"
