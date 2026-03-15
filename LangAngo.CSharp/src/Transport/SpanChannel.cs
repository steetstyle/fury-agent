using System.Threading.Channels;
using LangAngo.CSharp.Core;

namespace LangAngo.CSharp.Transport;

public static class SpanChannel
{
    private static readonly Channel<Span> _channel = Channel.CreateUnbounded<Span>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        }
    );

    public static ChannelWriter<Span> Writer => _channel.Writer;
    public static ChannelReader<Span> Reader => _channel.Reader;

    public static bool TryWrite(Span span) => Writer.TryWrite(span);

    public static ValueTask WriteAsync(Span span) => Writer.WriteAsync(span);

    public static void Complete() => Writer.Complete();

    public static int ApproximateCount => Reader.Count;
}
