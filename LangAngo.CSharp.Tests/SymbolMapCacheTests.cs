using LangAngo.CSharp.Core;
using Xunit;

namespace LangAngo.CSharp.Tests;

public class SymbolMapCacheTests
{
    [Fact]
    public void Resolve_returns_method_name_when_ip_in_range()
    {
        var cache = new SymbolMapCache();
        cache.Add(0x1000, 100, "MyNamespace.MyClass.MyMethod");
        Assert.Equal("MyNamespace.MyClass.MyMethod", cache.Resolve(0x1050));
        Assert.Null(cache.Resolve(0x0FFF));
        Assert.Null(cache.Resolve(0x1064));
    }

    [Fact]
    public void BuildCallStack_produces_readable_stack()
    {
        var cache = new SymbolMapCache();
        cache.Add(0x1000, 200, "A.B.Foo");
        cache.Add(0x2000, 200, "C.D.Bar");
        var ips = new[] { 0x2050UL, 0x1050UL };
        var stack = cache.BuildCallStack(ips);
        Assert.Contains("at C.D.Bar", stack);
        Assert.Contains("at A.B.Foo", stack);
    }

    [Fact]
    public void BuildCallStack_shows_hex_for_unresolved_ip()
    {
        var cache = new SymbolMapCache();
        cache.Add(0x1000, 100, "Known.Method");
        var stack = cache.BuildCallStack(new[] { 0x1050UL, 0x9999UL });
        Assert.Contains("at Known.Method", stack);
        Assert.Contains("0x9999", stack);
    }
}
