namespace LangAngo.CSharp.Instrumentation;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class TraceAttribute : Attribute
{
    public string? Name { get; }
    
    public TraceAttribute() { }
    
    public TraceAttribute(string name)
    {
        Name = name;
    }
}
