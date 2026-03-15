using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace LangAngo.SourceGenerator;

[Generator]
public class MethodTracerGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
    }

    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxContextReceiver is not SyntaxReceiver receiver)
            return;

        var compilation = context.Compilation;
        var methodTracerType = compilation.GetTypeByMetadataName("LangAngo.CSharp.Instrumentation.MethodTracer");
        
        if (methodTracerType == null)
            return;

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
            var methodName = methodSymbol.Name;
            var returnType = methodSymbol.ReturnType.ToDisplayString();
            
            var source = GenerateTracingCode(method, containingType.Name, methodName, namespaceName, returnType);
            
            var hintName = $"{containingType.Name}_{methodName}.g.cs";
            context.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
        }
    }

    private string GenerateTracingCode(MethodDeclarationSyntax method, string className, string methodName, string namespaceName, string returnType)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"namespace {namespaceName};");
        sb.AppendLine();
        sb.AppendLine($"#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"partial class {className}");
        sb.AppendLine("{");
        
        var paramList = method.ParameterList.Parameters;
        
        sb.AppendLine($"    public {returnType} {methodName}_Traced({GetParameterString(paramList)})");
        sb.AppendLine($"    {{");
        sb.AppendLine($"        LangAngo.CSharp.Instrumentation.MethodTracer.MethodEnter(\"{methodName}\");");
        sb.AppendLine($"        try");
        sb.AppendLine($"        {{");
        
        if (returnType == "void")
        {
            sb.AppendLine($"            {methodName}({GetArgumentString(paramList)});");
            sb.AppendLine($"            return;");
        }
        else if (returnType.StartsWith("async"))
        {
            sb.AppendLine($"            return await {methodName}({GetArgumentString(paramList)});");
        }
        else
        {
            sb.AppendLine($"            return {methodName}({GetArgumentString(paramList)});");
        }
        
        sb.AppendLine($"        }}");
        sb.AppendLine($"        finally");
        sb.AppendLine($"        {{");
        sb.AppendLine($"            LangAngo.CSharp.Instrumentation.MethodTracer.MethodLeave();");
        sb.AppendLine($"        }}");
        sb.AppendLine($"    }}");
        
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

internal class SyntaxReceiver : ISyntaxContextReceiver
{
    public List<MethodDeclarationSyntax> CandidateMethods { get; } = new();

    public void OnVisitSyntaxNode(GeneratorSyntaxContext syntaxNode)
    {
        if (syntaxNode.Node is MethodDeclarationSyntax method)
        {
            var hasAttribute = method.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(a => 
                    a.Name.ToString().Contains("Trace") ||
                    a.Name.ToString().Contains("Traced"));
            
            if (hasAttribute)
            {
                CandidateMethods.Add(method);
            }
        }
    }
}
