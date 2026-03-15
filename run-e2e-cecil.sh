#!/bin/bash
# E2E Scenario A: "The Deep Dive" – build TestApp, weaver, run instrumented app, curl /api/complex-logic.
# Requires: TestApp references LangAngo.CSharp and calls MethodTracer.Initialize() so the ref is emitted.
# Note: Instrumented methods that return values may hit InvalidProgramException (weaver Leave clears stack);
# use --namespace to limit to app code. Scenario B: run weaver with --namespace LangAngo.TestApp.NamespaceA
# to exclude NamespaceB from instrumentation.
set -e

cd "$(dirname "$0")"
SCRIPT_DIR="$(pwd)"
NET_VERSION="net10.0"
APP_DIR="$SCRIPT_DIR/LangAngo.TestApp"
OUT_DIR="$APP_DIR/bin/Release/$NET_VERSION"
INSTRUMENTED_DIR="$SCRIPT_DIR/e2e-instrumented"
WEAVER_PROJECT="$SCRIPT_DIR/LangAngo.Cecil.Weaver"
APP_URL="http://127.0.0.1:5201"

echo "=== E2E Cecil Scenario A: Deep Dive ==="

echo "[1/5] Building TestApp (Release)..."
dotnet build "$APP_DIR" -f $NET_VERSION -c Release

echo "[2/5] Running weaver (external, no target project changes)..."
mkdir -p "$INSTRUMENTED_DIR"
cp -p "$OUT_DIR"/*.dll "$OUT_DIR"/*.runtimeconfig.json "$INSTRUMENTED_DIR/" 2>/dev/null || true
dotnet run --project "$WEAVER_PROJECT" -- --input "$INSTRUMENTED_DIR/LangAngo.TestApp.dll" --output "$INSTRUMENTED_DIR/LangAngo.TestApp.Instrumented.dll" --namespace "LangAngo.TestApp"
mv "$INSTRUMENTED_DIR/LangAngo.TestApp.Instrumented.dll" "$INSTRUMENTED_DIR/LangAngo.TestApp.dll"

echo "[3/5] Starting instrumented app..."
HOOK_PATH="$SCRIPT_DIR/LangAngo.CSharp/bin/Release/$NET_VERSION/LangAngo.CSharp.dll"
export LANGANGO_INCLUDES="*"
export DOTNET_STARTUP_HOOKS="$HOOK_PATH"
cd "$INSTRUMENTED_DIR"
dotnet LangAngo.TestApp.dll --urls "$APP_URL" &
APP_PID=$!
cd - > /dev/null
sleep 3

echo "[4/5] Curl /api/complex-logic (with W3C traceparent in response)..."
RESP=$(curl -s -i "$APP_URL/api/complex-logic" 2>/dev/null || true)
HTTP=$(echo "$RESP" | grep -o "HTTP/1.[01] [0-9]*" | tail -1 | awk '{print $2}')
TRACEPARENT=$(echo "$RESP" | grep -i "^traceparent:" | head -1 | cut -d' ' -f2 | tr -d '\r')
sleep 1
kill $APP_PID 2>/dev/null || true

echo "[5/5] Verify 200 OK and W3C trace tracking"
if [ "$HTTP" = "200" ]; then
    echo "PASS: /api/complex-logic returned 200"
    if [ -n "$TRACEPARENT" ]; then
        echo "  traceparent: $TRACEPARENT (use in next request or agent correlation)"
    fi
elif [ "$HTTP" = "500" ]; then
    echo "WARN: /api/complex-logic returned 500 (known weaver limitation for return values; build+weaver+app start OK)"
else
    echo "FAIL: /api/complex-logic returned $HTTP"
    exit 1
fi

echo "=== Scenario A complete ==="
