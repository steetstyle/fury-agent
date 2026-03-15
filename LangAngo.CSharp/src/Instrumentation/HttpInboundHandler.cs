using System.Diagnostics;
using LangAngo.CSharp.Core;
using LangAngo.CSharp.Transport;

namespace LangAngo.CSharp.Instrumentation;

public sealed class HttpInboundHandler : BaseInstrumentationHandler
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

        var traceparent = GetTraceparentFromRequest(requestObj);
        var ctx = TraceContext.CreateFromW3C(traceparent, Protocol.SpanKind.Server) ?? TraceContext.CreateRoot(Protocol.SpanKind.Server);
        ctx.SetAsCurrent();
        TraceContext.RegisterActiveTrace(ctx.TraceId);

        InjectTraceparentIntoResponse(context, ctx.ToTraceparent());

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

    private static string? GetTraceparentFromRequest(object? requestObj)
    {
        if (requestObj == null) return null;
        var headers = PropertyFetcher.FetchProperty(requestObj, "Headers");
        if (headers == null) return null;
        var traceparent = PropertyFetcher.FetchProperty(headers, "Traceparent") ?? PropertyFetcher.FetchProperty(headers, "traceparent");
        return traceparent?.ToString();
    }

    /// <summary>Inject W3C traceparent into response headers so clients and the agent can correlate the request.</summary>
    private static void InjectTraceparentIntoResponse(object? httpContext, string traceparent)
    {
        if (string.IsNullOrEmpty(traceparent) || httpContext == null) return;
        try
        {
            var response = PropertyFetcher.FetchProperty(httpContext, "Response");
            if (response == null) return;
            var headers = PropertyFetcher.FetchProperty(response, "Headers");
            if (headers == null) return;
            var t = headers.GetType();
            foreach (var m in t.GetMethods())
            {
                if (m.Name != "Append" || m.GetParameters().Length != 2) continue;
                var p1 = m.GetParameters()[0];
                var p2 = m.GetParameters()[1];
                if (p1.ParameterType != typeof(string)) continue;
                var arg2 = p2.ParameterType == typeof(string)
                    ? (object)traceparent
                    : ConvertTo(p2.ParameterType, traceparent);
                if (arg2 != null)
                {
                    m.Invoke(headers, new[] { "traceparent", arg2 });
                    return;
                }
            }
            var item = t.GetProperty("Item", new[] { typeof(string) });
            if (item?.CanWrite == true)
                item.SetValue(headers, ConvertTo(item.PropertyType, traceparent), new object[] { "traceparent" });
        }
        catch { }
    }

    private static object? ConvertTo(Type target, string value)
    {
        if (target == typeof(string)) return value;
        var sv = Type.GetType("Microsoft.Extensions.Primitives.StringValues, Microsoft.Extensions.Primitives");
        if (sv != null && target == sv)
        {
            var ctor = sv.GetConstructor(new[] { typeof(string) });
            return ctor?.Invoke(new object[] { value });
        }
        return null;
    }

    private void EnrichWithHeaders(object requestObj, Span span)
    {
        var headers = PropertyFetcher.FetchProperty(requestObj, "Headers");
        if (headers == null) return;

        var headerNames = new[] { "User-Agent", "X-Request-ID", "X-Trace-ID", "Content-Type", "traceparent", "Traceparent" };
        
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
