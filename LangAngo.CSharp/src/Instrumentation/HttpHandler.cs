using System.Diagnostics;
using LangAngo.CSharp.Core;
using LangAngo.CSharp.Transport;

namespace LangAngo.CSharp.Instrumentation;

public sealed class HttpHandler : BaseInstrumentationHandler
{
    private readonly Dictionary<string, Span> _activeSpans = new();
    private readonly object _lock = new();

    public override string SourceName => "Microsoft.AspNetCore";

    public override bool CanHandle(string eventName) =>
        eventName is "Microsoft.AspNetCore.Hosting.HttpRequestIn.Start"
            or "Microsoft.AspNetCore.Hosting.HttpRequestIn.Stop";

    public override void OnEvent(string eventName, object payload)
    {
        if (eventName.EndsWith(".Start"))
        {
            OnRequestStart(payload);
        }
        else if (eventName.EndsWith(".Stop"))
        {
            OnRequestStop(payload);
        }
    }

    private void OnRequestStart(object payload)
    {
        var context = PropertyFetcher.FetchProperty(payload, "HttpContext");
        if (context == null) return;

        var path = "/";
        var method = "GET";
        var queryString = "";

        var requestObj = PropertyFetcher.FetchProperty(context, "Request");
        if (requestObj != null)
        {
            var reqPath = PropertyFetcher.FetchProperty(requestObj, "Path");
            if (reqPath != null)
            {
                var pathObj = PropertyFetcher.FetchProperty(reqPath, "Value");
                path = pathObj?.ToString() ?? "/";
            }
            
            var reqMethod = PropertyFetcher.FetchProperty(requestObj, "Method");
            method = reqMethod?.ToString() ?? "GET";

            var qs = PropertyFetcher.FetchProperty(requestObj, "QueryString");
            if (qs != null)
            {
                var qsValue = PropertyFetcher.FetchProperty(qs, "Value");
                queryString = qsValue?.ToString() ?? "";
                if (queryString == "?") queryString = "";
            }
        }

        var fullUrl = string.IsNullOrEmpty(queryString) ? path : $"{path}{queryString}";

        var ctx = TraceContext.CreateRoot(Protocol.SpanKind.Server);
        ctx.SetAsCurrent();
        TraceContext.RegisterActiveTrace(ctx.TraceId);

        var span = new Span
        {
            Type = Protocol.PayloadType.Span,
            TraceId = ctx.TraceId,
            SpanId = ctx.SpanId,
            ParentId = ctx.ParentId,
            Kind = ctx.Kind,
            Name = $"{method} {path}",
            ServiceName = ctx.ServiceName,
            StartTimestamp = Stopwatch.GetTimestamp()
        };

        span.Metadata.TryAdd("http.method", method);
        span.Metadata.TryAdd("http.url", fullUrl);
        span.Metadata.TryAdd("http.target", path);
        if (!string.IsNullOrEmpty(queryString))
        {
            span.Metadata.TryAdd("http.query_string", queryString.TrimStart('?'));
        }

        if (requestObj != null)
        {
            EnrichWithHeaders(requestObj, span);
        }

        lock (_lock)
        {
            _activeSpans[ctx.SpanId.ToString()] = span;
        }

        SpanChannel.Writer.TryWrite(span);
    }

    private void EnrichWithHeaders(object requestObj, Span span)
    {
        var headers = PropertyFetcher.FetchProperty(requestObj, "Headers");
        if (headers == null) return;

        var headerNames = new[] { "User-Agent", "X-Request-ID", "X-Trace-ID", "Content-Type" };
        
        foreach (var headerName in headerNames)
        {
            var headerValue = PropertyFetcher.FetchProperty(headers, headerName);
            if (headerValue != null)
            {
                var key = headerName.ToLowerInvariant().Replace("-", "_");
                span.Metadata[$"http.request.header.{key}"] = headerValue.ToString() ?? "";
            }
        }

        var scheme = PropertyFetcher.FetchProperty(requestObj, "Scheme");
        if (scheme != null)
        {
            span.Metadata["http.scheme"] = scheme.ToString() ?? "http";
        }

        var host = PropertyFetcher.FetchProperty(requestObj, "Host");
        if (host != null)
        {
            span.Metadata["http.host"] = host.ToString() ?? "";
        }

        var queryString = PropertyFetcher.FetchProperty(requestObj, "QueryString");
        if (queryString != null)
        {
            var qsValue = PropertyFetcher.FetchProperty(queryString, "ToString");
            var qs = qsValue?.ToString();
            if (!string.IsNullOrEmpty(qs) && qs != "?")
            {
                span.Metadata["http.query_string"] = qs.TrimStart('?');
            }
        }
    }

    private void OnRequestStop(object payload)
    {
        var context = PropertyFetcher.FetchProperty(payload, "HttpContext");
        if (context == null) return;

        var currentCtx = TraceContext.TryGetCurrent();
        if (currentCtx == null) return;

        Span? span;
        lock (_lock)
        {
            if (!_activeSpans.TryGetValue(currentCtx.SpanId.ToString(), out span))
                return;
            _activeSpans.Remove(currentCtx.SpanId.ToString());
        }

        if (span == null) return;

        TraceContext.UnregisterActiveTrace(span.TraceId);

        span.EndTimestamp = Stopwatch.GetTimestamp();

        if (Activity.Current?.Status == ActivityStatusCode.Error)
        {
            span.Status = Protocol.SpanStatus.Error;
            if (!string.IsNullOrEmpty(Activity.Current.StatusDescription))
            {
                span.Metadata.TryAdd("error.message", Activity.Current.StatusDescription);
            }
        }

        SpanChannel.Writer.TryWrite(span);
    }
}
