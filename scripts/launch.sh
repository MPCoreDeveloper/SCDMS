#!/usr/bin/env bash
# ============================================================================
# SCDMS — Cross-platform dev launcher (Linux / macOS)
# Builds (if needed) and starts SCDMS, then opens the browser.
# Usage:
#   ./launch.sh                  # build + run + open browser
#   ./launch.sh --no-build       # run existing build
#   ./launch.sh --port 5443      # custom port
# ============================================================================
set -euo pipefail

NO_BUILD=false
PORT=5443
CONFIGURATION="Release"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --no-build)          NO_BUILD=true; shift ;;
        --port)              PORT="$2"; shift 2 ;;
        --configuration|-c)  CONFIGURATION="$2"; shift 2 ;;
        --help|-h)
            echo "Usage: $0 [--no-build] [--port 5443] [--configuration Release]"
            exit 0
            ;;
        *) echo "Unknown argument: $1"; exit 1 ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/../src/SCDMS/SCDMS.csproj"
URL="https://localhost:${PORT}"

echo "=== SCDMS — Sharp Core Database Management System ==="

if [ "$NO_BUILD" = false ]; then
    echo "[1/3] Building SCDMS ($CONFIGURATION)..."
    dotnet build "$PROJECT" -c "$CONFIGURATION" --nologo -v q
fi

echo "[2/3] Starting SCDMS on $URL ..."
export SCDMS__HttpsPort="$PORT"

dotnet run --project "$PROJECT" --no-build -c "$CONFIGURATION" &
APP_PID=$!

echo "[3/3] Opening browser..."
trap 'kill $APP_PID 2>/dev/null || true' EXIT INT TERM

sleep 2
READY=false
for _ in $(seq 1 40); do
    if curl -k -s -o /dev/null --connect-timeout 2 "$URL"; then
        READY=true; break
    fi
    sleep 0.5
done

if [ "$READY" = true ]; then
    echo "SCDMS running at $URL"
    if command -v xdg-open >/dev/null 2>&1; then
        xdg-open "$URL" >/dev/null 2>&1 || true
    elif command -v open >/dev/null 2>&1; then
        open "$URL" >/dev/null 2>&1 || true
    else
        echo "Open $URL in your browser."
    fi
else
    echo "SCDMS did not respond in time. Check console output above." >&2
fi

wait "$APP_PID"
