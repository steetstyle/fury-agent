using System.Diagnostics;
using LangAngo.CSharp.Core;
using LangAngo.CSharp.Transport;

namespace LangAngo.CSharp.Instrumentation;

public sealed class DbHandler : BaseInstrumentationHandler
{
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
        var commandText = PropertyFetcher.FetchProperty(payload, "CommandText")?.ToString() ?? "";
        var parameters = PropertyFetcher.FetchProperty(payload, "Parameters");
        
        var currentCtx = TraceContext.TryGetCurrent() ?? TraceContext.CreateChild(Protocol.SpanKind.Client);
        
        var shortCommand = commandText.Length > 50 ? commandText.Substring(0, 50) + "..." : commandText;
        var span = new Span
        {
            Type = Protocol.PayloadType.Span,
            TraceId = currentCtx.TraceId,
            SpanId = currentCtx.SpanId,
            ParentId = currentCtx.ParentId,
            Kind = Protocol.SpanKind.Client,
            Name = $"DB: {shortCommand}",
            ServiceName = currentCtx.ServiceName,
            StartTimestamp = Stopwatch.GetTimestamp()
        };

        span.Metadata["db.system"] = "postgresql";
        span.Metadata["db.statement"] = commandText;
        
        SpanChannel.Writer.TryWrite(span);
    }

    private void OnCommandExecuted(object payload)
    {
        var duration = PropertyFetcher.FetchProperty(payload, "Duration");
        if (duration != null)
        {
            var span = new Span
            {
                Type = Protocol.PayloadType.Span,
                Name = "DB Executed",
                EndTimestamp = Stopwatch.GetTimestamp()
            };
            SpanChannel.Writer.TryWrite(span);
        }
    }
}
