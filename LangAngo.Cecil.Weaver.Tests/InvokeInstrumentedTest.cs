using System.Reflection;
using LangAngo.Cecil.Weaver;
using Xunit;

namespace LangAngo.Cecil.Weaver.Tests;

public class InvokeInstrumentedTest
{
    [Fact]
    public void Invoke_Handle_after_weaving_TestApp_returns_successfully()
    {
        var testAppDir = Path.Combine(Path.GetDirectoryName(typeof(InvokeInstrumentedTest).Assembly.Location)!, "..", "..", "..", "..", "LangAngo.TestApp", "bin", "Release", "net10.0");
        var testAppDll = Path.Combine(testAppDir, "LangAngo.TestApp.dll");
        if (!File.Exists(testAppDll))
            return;

        var outPath = Path.Combine(Path.GetTempPath(), "InvokeInstrumented_" + Guid.NewGuid().ToString("N") + ".dll");
        try
        {
            var count = WeaverRunner.Run(testAppDll, outPath, "LangAngo.TestApp", null);
            Assert.True(count >= 1);

            var dir = Path.GetDirectoryName(outPath)!;
            foreach (var f in Directory.EnumerateFiles(testAppDir, "*.dll"))
                File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), true);

            var asm = Assembly.LoadFrom(outPath);
            var type = asm.GetType("LangAngo.TestApp.ComplexLogicController");
            Assert.NotNull(type);
            var instance = Activator.CreateInstance(type!);
            var method = type!.GetMethod("Handle");
            Assert.NotNull(method);
            var result = method!.Invoke(instance, null);
            Assert.NotNull(result);
        }
        finally
        {
            try { File.Delete(outPath); } catch { }
        }
    }
}
