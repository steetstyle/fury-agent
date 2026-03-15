namespace LangAngo.Cecil.Weaver.Tests;

/// <summary>Target type for weaver verification: has a public instance method to instrument.</summary>
public class SampleTarget
{
    public void DoWork()
    {
        // Empty so weaver can wrap with try/finally
    }
}
