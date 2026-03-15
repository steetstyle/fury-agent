#!/bin/bash

# Lang-Ango 2.0 E2E Test Script
# Usage: ./run-e2e-tests.sh

set -e

cd "$(dirname "$0")"
SCRIPT_DIR="$(pwd)"

SOCKET_PATH="/tmp/langango.sock"
APP_URL="http://127.0.0.1:5200"
AGENT_PATH="$SCRIPT_DIR/LangAngo.Agent/cmd/agent/agent"
APP_PATH="$SCRIPT_DIR/LangAngo.TestApp/bin/Release/net8.0/LangAngo.TestApp.dll"
HOOK_PATH="$SCRIPT_DIR/LangAngo.CSharp/bin/Release/net8.0/LangAngo.CSharp.dll"

echo "=== Lang-Ango 2.0 E2E Tests ==="
echo "Working directory: $(pwd)"

# Cleanup
echo "[1/8] Cleaning up..."
pkill -9 -f "dotnet" 2>/dev/null || true
pkill -9 agent 2>/dev/null || true
rm -f $SOCKET_PATH
sleep 1

# Build projects (Release mode)
echo "[2/8] Building projects (Release)..."
dotnet build LangAngo.CSharp -f net8.0 -c Release
dotnet build LangAngo.TestApp -f net8.0 -c Release

# Start Agent
echo "[3/8] Starting Go Agent..."
if [ ! -f "$AGENT_PATH" ]; then
    echo "ERROR: Agent not found at $AGENT_PATH"
    exit 1
fi
$AGENT_PATH -socket $SOCKET_PATH &
AGENT_PID=$!
sleep 2

# Start TestApp with EventPipe
echo "[4/8] Starting TestApp with EventPipe..."
if [ ! -f "$APP_PATH" ]; then
    echo "ERROR: App not found at $APP_PATH"
    exit 1
fi

cd "$SCRIPT_DIR/LangAngo.TestApp/bin/Release/net8.0"
LANGANGO_SOCKET=$SOCKET_PATH \
LANGANGO_EVENTPIPE=true \
LANGANGO_USE_OPENTELEMETRY=true \
DOTNET_STARTUP_HOOKS="$HOOK_PATH" \
ASPNETCORE_URLS=$APP_URL \
dotnet LangAngo.TestApp.dll &
APP_PID=$!
cd - > /dev/null
sleep 4

# Test 1: Standard Entry (Inbound HTTP)
echo ""
echo "[5/8] Test 01: Standard Entry (Inbound HTTP)"
curl -s $APP_URL/api/welcome
echo ""

# Test 2: Outbound HTTP
echo "[6/8] Test 02: Outbound HttpClient (httpbin.org)"
curl -s $APP_URL/api/http-outbound 2>/dev/null | head -c 100 || echo "Outbound call failed"
echo "..."

# Test 3: DB Simulation
echo ""
echo "[7/8] Test 03: DB Simulation"
curl -s $APP_URL/api/db-simulation
echo ""

# Test 4: GC Event
echo ""
echo "[8/8] Test 04: GC Event Capture"
curl -s $APP_URL/api/gc-test
echo ""

# Test 5: Exception
echo ""
echo "[Bonus] Test 05: Exception Capture"
curl -s $APP_URL/api/exception-test 2>/dev/null || echo "(Expected: 500 Error)"

echo ""
echo "=== E2E Tests Complete ==="
echo "Check Go Agent output above for trace data"

# Keep agent running for inspection
echo "Press Ctrl+C to stop..."
wait $AGENT_PID $APP_PID 2>/dev/null || true
