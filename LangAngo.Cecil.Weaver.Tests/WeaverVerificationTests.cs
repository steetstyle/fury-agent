using System.Reflection;
using LangAngo.Cecil.Weaver;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace LangAngo.Cecil.Weaver.Tests;

public class WeaverVerificationTests
{
    private static string GetTestAssemblyPath()
    {
        var asm = typeof(LangAngo.CSharp.Tests.SymbolMapCacheTests).Assembly;
        return asm.Location;
    }

    private static (string inputPath, string outputPath) RunWeaver()
    {
        var inputPath = GetTestAssemblyPath();
        var outputPath = Path.Combine(Path.GetTempPath(), "LangAngo.Weaver.Verification." + Guid.NewGuid().ToString("N") + ".dll");
        var count = WeaverRunner.Run(inputPath, outputPath, null, null);
        Assert.True(count >= 1, "Weaver should instrument at least one method");
        return (inputPath, outputPath);
    }

    [Fact]
    public void V01_MetadataIntegrity_weaver_output_loads()
    {
        var (_, outputPath) = RunWeaver();
        try
        {
            var assembly = Assembly.LoadFrom(outputPath);
            Assert.NotNull(assembly);
            Assert.True(assembly.GetTypes().Length > 0);
        }
        finally
        {
            try { File.Delete(outputPath); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void V02_InstructionInjection_method_starts_with_ldstr_and_call_MethodEnter()
    {
        var (_, outputPath) = RunWeaver();
        try
        {
            var module = ModuleDefinition.ReadModule(outputPath);
            var instrumentedType = module.Types.FirstOrDefault(t => t.Name != "<Module>" && t.Methods.Any(m => m.Body?.ExceptionHandlers.Count > 0));
            Assert.NotNull(instrumentedType);
            var doWork = instrumentedType.Methods.FirstOrDefault(m => m.Body?.ExceptionHandlers.Count > 0);
            Assert.NotNull(doWork);
            var instructions = doWork!.Body!.Instructions;
            Assert.True(instructions.Count >= 3);
            var il = instructions.Take(5).Select(i => (i.OpCode.Code, i.Operand?.ToString())).ToList();
            Assert.Equal(Code.Ldstr, instructions[0].OpCode.Code);
            Assert.Equal(Code.Ldstr, instructions[1].OpCode.Code);
            Assert.Equal(Code.Call, instructions[2].OpCode.Code);
            var callOperand = instructions[2].Operand?.ToString() ?? "";
            Assert.Contains("MethodEnter", callOperand);
        }
        finally
        {
            try { File.Delete(outputPath); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void V03_FinallyBlockWrapping_method_has_finally_with_MethodLeave()
    {
        var (_, outputPath) = RunWeaver();
        try
        {
            var module = ModuleDefinition.ReadModule(outputPath);
            var instrumentedType = module.Types.FirstOrDefault(t => t.Name != "<Module>" && t.Methods.Any(m => m.Body?.ExceptionHandlers.Count > 0));
            Assert.NotNull(instrumentedType);
            var doWork = instrumentedType.Methods.FirstOrDefault(m => m.Body?.ExceptionHandlers.Count > 0);
            Assert.NotNull(doWork);
            var handlers = doWork!.Body!.ExceptionHandlers;
            var finallyHandler = handlers.FirstOrDefault(h => h.HandlerType == ExceptionHandlerType.Finally);
            Assert.NotNull(finallyHandler);
            var hasMethodLeaveInMethod = doWork.Body.Instructions.Any(i => i.OpCode.Code == Code.Call &&
                ((i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "MethodLeave") || (i.Operand?.ToString() ?? "").Contains("MethodLeave")));
            Assert.True(hasMethodLeaveInMethod, "Instrumented method should contain call to MethodTracer.MethodLeave (in finally)");
        }
        finally
        {
            try { File.Delete(outputPath); } catch { /* ignore */ }
        }
    }
}
