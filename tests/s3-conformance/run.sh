#!/usr/bin/env bash
# Runs ceph/s3-tests against this checkout of ObjeX and prints the in-scope pass rate.
#
#   tests/s3-conformance/run.sh                  everything in scope
#   tests/s3-conformance/run.sh -k multipart     extra arguments go to pytest
#   FAILURES=1 tests/s3-conformance/run.sh       also list what still fails
#   MIN_PASS_RATE=80 tests/s3-conformance/run.sh exit 1 below that rate (for CI)
#
# Needs dotnet, python3 and git. Starts its own ObjeX on ports 19000/19001 with throwaway data and random
# credentials, and keeps the s3-tests clone, the virtualenv and the logs in .work/ next to this script.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
work="$here/.work"
s3_tests_commit=5522d1c351f75bc00ae0f64f742f3f095f5939d9
s3_port="${OBJEX_S3_PORT:-19000}"
ui_port="${OBJEX_UI_PORT:-19001}"

mkdir -p "$work"
if [ ! -d "$work/s3-tests/.git" ]; then
    git init -q "$work/s3-tests"
    git -C "$work/s3-tests" remote add origin https://github.com/ceph/s3-tests.git
fi
if [ "$(git -C "$work/s3-tests" rev-parse -q --verify HEAD || true)" != "$s3_tests_commit" ]; then
    git -C "$work/s3-tests" fetch -q --depth 1 origin "$s3_tests_commit"
    git -C "$work/s3-tests" checkout -q FETCH_HEAD
fi
if [ ! -x "$work/venv/bin/pytest" ]; then
    python3 -m venv "$work/venv"
    "$work/venv/bin/pip" install -q -r "$work/s3-tests/requirements.txt" pytest-timeout
fi

random() { LC_ALL=C tr -dc "$1" </dev/urandom | head -c "$2" || true; }
data="$(mktemp -d)"
access_key="OBX$(random 'A-Z0-9' 17)"
secret_key="$(random 'A-Za-z0-9' 40)"
sed -e "s/@S3_PORT@/$s3_port/" -e "s/@ACCESS_KEY@/$access_key/" -e "s/@SECRET_KEY@/$secret_key/" \
    "$here/s3tests.conf.template" > "$data/s3tests.conf"

dotnet build "$repo/src/ObjeX.Api" -c Release -v q --nologo >"$work/build.log" 2>&1 || { tail -20 "$work/build.log"; exit 1; }

(
    cd "$repo/src/ObjeX.Api"
    exec env ASPNETCORE_ENVIRONMENT=Production \
        Server__S3Port="$s3_port" Server__UiPort="$ui_port" \
        ConnectionStrings__DefaultConnection="Data Source=$data/objex.db" \
        Storage__BasePath="$data/blobs" \
        DefaultAdmin__Password="$(random 'A-Za-z0-9' 24)" \
        Seed__S3Credential__AccessKeyId="$access_key" \
        Seed__S3Credential__SecretAccessKey="$secret_key" \
        Serilog__MinimumLevel__Default=Warning \
        dotnet bin/Release/net10.0/ObjeX.Api.dll
) >"$work/objex.log" 2>&1 &
server=$!
trap 'kill "$server" 2>/dev/null || true; rm -rf "$data"' EXIT

for _ in $(seq 1 60); do
    curl -sf "http://127.0.0.1:$ui_port/health" >/dev/null && break
    kill -0 "$server" 2>/dev/null || { tail -20 "$work/objex.log"; exit 1; }
    sleep 1
done
curl -sf "http://127.0.0.1:$ui_port/health" >/dev/null || { echo "ObjeX did not start; see $work/objex.log"; exit 1; }

markers="$(grep -v '^#' "$here/excluded-markers.txt" | grep . | paste -sd '|' - | sed 's/|/ or /g')"
(
    cd "$work/s3-tests"
    S3TEST_CONF="$data/s3tests.conf" "$work/venv/bin/pytest" s3tests/functional/test_s3.py \
        -m "not ($markers)" --timeout=60 -q -p no:cacheprovider --junitxml="$work/results.xml" "$@"
) >"$work/pytest.log" 2>&1 || true

"$work/venv/bin/python" "$here/summarize.py" "$work/results.xml" --min "${MIN_PASS_RATE:-0}" ${FAILURES:+--failures}
