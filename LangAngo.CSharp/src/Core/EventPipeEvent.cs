namespace LangAngo.CSharp.Core;

public readonly struct EventPipeEvent
{
    public Protocol.EventType EventType { get; init; }
    public ulong Timestamp { get; init; }
    public Guid TraceId { get; init; }
    public ulong SpanId { get; init; }
    public ulong? ParentSpanId { get; init; }
    public string? Name { get; init; }
    public string? Value { get; init; }
    public Dictionary<string, string>? Tags { get; init; }

    private static ulong GetTimestamp()
    {
        return (ulong)(DateTimeOffset.UtcNow.UtcTicks);
    }

    public static EventPipeEvent CreateSpanStart(string name, Guid traceId, ulong spanId, ulong? parentSpanId)
    {
        return new EventPipeEvent
        {
            EventType = Protocol.EventType.SpanStartEnd,
            Timestamp = GetTimestamp(),
            TraceId = traceId,
            SpanId = spanId,
            ParentSpanId = parentSpanId,
            Name = name,
            Value = "start"
        };
    }

    public static EventPipeEvent CreateSpanEnd(string name, Guid traceId, ulong spanId)
    {
        return new EventPipeEvent
        {
            EventType = Protocol.EventType.SpanStartEnd,
            Timestamp = GetTimestamp(),
            TraceId = traceId,
            SpanId = spanId,
            Name = name,
            Value = "end"
        };
    }

    public static EventPipeEvent CreateRuntimeMetric(string metricName, double value, string? unit = null)
    {
        return new EventPipeEvent
        {
            EventType = Protocol.EventType.RuntimeMetric,
            Timestamp = GetTimestamp(),
            TraceId = Guid.Empty,
            Name = metricName,
            Value = value.ToString("F2"),
            Tags = unit != null ? new Dictionary<string, string> { { "unit", unit } } : null
        };
    }

    public static EventPipeEvent CreateException(string type, string message, string? stackTrace = null, Guid traceId = default)
    {
        return new EventPipeEvent
        {
            EventType = Protocol.EventType.Exception,
            Timestamp = GetTimestamp(),
            TraceId = traceId,
            Name = type,
            Value = message,
            Tags = stackTrace != null ? new Dictionary<string, string> { { "stacktrace", stackTrace } } : null
        };
    }

    public static EventPipeEvent CreateGCEvent(int generation, string reason, long bytesBefore, long bytesAfter, Duration duration, Guid traceId = default)
    {
        return new EventPipeEvent
        {
            EventType = Protocol.EventType.GCEvent,
            Timestamp = GetTimestamp(),
            TraceId = traceId,
            Name = $"GC.Gen{generation}",
            Value = reason,
            Tags = new Dictionary<string, string>
            {
                { "bytes_before", bytesBefore.ToString() },
                { "bytes_after", bytesAfter.ToString() },
                { "duration_us", duration.TotalMicroseconds.ToString("F0") }
            }
        };
    }

    public static EventPipeEvent CreateJITEvent(string methodName, Duration compilationTime, Guid traceId = default)
    {
        return new EventPipeEvent
        {
            EventType = Protocol.EventType.JITEvent,
            Timestamp = GetTimestamp(),
            TraceId = traceId,
            Name = "JITCompilation",
            Value = methodName,
            Tags = new Dictionary<string, string>
            {
                { "duration_us", compilationTime.TotalMicroseconds.ToString("F0") }
            }
        };
    }

    public static EventPipeEvent CreateThreadPoolEvent(int workerThreads, int ioThreads, Guid traceId = default)
    {
        return new EventPipeEvent
        {
            EventType = Protocol.EventType.ThreadPoolEvent,
            Timestamp = GetTimestamp(),
            TraceId = traceId,
            Name = "ThreadPool",
            Value = $"workers:{workerThreads},io:{ioThreads}",
            Tags = new Dictionary<string, string>
            {
                { "worker_threads", workerThreads.ToString() },
                { "io_threads", ioThreads.ToString() }
            }
        };
    }

    public static EventPipeEvent CreateContentionEvent(string lockName, Duration waitTime, Guid traceId = default)
    {
        return new EventPipeEvent
        {
            EventType = Protocol.EventType.ContentionEvent,
            Timestamp = GetTimestamp(),
            TraceId = traceId,
            Name = "Contention",
            Value = lockName,
            Tags = new Dictionary<string, string>
            {
                { "wait_time_us", waitTime.TotalMicroseconds.ToString("F0") }
            }
        };
    }

    public static EventPipeEvent CreateSamplingEvent(Guid traceId, ulong spanId, ulong? parentSpanId, string stackTrace, Duration duration)
    {
        return new EventPipeEvent
        {
            EventType = Protocol.EventType.Sampling,
            Timestamp = GetTimestamp(),
            TraceId = traceId,
            SpanId = spanId,
            ParentSpanId = parentSpanId,
            Name = "StackSample",
            Value = stackTrace,
            Tags = new Dictionary<string, string>
            {
                { "duration_us", duration.TotalMicroseconds.ToString("F0") },
                { "sample_type", "stack_walk" }
            }
        };
    }
}

public readonly struct Duration
{
    public double TotalMicroseconds { get; }
    public double TotalMilliseconds => TotalMicroseconds / 1000;
    public double TotalSeconds => TotalMilliseconds / 1000;

    public Duration(double microseconds) => TotalMicroseconds = microseconds;

    public static Duration FromMicroseconds(double us) => new(us);
    public static Duration FromMilliseconds(double ms) => new(ms * 1000);
    public static Duration FromStopwatchTicks(long ticks)
    {
        var us = (ticks * 1_000_000.0) / System.Diagnostics.Stopwatch.Frequency;
        return new Duration(us);
    }
}
