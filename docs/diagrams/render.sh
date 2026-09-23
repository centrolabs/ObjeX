#!/usr/bin/env bash
# Splits objex.mmd at "%%% Title" lines and renders each diagram twice with mermaid-cli:
# NN-name.svg (light, white background) and NN-name.dark.svg (mermaid dark theme, GitHub dark background).
# docs/architecture.md picks one per colour scheme with <picture>. Run after every change to objex.mmd.
# Needs Node; the first run downloads Chrome for puppeteer (~150 MB).
set -euo pipefail
cd "$(dirname "$0")"
rm -f ./*.svg
tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
printf '%s' '{"fontFamily": "system-ui, sans-serif", "themeVariables": {"fontFamily": "system-ui, sans-serif"}}' > "$tmp/config.json"
awk -v dir="$tmp" '
  function slug(t) { t = tolower(t); gsub(/[^a-z0-9]+/, "-", t); gsub(/^-|-$/, "", t); return t }
  BEGIN { n = 1; title = "Architecture"; file = dir "/" sprintf("%02d", n) "-" slug(title) ".mmd" }
  /^%%% / { close(file); n++; title = substr($0, 5); file = dir "/" sprintf("%02d", n) "-" slug(title) ".mmd"; next }
  { print > file }
' objex.mmd
for f in "$tmp"/*.mmd; do
  name="$(basename "${f%.mmd}")"
  npx -y @mermaid-js/mermaid-cli -q -i "$f" -o "$name.svg" -b white -c "$tmp/config.json"
  sed 's/"theme": "base"/"theme": "dark"/' "$f" > "$tmp/dark.mmd"
  npx -y @mermaid-js/mermaid-cli -q -i "$tmp/dark.mmd" -o "$name.dark.svg" -b "#0d1117" -c "$tmp/config.json"
  echo "rendered $name.svg + $name.dark.svg"
done
