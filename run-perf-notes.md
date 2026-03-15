# Performance and load (Test Strategy §4)

- **Baseline RPS:** Run TestApp without instrumentation (no weaver, no LANGANGO_INCLUDES). Use `hey` or `wrk` against e.g. `/api/welcome` or `/api/complex-logic`. Record requests per second.
- **Instrumented RPS:** Run TestApp with weaver + LANGANGO_INCLUDES=* (or limited namespace). Same endpoint, same tool. Target: **<10% drop** vs baseline.
- **Memory:** Unit test `Perf_Memory_100k_calls_stack_drains_and_RAM_stable` in LangAngo.CSharp.Tests verifies 100k MethodEnter/MethodLeave cycles complete, all spans drained, and channel empty after GC.

Example (bash):
```bash
# Baseline
dotnet run --project LangAngo.TestApp -- --urls http://127.0.0.1:5202 &
hey -n 10000 -c 10 http://127.0.0.1:5202/api/welcome

# Instrumented (build, weaver, run instrumented DLL with LANGANGO_INCLUDES=*)
# Same hey command; compare RPS.
```
