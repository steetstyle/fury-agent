using Mono.Cecil;
using Mono.Cecil.Cil;

namespace LangAngo.Cecil.Weaver;

public static class WeaverRunner
{
    const string LangAngoAssemblyName = "LangAngo.CSharp";
    const string MethodTracerTypeName = "LangAngo.CSharp.Instrumentation.MethodTracer";
    const string MethodEnterName = "MethodEnter";
    const string MethodLeaveName = "MethodLeave";

    public static int Run(string inputPath, string outputPath, string? namespacePrefix, string? classPattern)
    {
        var resolver = new DefaultAssemblyResolver();
        var inputDir = Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? ".";
        resolver.AddSearchDirectory(inputDir);

        var readerParams = new ReaderParameters { AssemblyResolver = resolver };
        using var assembly = AssemblyDefinition.ReadAssembly(inputPath, readerParams);

        var refs = assembly.MainModule.AssemblyReferences.ToList();
        var langAngoRef = refs.FirstOrDefault(r =>
            string.Equals(r.Name, LangAngoAssemblyName, StringComparison.OrdinalIgnoreCase) ||
            (r.Name?.StartsWith("LangAngo.CSharp", StringComparison.OrdinalIgnoreCase) == true));
        if (langAngoRef == null)
            throw new InvalidOperationException(
                $"Target assembly does not reference {LangAngoAssemblyName}. Add a reference and use a type (e.g. MethodTracer.Initialize()) so the reference is emitted.");

        var langAngoPath = Path.Combine(inputDir, LangAngoAssemblyName + ".dll");
        if (!File.Exists(langAngoPath))
            throw new FileNotFoundException($"Cannot find {LangAngoAssemblyName}.dll next to input assembly.", langAngoPath);

        var langAngoAssembly = AssemblyDefinition.ReadAssembly(langAngoPath, readerParams);
        var methodTracerType = langAngoAssembly.MainModule.GetType(MethodTracerTypeName)
            ?? langAngoAssembly.MainModule.Types.FirstOrDefault(t => t.Name == "MethodTracer" && t.Namespace == "LangAngo.CSharp.Instrumentation")
            ?? throw new InvalidOperationException($"Type not found: {MethodTracerTypeName}. Seen types: {string.Join(", ", langAngoAssembly.MainModule.Types.Take(5).Select(t => t.FullName))}");

        var methodEnter = methodTracerType.Methods.FirstOrDefault(m =>
            m.Name == MethodEnterName && m.Parameters.Count == 2
            && m.Parameters[0].ParameterType.FullName == "System.String"
            && (m.Parameters[1].ParameterType.FullName == "System.String" || m.Parameters[1].ParameterType.FullName == "System.String&"));
        if (methodEnter == null)
            methodEnter = methodTracerType.Methods.FirstOrDefault(m =>
                m.Name == MethodEnterName && m.Parameters.Count == 4
                && m.Parameters[0].ParameterType.FullName == "System.String");
        var methodLeave = methodTracerType.Methods.FirstOrDefault(m => m.Name == MethodLeaveName && m.Parameters.Count == 0);
        if (methodEnter == null || methodLeave == null)
            throw new InvalidOperationException("MethodTracer.MethodEnter(string, string) or MethodLeave() not found.");

        var methodEnterRef = assembly.MainModule.ImportReference(methodEnter);
        var methodLeaveRef = assembly.MainModule.ImportReference(methodLeave);

        int count = 0;
        foreach (var type in assembly.MainModule.Types.Where(t => t.Name != "<Module>"))
        {
            if (!MatchesTypeFilter(type, namespacePrefix, classPattern))
                continue;

            foreach (var method in type.Methods)
            {
                if (!IsInstrumentable(method))
                    continue;

                InstrumentMethod(method, methodEnterRef, methodLeaveRef, methodEnter.Parameters.Count);
                count++;
            }
        }

        var writerParams = new WriterParameters { WriteSymbols = false };
        assembly.Write(outputPath, writerParams);
        return count;
    }

    static bool MatchesTypeFilter(TypeDefinition type, string? namespacePrefix, string? classPattern)
    {
        if (type.Namespace == null)
            return false;
        if (!string.IsNullOrEmpty(namespacePrefix) && !type.Namespace.StartsWith(namespacePrefix, StringComparison.Ordinal))
            return false;
        if (!string.IsNullOrEmpty(classPattern))
        {
            var pattern = classPattern.Trim();
            var name = type.Name;
            if (pattern.Contains('*'))
            {
                var prefix = pattern.Replace("*", "");
                if (!name.StartsWith(prefix) && !name.Contains(prefix))
                    return false;
            }
            else if (!name.Contains(pattern) && !name.EndsWith(pattern))
                return false;
        }
        return true;
    }

    static bool IsInstrumentable(MethodDefinition method)
    {
        if (method.IsAbstract || method.IsConstructor || method.IsStatic)
            return false;
        if (!method.IsPublic && !method.IsAssembly)
            return false;
        if (method.IsGetter || method.IsSetter || method.IsAddOn || method.IsRemoveOn)
            return false;
        if (method.Body == null || !method.Body.Instructions.Any())
            return false;
        if (method.HasCustomAttributes && method.CustomAttributes.Any(a =>
                a.AttributeType.FullName == "System.Runtime.CompilerServices.AsyncStateMachineAttribute"))
            return false;
        var ret = method.ReturnType.FullName;
        if (ret == "System.Threading.Tasks.Task" || ret.StartsWith("System.Threading.Tasks.Task`1", StringComparison.Ordinal))
            return false;
        return true;
    }

    static void InstrumentMethod(MethodDefinition method, MethodReference methodEnterRef, MethodReference methodLeaveRef, int methodEnterParamCount)
    {
        var body = method.Body!;
        var processor = body.GetILProcessor();
        var methodName = method.Name;
        var declaringTypeFullName = method.DeclaringType?.FullName ?? "";
        var isVoid = method.ReturnType.FullName == "System.Void";
        var firstInstruction = body.Instructions[0];

        // 1. Entry: MethodEnter at the start
        var ldstrName = processor.Create(OpCodes.Ldstr, methodName);
        var ldstrType = processor.Create(OpCodes.Ldstr, declaringTypeFullName);
        var callEnter = processor.Create(OpCodes.Call, methodEnterRef);
        processor.InsertBefore(firstInstruction, callEnter);
        if (methodEnterParamCount >= 4)
        {
            processor.InsertBefore(callEnter, processor.Create(OpCodes.Ldc_I4_0));
            processor.InsertBefore(callEnter, processor.Create(OpCodes.Ldnull));
        }
        processor.InsertBefore(callEnter, ldstrType);
        processor.InsertBefore(ldstrType, ldstrName);

        // 2. Return variable and finally/exit block (create before redirecting rets)
        VariableDefinition? returnLocal = null;
        Instruction callLeaveInstr = processor.Create(OpCodes.Call, methodLeaveRef);
        Instruction endFinally = processor.Create(OpCodes.Endfinally);
        Instruction finalRet = processor.Create(OpCodes.Ret);
        Instruction? loadResult = null;

        if (!isVoid)
        {
            body.InitLocals = true;
            returnLocal = new VariableDefinition(method.ReturnType);
            body.Variables.Add(returnLocal);
            loadResult = processor.Create(OpCodes.Ldloc, returnLocal);
        }

        processor.Append(callLeaveInstr);
        processor.Append(endFinally);
        if (loadResult != null)
            processor.Append(loadResult);
        processor.Append(finalRet);

        // 3. Redirect all original Ret to Leave (do not touch finalRet)
        foreach (var instr in body.Instructions.ToList())
        {
            if (instr.OpCode != OpCodes.Ret || instr == finalRet)
                continue;
            if (!isVoid)
            {
                var stloc = processor.Create(OpCodes.Stloc, returnLocal!);
                processor.InsertBefore(instr, stloc);
            }
            instr.OpCode = OpCodes.Leave;
            instr.Operand = isVoid ? callLeaveInstr : loadResult;
        }

        // 4. Exception handler: Try [first, callLeave), Handler [callLeave, HandlerEnd) = call + Endfinally only
        var tryStart = body.Instructions[0];
        Instruction handlerEnd = isVoid ? finalRet : loadResult!;
        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally)
        {
            TryStart = tryStart,
            TryEnd = callLeaveInstr,
            HandlerStart = callLeaveInstr,
            HandlerEnd = handlerEnd
        });
    }
}
