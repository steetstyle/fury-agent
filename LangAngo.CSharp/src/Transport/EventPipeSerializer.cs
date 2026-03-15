using System.Buffers.Binary;
using System.Text;
using LangAngo.CSharp.Core;

namespace LangAngo.CSharp.Transport;

public static class EventPipeSerializer
{
    public static byte[] Serialize(in EventPipeEvent evt)
    {
        var payloadSize = CalculatePayloadSize(evt);
        var totalSize = Protocol.HeaderSize + payloadSize;
        
        var buffer = new byte[totalSize];
        var writer = new SpanWriter(buffer);
        
        WriteHeader(writer, payloadSize, Protocol.PayloadType.EventPipeEvent, evt.TraceId, evt.SpanId, evt.ParentSpanId);
        
        writer.WriteByte((byte)evt.EventType);
        writer.WriteUInt64(evt.Timestamp);
        WriteString(writer, evt.Name);
        WriteString(writer, evt.Value);
        WriteTags(writer, evt.Tags);
        
        return buffer;
    }

    private static int CalculatePayloadSize(in EventPipeEvent evt)
    {
        var nameLen = evt.Name?.Length ?? 0;
        var valueLen = evt.Value?.Length ?? 0;
        var tagsLen = evt.Tags?.Sum(k => k.Key.Length + k.Value.Length + 8) ?? 0;
        
        return 1 + 8 + 4 + nameLen + 4 + valueLen + 2 + tagsLen;
    }

    private static void WriteHeader(SpanWriter writer, int payloadSize, Protocol.PayloadType payloadType, Guid traceId, ulong spanId, ulong? parentSpanId)
    {
        writer.WriteByte(Protocol.Magic1);
        writer.WriteByte(Protocol.Magic2);
        writer.WriteByte(Protocol.Version);
        writer.WriteByte((byte)payloadType);
        
        var payloadLenBytes = BitConverter.GetBytes((uint)payloadSize);
        if (BitConverter.IsLittleEndian) Array.Reverse(payloadLenBytes);
        writer.WriteBytes(payloadLenBytes);
        
        var traceIdBytes = traceId.ToByteArray();
        writer.WriteBytes(traceIdBytes);
        
        var spanIdBytes = BitConverter.GetBytes(spanId);
        if (BitConverter.IsLittleEndian) Array.Reverse(spanIdBytes);
        writer.WriteBytes(spanIdBytes);
        
        var parentId = parentSpanId ?? 0;
        var parentIdBytes = BitConverter.GetBytes(parentId);
        if (BitConverter.IsLittleEndian) Array.Reverse(parentIdBytes);
        writer.WriteBytes(parentIdBytes);
    }

    private static void WriteString(SpanWriter writer, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            writer.WriteInt32(0);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        writer.WriteInt32(bytes.Length);
        writer.WriteBytes(bytes);
    }

    private static void WriteTags(SpanWriter writer, Dictionary<string, string>? tags)
    {
        if (tags == null || tags.Count == 0)
        {
            writer.WriteInt16(0);
            return;
        }

        writer.WriteInt16((short)tags.Count);
        foreach (var kvp in tags)
        {
            WriteString(writer, kvp.Key);
            WriteString(writer, kvp.Value);
        }
    }
}

public ref struct SpanWriter
{
    private readonly Span<byte> _buffer;
    private int _position;

    public SpanWriter(Span<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    public int WrittenLength => _position;

    public void WriteByte(byte value)
    {
        _buffer[_position++] = value;
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        value.CopyTo(_buffer.Slice(_position));
        _position += value.Length;
    }

    public void WriteInt16(short value)
    {
        BinaryPrimitives.WriteInt16LittleEndian(_buffer.Slice(_position), value);
        _position += 2;
    }

    public void WriteInt32(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_buffer.Slice(_position), value);
        _position += 4;
    }

    public void WriteUInt64(ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(_buffer.Slice(_position), value);
        _position += 8;
    }

    public void WriteBoolean(bool value)
    {
        _buffer[_position++] = value ? (byte)1 : (byte)0;
    }
}
