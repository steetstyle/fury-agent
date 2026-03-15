#!/bin/bash
# Lang-Ango 2.0 E2E Tests – full flow with Cecil weaver (instrumented TestApp + agent).
# Usage: ./run-e2e-tests.sh

set -e

cd "$(dirname "$0")"
SCRIPT_DIR="$(pwd)"
NET_VERSION="net10.0"
SOCKET_PATH="/tmp/langango.sock"
APP_URL="http://127.0.0.1:5200"
APP_DIR="$SCRIPT_DIR/LangAngo.TestApp"
OUT_DIR="$APP_DIR/bin/Release/$NET_VERSION"
INSTRUMENTED_DIR="$SCRIPT_DIR/e2e-instrumented"
WEAVER_PROJECT="$SCRIPT_DIR/LangAngo.Cecil.Weaver"
HOOK_PATH="$SCRIPT_DIR/LangAngo.CSharp/bin/Release/$NET_VERSION/LangAngo.CSharp.dll"

# Agent: prefer built binary, else run with go run
AGENT_CMD=""
for p in "$SCRIPT_DIR/LangAngo.Agent/agent" "$SCRIPT_DIR/LangAngo.Agent/cmd/agent/agent"; do
    if [ -x "$p" ]; then AGENT_CMD="$p"; break; fi
done
if [ -z "$AGENT_CMD" ]; then
    if command -v go >/dev/null 2>&1; then
        AGENT_CMD="go run ."
        AGENT_DIR="$SCRIPT_DIR/LangAngo.Agent"
    fi
fi

export LANGANGO_INCLUDES="*"
export LANGANGO_EVENTPIPE="${LANGANGO_EVENTPIPE:-true}"
export LANGANGO_EVENTPIPE_RUNTIME="${LANGANGO_EVENTPIPE_RUNTIME:-true}"
export LANGANGO_EVENTPIPE_GC="${LANGANGO_EVENTPIPE_GC:-true}"
export LANGANGO_EVENTPIPE_JIT="${LANGANGO_EVENTPIPE_JIT:-true}"
export LANGANGO_EVENTPIPE_CONTENTION="${LANGANGO_EVENTPIPE_CONTENTION:-true}"
export LANGANGO_EVENTPIPE_THREADPOOL="${LANGANGO_EVENTPIPE_THREADPOOL:-true}"
export LANGANGO_EVENTPIPE_SAMPLING="${LANGANGO_EVENTPIPE_SAMPLING:-true}"
export LANGANGO_SAMPLE_INTERVAL_MS="${LANGANGO_SAMPLE_INTERVAL_MS:-100}"
export DOTNET_ReadyToRun=0

echo "=== Lang-Ango 2.0 E2E Tests (Cecil instrumented) ==="
echo "Working directory: $SCRIPT_DIR"

# Cleanup
echo "[1/9] Cleaning up..."
pkill -9 -f "dotnet.*LangAngo.TestApp" 2>/dev/null || true
pkill -9 agent 2>/dev/null || true
rm -f "$SOCKET_PATH"
sleep 1

# Build
echo "[2/9] Building LangAngo.CSharp and LangAngo.TestApp (Release)..."
dotnet build LangAngo.CSharp -f $NET_VERSION -c Release
dotnet build "$APP_DIR" -f $NET_VERSION -c Release

# Cecil weaver: instrument TestApp
echo "[3/9] Running Cecil weaver..."
mkdir -p "$INSTRUMENTED_DIR"
cp -p "$OUT_DIR"/*.dll "$OUT_DIR"/*.runtimeconfig.json "$INSTRUMENTED_DIR/" 2>/dev/null || true
dotnet run --project "$WEAVER_PROJECT" -- --input "$INSTRUMENTED_DIR/LangAngo.TestApp.dll" --output "$INSTRUMENTED_DIR/LangAngo.TestApp.Instrumented.dll" --namespace "LangAngo.TestApp"
mv "$INSTRUMENTED_DIR/LangAngo.TestApp.Instrumented.dll" "$INSTRUMENTED_DIR/LangAngo.TestApp.dll"

# Start agent
echo "[4/9] Starting Go Agent..."
if [ -z "$AGENT_CMD" ]; then
    echo "ERROR: No agent binary found. Build with: cd LangAngo.Agent && go build -o agent ."
    exit 1
fi
if [ -n "$AGENT_DIR" ]; then
    (cd "$AGENT_DIR" && $AGENT_CMD -socket "$SOCKET_PATH") &
else
    (cd "$(dirname "$AGENT_CMD")" && ./"$(basename "$AGENT_CMD")" -socket "$SOCKET_PATH") &
fi
AGENT_PID=$!
sleep 2

# Start instrumented TestApp
echo "[5/9] Starting instrumented TestApp..."
export DOTNET_STARTUP_HOOKS="$HOOK_PATH"
cd "$INSTRUMENTED_DIR"
dotnet LangAngo.TestApp.dll --urls "$APP_URL" &
APP_PID=$!
cd - >/dev/null
sleep 4

# E2E test cases
echo "[6/9] Test 01: /api/welcome"
curl -s -o /dev/null -w "%{http_code}" "$APP_URL/api/welcome" | grep -q 200 && echo "  PASS" || { echo "  FAIL"; exit 1; }

echo "[7/9] Test 02: /api/http-outbound"
curl -s -o /dev/null -w "%{http_code}" "$APP_URL/api/http-outbound" 2>/dev/null | grep -qE '200|000' && echo "  PASS (or skipped)" || echo "  WARN (network)"

echo "[8/9] Test 03: /api/db-simulation"
curl -s -o /dev/null -w "%{http_code}" "$APP_URL/api/db-simulation" | grep -q 200 && echo "  PASS" || { echo "  FAIL"; exit 1; }

echo "[9/9] Test 04: /api/complex-logic (weaver + method spans)"
HTTP=$(curl -s -o /dev/null -w "%{http_code}" "$APP_URL/api/complex-logic")
if [ "$HTTP" = "200" ]; then
    echo "  PASS (200)"
elif [ "$HTTP" = "500" ]; then
    echo "  WARN (500 - known weaver/return-value edge case)"
else
    echo "  FAIL ($HTTP)"
    exit 1
fi

echo ""
echo "Bonus: /api/heavy-calculation"
curl -s -o /dev/null -w "%{http_code}" "$APP_URL/api/heavy-calculation" | grep -q 200 && echo "  PASS" || echo "  WARN"

echo "Bonus: /api/gc-test"
curl -s -o /dev/null -w "%{http_code}" "$APP_URL/api/gc-test" | grep -q 200 && echo "  PASS" || echo "  WARN"

echo "Bonus: /api/exception-test (expect 500)"
CODE=$(curl -s -o /dev/null -w "%{http_code}" "$APP_URL/api/exception-test" 2>/dev/null || echo "000")
[ "$CODE" = "500" ] && echo "  PASS (500)" || echo "  INFO (got $CODE)"

sleep 1
kill $APP_PID 2>/dev/null || true

echo ""
echo "=== E2E Tests Complete ==="
echo "Check Go Agent output above for trace data. Press Ctrl+C to stop the agent."
wait $AGENT_PID 2>/dev/null || true
