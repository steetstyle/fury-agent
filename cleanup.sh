#!/bin/bash

echo "=== LangAngo Cleanup ==="

pkill -f "cmd/agent" 2>/dev/null
pkill -f "LangAngo.Agent" 2>/dev/null
pkill -f "LangAngo.TestApp" 2>/dev/null

fuser -k 5000/tcp 2>/dev/null
fuser -k 8080/tcp 2>/dev/null

rm -f /tmp/langango.sock
rm -f /tmp/langango_cmd.sock

rm -f /tmp/agent.log
rm -f /tmp/testapp.log

echo "=== Cleanup Complete ==="
