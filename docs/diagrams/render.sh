#!/usr/bin/env bash
# Splits objex.mmd at "%%% Title" lines and renders one SVG per diagram with mermaid-cli.
# Needs Node; the first run downloads Chrome for puppeteer (~150 MB). Run after every change to objex.mmd.
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
  out="$(basename "${f%.mmd}").svg"
  npx -y @mermaid-js/mermaid-cli -q -i "$f" -o "$out" -b transparent -c "$tmp/config.json"
  echo "rendered $out"
done
