using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace LangAngo.SourceGenerator;

[Generator]
public class AutoMethodTracerGenerator : ISourceGenerator
{
    private string _includes = "";
    private string _excludes = "";

    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new AutoTraceSyntaxReceiver());
    }

    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxContextReceiver is not AutoTraceSyntaxReceiver receiver)
            return;

        var compilation = context.Compilation;
        
        foreach (var method in receiver.CandidateMethods)
        {
            var model = compilation.GetSemanticModel(method.SyntaxTree);
            var methodSymbol = model.GetDeclaredSymbol(method);
            
            if (methodSymbol == null)
                continue;

            var containingType = methodSymbol.ContainingType;
            if (containingType == null)
                continue;

            var namespaceName = containingType.ContainingNamespace?.ToDisplayString() ?? "";
            var fullName = $"{namespaceName}.{containingType.Name}.{methodSymbol.Name}";
            
            if (!ShouldTrace(namespaceName, fullName))
                continue;

            var methodName = methodSymbol.Name;
            var returnType = methodSymbol.ReturnType.ToDisplayString();
            
            var source = GenerateWrapper(method, containingType.Name, methodName, namespaceName, returnType);
            
            var hintName = $"{containingType.Name}_{methodName}_Traced.g.cs";
            context.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
        }
    }

    private bool ShouldTrace(string namespaceName, string fullName)
    {
        if (string.IsNullOrEmpty(_includes) && string.IsNullOrEmpty(_excludes))
            return false;

        if (!string.IsNullOrEmpty(_includes))
        {
            var patterns = _includes.Split(',');
            foreach (var pattern in patterns)
            {
                var p = pattern.Trim().Replace(".", "\\.").Replace("*", ".*");
                if (Regex.IsMatch(namespaceName, p, RegexOptions.IgnoreCase) || 
                    Regex.IsMatch(fullName, p, RegexOptions.IgnoreCase))
                    return true;
            }
            return false;
        }

        if (!string.IsNullOrEmpty(_excludes))
        {
            var patterns = _excludes.Split(',');
            foreach (var pattern in patterns)
            {
                var p = pattern.Trim().Replace(".", "\\.").Replace("*", ".*");
                if (Regex.IsMatch(namespaceName, p, RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(fullName, p, RegexOptions.IgnoreCase))
                    return false;
            }
        }

        return true;
    }

    private string GenerateWrapper(MethodDeclarationSyntax method, string className, string methodName, string namespaceName, string returnType)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"namespace {namespaceName};");
        sb.AppendLine();
        sb.AppendLine($"#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"partial class {className}");
        sb.AppendLine("{");
        
        var paramList = method.ParameterList.Parameters;
        
        var isAsync = method.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword));
        var isVoid = returnType == "void";
        
        if (isAsync && !isVoid)
        {
            sb.AppendLine($"    public async {returnType} {methodName}_Traced({GetParameterString(paramList)})");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        LangAngo.CSharp.Instrumentation.MethodTracer.MethodEnter(\"{methodName}\");");
            sb.AppendLine($"        try");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            return await {methodName}({GetArgumentString(paramList)});");
            sb.AppendLine($"        }}");
            sb.AppendLine($"        finally");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            LangAngo.CSharp.Instrumentation.MethodTracer.MethodLeave();");
            sb.AppendLine($"        }}");
            sb.AppendLine($"    }}");
        }
        else if (isAsync && isVoid)
        {
            sb.AppendLine($"    public async System.Threading.Tasks.Task {methodName}_Traced({GetParameterString(paramList)})");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        LangAngo.CSharp.Instrumentation.MethodTracer.MethodEnter(\"{methodName}\");");
            sb.AppendLine($"        try");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            await {methodName}({GetArgumentString(paramList)});");
            sb.AppendLine($"        }}");
            sb.AppendLine($"        finally");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            LangAngo.CSharp.Instrumentation.MethodTracer.MethodLeave();");
            sb.AppendLine($"        }}");
            sb.AppendLine($"    }}");
        }
        else if (!isAsync && isVoid)
        {
            sb.AppendLine($"    public void {methodName}_Traced({GetParameterString(paramList)})");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        LangAngo.CSharp.Instrumentation.MethodTracer.MethodEnter(\"{methodName}\");");
            sb.AppendLine($"        try");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            {methodName}({GetArgumentString(paramList)});");
            sb.AppendLine($"        }}");
            sb.AppendLine($"        finally");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            LangAngo.CSharp.Instrumentation.MethodTracer.MethodLeave();");
            sb.AppendLine($"        }}");
            sb.AppendLine($"    }}");
        }
        else
        {
            sb.AppendLine($"    public {returnType} {methodName}_Traced({GetParameterString(paramList)})");
            sb.AppendLine($"    {{");
            sb.AppendLine($"        LangAngo.CSharp.Instrumentation.MethodTracer.MethodEnter(\"{methodName}\");");
            sb.AppendLine($"        try");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            return {methodName}({GetArgumentString(paramList)});");
            sb.AppendLine($"        }}");
            sb.AppendLine($"        finally");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            LangAngo.CSharp.Instrumentation.MethodTracer.MethodLeave();");
            sb.AppendLine($"        }}");
            sb.AppendLine($"    }}");
        }
        
        sb.AppendLine("}");
        
        return sb.ToString();
    }

    private string GetParameterString(SeparatedSyntaxList<ParameterSyntax> parameters)
    {
        if (parameters.Count == 0)
            return "";
        
        var parts = new List<string>();
        foreach (var p in parameters)
        {
            parts.Add($"{p.Type} {p.Identifier}");
        }
        return string.Join(", ", parts);
    }

    private string GetArgumentString(SeparatedSyntaxList<ParameterSyntax> parameters)
    {
        if (parameters.Count == 0)
            return "";
        
        var parts = new List<string>();
        foreach (var p in parameters)
        {
            parts.Add(p.Identifier.ToString());
        }
        return string.Join(", ", parts);
    }
}

internal class AutoTraceSyntaxReceiver : ISyntaxContextReceiver
{
    public List<MethodDeclarationSyntax> CandidateMethods { get; } = new();

    public void OnVisitSyntaxNode(GeneratorSyntaxContext syntaxNode)
    {
        if (syntaxNode.Node is MethodDeclarationSyntax method)
        {
            var isPublic = method.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
            var isNotGenerated = !method.Identifier.ToString().EndsWith("_Traced");
            var isPartial = method.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
            
            if (isPublic && isNotGenerated && isPartial)
            {
                CandidateMethods.Add(method);
            }
        }
    }
}
