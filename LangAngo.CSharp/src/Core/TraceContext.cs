using System.Collections.Concurrent;

namespace LangAngo.CSharp.Core;

public sealed class TraceContext
{
    private static readonly AsyncLocal<TraceContext?> _current = new();
    private static readonly ConcurrentDictionary<string, Guid> _activeTraces = new();

    public Guid TraceId { get; }
    public ulong SpanId { get; }
    public ulong? ParentId { get; }
    public Protocol.SpanKind Kind { get; }
    public string ServiceName { get; }

    public static TraceContext? Current => _current.Value;

    private TraceContext(Guid traceId, ulong spanId, ulong? parentId, Protocol.SpanKind kind, string serviceName)
    {
        TraceId = traceId;
        SpanId = spanId;
        ParentId = parentId;
        Kind = kind;
        ServiceName = serviceName;
    }

    public static TraceContext CreateRoot(Protocol.SpanKind kind = Protocol.SpanKind.Internal)
    {
        var traceId = Guid.NewGuid();
        var spanId = GenerateSpanId();
        var serviceName = GetServiceName();

        return new TraceContext(traceId, spanId, null, kind, serviceName);
    }

    /// <summary>Create context from W3C traceparent (incoming request). Format: 00-{traceId}-{parentSpanId}-01</summary>
    public static TraceContext? CreateFromW3C(string? traceparent, Protocol.SpanKind kind = Protocol.SpanKind.Server)
    {
        if (string.IsNullOrWhiteSpace(traceparent)) return null;
        var parts = traceparent.Trim().Split('-');
        if (parts.Length != 4 || parts[0] != "00") return null;
        if (parts[1].Length != 32 || parts[2].Length != 16) return null;
        try
        {
            var traceId = Guid.ParseExact(parts[1], "N");
            var parentSpanId = Convert.ToUInt64(parts[2], 16);
            var spanId = GenerateSpanId();
            var serviceName = GetServiceName();
            return new TraceContext(traceId, spanId, parentSpanId, kind, serviceName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>W3C traceparent value for propagation. 00-{traceId}-{spanId}-01</summary>
    public string ToTraceparent()
    {
        return $"00-{TraceId:N}-{SpanId:X16}-01";
    }

    public static TraceContext CreateChild(Protocol.SpanKind kind = Protocol.SpanKind.Internal)
    {
        var parent = Current ?? CreateRoot(kind);
        var spanId = GenerateSpanId();

        return new TraceContext(parent.TraceId, spanId, parent.SpanId, kind, parent.ServiceName);
    }

    public void SetAsCurrent()
    {
        _current.Value = this;
    }

    public static void Clear()
    {
        _current.Value = null;
    }

    public static TraceContext? TryGetCurrent()
    {
        return _current.Value;
    }

    private static ulong GenerateSpanId()
    {
        var bytes = new byte[8];
        Random.Shared.NextBytes(bytes);
        return BitConverter.ToUInt64(bytes, 0);
    }

    private static string GetServiceName()
    {
        return Environment.GetEnvironmentVariable("LANGANGO_SERVICE_NAME") 
               ?? "unknown-service";
    }

    public static void RegisterActiveTrace(Guid traceId)
    {
        var key = $"trace_{traceId}";
        _activeTraces[key] = traceId;
    }

    public static void UnregisterActiveTrace(Guid traceId)
    {
        var key = $"trace_{traceId}";
        _activeTraces.TryRemove(key, out _);
    }

    public static Guid? TryGetActiveTraceId()
    {
        var ctx = _current.Value;
        if (ctx != null) return ctx.TraceId;

        foreach (var kvp in _activeTraces)
        {
            return kvp.Value;
        }
        return null;
    }
}
