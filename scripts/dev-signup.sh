#!/usr/bin/env bash
# Local dev only: creates the test account from dev-test-account.md (gitignored) via the real signup form.
set -euo pipefail
cd "$(dirname "$0")/.."
BASE="${BASE:-http://localhost:5145}"
EMAIL=$(sed -n 's/^Email: //p' dev-test-account.md)
PASS=$(sed -n 's/^Password: //p' dev-test-account.md)
JAR=$(mktemp)
TOKEN=$(curl -fsS -c "$JAR" "$BASE/signup" | grep -o 'name="__RequestVerificationToken" value="[^"]*"' | head -1 | sed 's/.*value="//; s/"$//')
curl -sS -b "$JAR" -c "$JAR" -o /dev/null -w "signup: %{http_code} -> %{redirect_url}\n" -X POST "$BASE/signup" \
  --data-urlencode "__RequestVerificationToken=$TOKEN" --data-urlencode "_handler=signup" \
  --data-urlencode "Input.Name=Test Founder" --data-urlencode "Input.Email=$EMAIL" --data-urlencode "Input.Password=$PASS"
rm -f "$JAR"
