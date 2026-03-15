using Mono.Cecil;
using Mono.Cecil.Cil;

namespace LangAngo.Cecil.Weaver;

public static class DumpIl
{
    public static void DumpMethod(string assemblyPath, string typeName, string methodName)
    {
        var module = ModuleDefinition.ReadModule(assemblyPath);
        var type = module.Types.FirstOrDefault(t => t.FullName == typeName);
        if (type == null) return;
        var method = type.Methods.FirstOrDefault(m => m.Name == methodName);
        if (method?.Body == null) return;
        Console.WriteLine($"Method: {typeName}.{methodName}");
        Console.WriteLine($"  ReturnType: {method.ReturnType.FullName}");
        Console.WriteLine($"  Variables: {method.Body.Variables.Count}");
        for (var i = 0; i < method.Body.Instructions.Count; i++)
        {
            var instr = method.Body.Instructions[i];
            var operand = instr.Operand is Instruction target ? $"-> {target.OpCode}" : (instr.Operand?.ToString() ?? "");
            Console.WriteLine($"  {i,3}: {instr.OpCode} {operand}");
        }
        Console.WriteLine("  ExceptionHandlers:");
        foreach (var h in method.Body.ExceptionHandlers)
        {
            Console.WriteLine($"    {h.HandlerType}: Try={h.TryStart?.OpCode}-{h.TryEnd?.OpCode} Handler={h.HandlerStart?.OpCode}-{h.HandlerEnd?.OpCode}");
        }
    }
}
