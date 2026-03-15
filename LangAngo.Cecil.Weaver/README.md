# LangAngo.Cecil.Weaver

Post-build IL weaver that injects `MethodTracer.MethodEnter` / `MethodLeave` into a target assembly. **The target project is not modified** (no ProjectReference, no AfterBuild); you run the weaver externally against the built DLL.

## Usage

```bash
dotnet run --project LangAngo.Cecil.Weaver -- --input <path-to-built-app.dll> [--output <instrumented.dll>] [--namespace <prefix>] [--class-pattern <pattern>]
```

- **--input** — Path to the target assembly (must reference `LangAngo.CSharp`; `LangAngo.CSharp.dll` must be next to it or resolvable).
- **--output** — Output path for the instrumented assembly (default: overwrites input).
- **--namespace** — Instrument only types in this namespace prefix (e.g. `MyApp.Services`).
- **--class-pattern** — Instrument only types matching the pattern (e.g. `*Controller`, `*Service`).

Example (target project unchanged):

```bash
dotnet build MyApp/MyApp.csproj -c Release
dotnet run --project LangAngo.Cecil.Weaver -- --input MyApp/bin/Release/net10.0/MyApp.dll --output ./instrumented/MyApp.dll
# Run the instrumented copy: dotnet ./instrumented/MyApp.dll
```

## Cecil vs Profiler

Both paths use the same entry point in **LangAngo.CSharp**:

- **Cecil (this weaver):** Rewrites the assembly on disk; injects `MethodTracer.MethodEnter(string, string?)` and a try/finally with `MethodTracer.MethodLeave()`.
- **Profiler (future):** CLR Profiling API will call the same `MethodTracer` methods at runtime without modifying the assembly.

The same pipeline (SpanChannel, TraceContext) is used in both cases.
