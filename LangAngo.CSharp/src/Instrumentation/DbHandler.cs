using System.Diagnostics;
using LangAngo.CSharp.Core;
using LangAngo.CSharp.Transport;

namespace LangAngo.CSharp.Instrumentation;

public sealed class DbHandler : BaseInstrumentationHandler
{
    private static readonly AsyncLocal<Stack<(TraceContext? Prev, Span Span)>> _stack = new();

    public override string SourceName => "Microsoft.EntityFrameworkCore";

    public override bool CanHandle(string eventName) =>
        eventName is "Microsoft.EntityFrameworkCore.Database.Command.CommandStarting"
            or "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted";

    public override void OnEvent(string eventName, object payload)
    {
        if (eventName.Contains("CommandStarting"))
        {
            OnCommandStart(payload);
        }
        else if (eventName.Contains("CommandExecuted"))
        {
            OnCommandExecuted(payload);
        }
    }

    private void OnCommandStart(object payload)
    {
        var parent = TraceContext.TryGetCurrent();
        if (parent == null) return;

        var commandText = PropertyFetcher.FetchProperty(payload, "CommandText")?.ToString() ?? "";
        var child = TraceContext.CreateChild(Protocol.SpanKind.Client);
        child.SetAsCurrent();

        var shortCommand = commandText.Length > 50 ? commandText[..50] + "..." : commandText;
        var span = new Span
        {
            Type = Protocol.PayloadType.Span,
            TraceId = child.TraceId,
            SpanId = child.SpanId,
            ParentId = child.ParentId,
            Kind = Protocol.SpanKind.Client,
            Name = $"DB: {shortCommand}",
            ServiceName = child.ServiceName,
            StartTimestamp = Stopwatch.GetTimestamp()
        };
        span.Metadata["db.system"] = "postgresql";
        span.Metadata["db.statement"] = commandText;

        var stack = _stack.Value ?? new Stack<(TraceContext? Prev, Span Span)>();
        stack.Push((Prev: parent, Span: span));
        _stack.Value = stack;
    }

    private void OnCommandExecuted(object payload)
    {
        var stack = _stack.Value;
        if (stack == null || stack.Count == 0) return;

        var (prev, dbSpan) = stack.Pop();
        if (stack.Count == 0) _stack.Value = null;

        dbSpan.EndTimestamp = Stopwatch.GetTimestamp();
        var duration = PropertyFetcher.FetchProperty(payload, "Duration");
        if (duration != null)
            dbSpan.Metadata["db.duration_ms"] = duration.ToString() ?? "";

        if (prev != null)
            prev.SetAsCurrent();
        else
            TraceContext.Clear();

        SpanChannel.Writer.TryWrite(dbSpan);
    }
}
