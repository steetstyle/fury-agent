namespace LangAngo.CSharp.Core;

public static class Protocol
{
    public const byte Magic1 = 0x4C;
    public const byte Magic2 = 0x41;
    public const byte Version = 0x01;
    public const int HeaderSize = 40;

    public enum PayloadType : byte
    {
        Span = 1,
        Symbol = 2,
        Stack = 3,
        Exception = 4,
        EventPipeEvent = 5,
        Metric = 6,
        SpanStart = 7,
        SpanEnd = 8,
    }

    public enum EventType : byte
    {
        SpanStartEnd = 0x01,
        RuntimeMetric = 0x02,
        Exception = 0x03,
        Metadata = 0x04,
        GCEvent = 0x10,
        JITEvent = 0x11,
        ThreadPoolEvent = 0x12,
        ContentionEvent = 0x13,
        Sampling = 0x20,
    }

    public enum SpanKind : byte
    {
        Internal = 0,
        Server = 1,
        Client = 2,
        Producer = 3,
        Consumer = 4
    }

    public enum SpanStatus : byte
    {
        Unset = 0,
        Ok = 1,
        Error = 2
    }
}
