using System.Collections.Concurrent;

namespace LangAngo.CSharp.Core;

public sealed class Span
{
    public Protocol.PayloadType Type { get; init; }
    public Guid TraceId { get; init; }
    public ulong SpanId { get; init; }
    public ulong? ParentId { get; init; }
    public Protocol.SpanKind Kind { get; init; }
    public string Name { get; set; } = string.Empty;
    public string? ServiceName { get; init; }
    public long StartTimestamp { get; set; }
    public long? EndTimestamp { get; set; }
    public Protocol.SpanStatus Status { get; set; }
    public string? StatusMessage { get; set; }
    
    public ConcurrentDictionary<string, string> Metadata { get; } = new();
    public ConcurrentDictionary<string, string> Attributes { get; } = new();

    public long DurationTicks => EndTimestamp.HasValue 
        ? EndTimestamp.Value - StartTimestamp 
        : 0;

    public double DurationNanoseconds
    {
        get
        {
            var ticks = DurationTicks;
            return (ticks * 1_000_000_000.0) / System.Diagnostics.Stopwatch.Frequency;
        }
    }
}
