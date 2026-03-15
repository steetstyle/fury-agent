using System.Diagnostics;
using System.Runtime.CompilerServices;
using LangAngo.CSharp.Core;
using LangAngo.CSharp.Transport;

namespace LangAngo.CSharp.Instrumentation;

/// <summary>Instruments outbound HttpClient calls as child spans and injects W3C traceparent.</summary>
public sealed class HttpOutboundHandler : BaseInstrumentationHandler
{
    private sealed class RequestState
    {
        internal TraceContext? Previous;
        internal Span Span;
        internal RequestState(TraceContext? previous, Span span) { Previous = previous; Span = span; }
    }

    private static readonly ConditionalWeakTable<object, RequestState> _activeByRequest = new();

    public override string SourceName => "System.Net.Http";

    public override bool CanHandle(string eventName) =>
        eventName is "System.Net.Http.HttpRequestOut.Start"
            or "System.Net.Http.HttpRequestOut.Stop"
            or "System.Net.Http.HttpRequestOut.Exception";

    public override void OnEvent(string eventName, object payload)
    {
        if (eventName.EndsWith(".Start"))
            OnRequestStart(payload);
        else if (eventName.EndsWith(".Stop"))
            OnRequestStop(payload);
        else if (eventName.EndsWith(".Exception"))
            OnRequestException(payload);
    }

    private void OnRequestStart(object payload)
    {
        var parent = TraceContext.TryGetCurrent();
        var child = TraceContext.CreateChild(Protocol.SpanKind.Client);
        child.SetAsCurrent();

        var request = PropertyFetcher.FetchProperty(payload, "Request");
        if (request == null) return;

        var method = PropertyFetcher.FetchProperty(request, "Method")?.ToString() ?? "GET";
        var uri = PropertyFetcher.FetchProperty(request, "RequestUri")?.ToString() ?? "";
        var host = PropertyFetcher.FetchProperty(request, "RequestUri") is Uri u ? u.Host : "";

        InjectTraceparent(request, child.ToTraceparent());

        var span = new Span
        {
            Type = Protocol.PayloadType.Span,
            TraceId = child.TraceId,
            SpanId = child.SpanId,
            ParentId = child.ParentId,
            Kind = Protocol.SpanKind.Client,
            Name = $"{method} {uri}",
            ServiceName = child.ServiceName,
            StartTimestamp = Stopwatch.GetTimestamp()
        };
        span.Metadata["http.method"] = method;
        span.Metadata["http.url"] = uri;
        if (!string.IsNullOrEmpty(host))
            span.Metadata["http.host"] = host;

        try { _activeByRequest.Add(request, new RequestState(parent, span)); } catch { /* already added */ }
    }

    private void OnRequestStop(object payload)
    {
        var request = PropertyFetcher.FetchProperty(payload, "Request");
        if (request == null) return;
        if (!_activeByRequest.TryGetValue(request, out var state))
            return;

        state.Span.EndTimestamp = Stopwatch.GetTimestamp();
        var response = PropertyFetcher.FetchProperty(payload, "Response");
        if (response != null)
        {
            var statusCode = PropertyFetcher.FetchProperty(response, "StatusCode");
            if (statusCode != null)
                state.Span.Metadata["http.status_code"] = statusCode.ToString() ?? "";
        }

        if (state.Previous != null)
            state.Previous.SetAsCurrent();
        else
            TraceContext.Clear();

        SpanChannel.Writer.TryWrite(state.Span);
    }

    private void OnRequestException(object payload)
    {
        var request = PropertyFetcher.FetchProperty(payload, "Request");
        if (request == null) return;
        if (!_activeByRequest.TryGetValue(request, out var state))
            return;

        state.Span.EndTimestamp = Stopwatch.GetTimestamp();
        state.Span.Status = Protocol.SpanStatus.Error;

        if (state.Previous != null)
            state.Previous.SetAsCurrent();
        else
            TraceContext.Clear();

        SpanChannel.Writer.TryWrite(state.Span);
    }

    private static void InjectTraceparent(object request, string traceparent)
    {
        try
        {
            var headers = PropertyFetcher.FetchProperty(request, "Headers");
            if (headers == null) return;
            var method = headers.GetType().GetMethod("TryAddWithoutValidation", new[] { typeof(string), typeof(string) })
                ?? headers.GetType().GetMethod("Add", new[] { typeof(string), typeof(string) });
            method?.Invoke(headers, new object[] { "traceparent", traceparent });
        }
        catch { }
    }
}
