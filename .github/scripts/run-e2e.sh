#!/usr/bin/env bash
# Publish the Web app for E2E, serve it, and run the E2E suite against it: the one sequence CI
# (.github/workflows/tests.yml) and the build server (BUILD-RUNBOOK.md, step 6) both run.
#
#   .github/scripts/run-e2e.sh                  # quiet: the server's request log goes to a file
#   .github/scripts/run-e2e.sh -v               # verbose: the server log and the full publish output
#   .github/scripts/run-e2e.sh -- --filter X    # anything after -- goes to dotnet test
#
# Options:
#   -v, --verbose            stream the server's request log and the full publish output
#   -c, --configuration C    Release (default) or Debug
#   -p, --port N             the port to serve on (default 5000, or $E2E_PORT)
#   -h, --help               this text
#
# Quiet by default, because the server logs one line per file served (thousands per run): on CI
# that only fills a log, but on the build server it fills an agent session's context. On a
# failure, the last 50 lines of the server log are printed whatever the mode.
#
# Needs Chromium for the installed Playwright version (CI installs it in the step before; the
# build server copies it in, see the build-server notes). Exits with the tests' exit code.
set -uo pipefail

usage() { sed -n '2,/^set /p' "$0" | sed '$d' | sed 's/^# \{0,1\}//'; }

verbose=false
config=Release
port="${E2E_PORT:-5000}"
while (( $# )); do
  case "$1" in
    -v|--verbose)        verbose=true ;;
    -c|--configuration)  config="${2:?-c needs a value}"; shift ;;
    -p|--port)           port="${2:?-p needs a value}"; shift ;;
    -h|--help)           usage; exit 0 ;;
    --)                  shift; break ;;
    *)                   echo "run-e2e.sh: unknown option '$1' (try --help)" >&2; exit 2 ;;
  esac
  shift
done

cd "$(git rev-parse --show-toplevel)" || exit 2

quiet=(-v quiet -nologo)
$verbose && quiet=()

# #710: -p:E2E=true compiles in the test-only fake-auth shim so the authed a11y surfaces are
# reachable. Never passed by the prod deploy.
echo "== Publishing the app ($config)"
dotnet publish src/Consultologist.Web/Consultologist.Web.csproj -c "$config" -o publish -p:E2E=true "${quiet[@]}" || exit $?

# The SPA-fallback server lets Blazor deep links (/history, /consults/{id}) load. It is stopped by
# its process id: `pkill -f serve.py` would also match a shell whose command line holds that text.
log=$(mktemp "${TMPDIR:-/tmp}/e2e-server.XXXXXX.log")
echo "== Serving publish/wwwroot on port $port (server log: $($verbose && echo streamed || echo "$log"))"
if $verbose; then
  python3 tests/Consultologist.Web.E2E.Tests/serve.py "$port" publish/wwwroot &
else
  python3 tests/Consultologist.Web.E2E.Tests/serve.py "$port" publish/wwwroot >"$log" 2>&1 &
fi
srv=$!
trap 'kill "$srv" 2>/dev/null' EXIT

if ! curl -sf --retry 30 --retry-connrefused --retry-delay 1 "http://localhost:$port/" >/dev/null; then
  echo "run-e2e.sh: the server did not answer on port $port" >&2
  $verbose || tail -n 50 "$log" >&2
  exit 1
fi

echo "== Running the E2E suite"
E2E_BASE_URL="http://localhost:$port" \
  dotnet test tests/Consultologist.Web.E2E.Tests/Consultologist.Web.E2E.Tests.csproj -c "$config" "$@"
rc=$?

if (( rc != 0 )) && ! $verbose; then
  echo "== The last 50 lines of the server log ($log)"
  tail -n 50 "$log"
elif (( rc == 0 )); then
  rm -f "$log"
fi
exit $rc
