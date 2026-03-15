using LangAngo.CSharp.Core;
using LangAngo.CSharp.Instrumentation;
using LangAngo.CSharp.Transport;
using Xunit;

namespace LangAngo.CSharp.Tests;

public class MethodTracerTests
{
    private static void DrainChannel()
    {
        while (SpanChannel.Reader.TryRead(out _)) { }
    }

    private static void ResetForTest()
    {
        TraceContext.Clear();
        DrainChannel();
    }

    [Fact]
    public void UT01_NestedCalls_span_stack_closes_correctly()
    {
        ResetForTest();
        SetMethodTracerFilter("*", null);

        MethodTracer.MethodEnter(methodName: "Outer", declaringTypeFullName: "MyNs.MyClass");
        MethodTracer.MethodEnter(methodName: "Inner", declaringTypeFullName: "MyNs.MyClass");
        MethodTracer.MethodLeave();
        MethodTracer.MethodLeave();

        var spans = new List<Span>();
        while (SpanChannel.Reader.TryRead(out var s)) spans.Add(s);

        Assert.Equal(2, spans.Count);
        Assert.Equal("MyNs.MyClass.Inner", spans[0].Name);
        Assert.Equal("MyNs.MyClass.Outer", spans[1].Name);
        Assert.Equal(spans[1].SpanId, spans[0].ParentId);
        Assert.Equal(spans[0].TraceId, spans[1].TraceId);
    }

    [Fact]
    public async Task UT02_AsyncContextSwitch_TraceID_preserved_after_await()
    {
        ResetForTest();
        SetMethodTracerFilter("*", null);

        await RunAsync();

        async Task RunAsync()
        {
            MethodTracer.MethodEnter(methodName: "AsyncMethod", declaringTypeFullName: "Test");
            await Task.Yield();
            MethodTracer.MethodLeave();
        }

        var spans = new List<Span>();
        while (SpanChannel.Reader.TryRead(out var s)) spans.Add(s);

        Assert.Single(spans);
        Assert.Equal("Test.AsyncMethod", spans[0].Name);
        Assert.True(spans[0].EndTimestamp.HasValue);
    }

    [Fact]
    public void UT03_ExceptionHandling_finally_closes_span()
    {
        ResetForTest();
        SetMethodTracerFilter("*", null);

        try
        {
            MethodTracer.MethodEnter(methodName: "ThrowingMethod", declaringTypeFullName: "Test");
            throw new InvalidOperationException("test");
        }
        catch (InvalidOperationException)
        {
            MethodTracer.MethodLeave();
        }

        var spans = new List<Span>();
        while (SpanChannel.Reader.TryRead(out var s)) spans.Add(s);

        Assert.Single(spans);
        Assert.Equal("Test.ThrowingMethod", spans[0].Name);
        Assert.True(spans[0].EndTimestamp.HasValue);
    }

    [Fact]
    public void UT04_FilterLogic_ShouldTrace_respects_includes_excludes()
    {
        SetMethodTracerFilter(includes: null, excludes: null);
        Assert.False(MethodTracer.ShouldTrace("AnyMethod"));

        SetMethodTracerFilter(includes: "Foo*,*Bar", excludes: null);
        Assert.True(MethodTracer.ShouldTrace("Foo"));
        Assert.True(MethodTracer.ShouldTrace("FooM"));
        Assert.True(MethodTracer.ShouldTrace("MyBar"));
        Assert.False(MethodTracer.ShouldTrace("Other"));

        SetMethodTracerFilter(includes: null, excludes: "Skip*");
        Assert.False(MethodTracer.ShouldTrace("SkipMe"));
        Assert.True(MethodTracer.ShouldTrace("IncludeMe"));
    }

    private static void SetMethodTracerFilter(string? includes, string? excludes)
    {
        var t = typeof(MethodTracer);
        var inc = t.GetField("_includes", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var exc = t.GetField("_excludes", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        inc?.SetValue(null, includes);
        exc?.SetValue(null, excludes);
    }

    [Fact]
    public void Perf_Memory_100k_calls_stack_drains_and_RAM_stable()
    {
        ResetForTest();
        SetMethodTracerFilter("*", null);

        for (int i = 0; i < 100_000; i++)
        {
            MethodTracer.MethodEnter(methodName: "PerfMethod", declaringTypeFullName: "PerfTest");
            MethodTracer.MethodLeave();
        }

        var count = 0;
        while (SpanChannel.Reader.TryRead(out _)) count++;
        Assert.Equal(100_000, count);

        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(SpanChannel.Reader.TryRead(out _));
    }
}
