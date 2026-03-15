using System.Diagnostics;
using System.Runtime.CompilerServices;
using LangAngo.CSharp.Core;
using LangAngo.CSharp.Transport;

namespace LangAngo.CSharp.Instrumentation;

public static class OpenTelemetryBridge
{
    private static DiagnosticListener? _httpListener;
    private static DiagnosticListener? _dbListener;
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        Logger.Info("Initializing OpenTelemetry Bridge...");

        _httpListener = new DiagnosticListener("Microsoft.AspNetCore");
        _httpListener.Subscribe(new AspNetCoreObserver());

        Logger.Info("OpenTelemetry Bridge initialized");
    }
}

public class AspNetCoreObserver : IObserver<DiagnosticListener>
{
    public void OnCompleted() { }
    public void OnError(Exception error) { }
    public void OnNext(DiagnosticListener listener)
    {
        if (listener.Name == "Microsoft.AspNetCore")
        {
            listener.Subscribe(new AspNetCoreSpanObserver());
        }
    }
}

public class AspNetCoreSpanObserver : IObserver<KeyValuePair<string, object?>>
{
    public void OnCompleted() { }
    public void OnError(Exception error) { }

    public void OnNext(KeyValuePair<string, object?> value)
    {
        try
        {
            var name = value.Key;
            var payload = value.Value;

            if (name == "Microsoft.AspNetCore.Hosting.Begin" ||
                name == "Microsoft.AspNetCore.Hosting.End")
            {
                HandleHttpSpan(name, payload);
            }
            else if (name.StartsWith("Microsoft.AspNetCore.Routing"))
            {
                HandleRoutingSpan(name, payload);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("[OtelBridge] Error: {0}", ex.Message);
        }
    }

    private void HandleHttpSpan(string name, object? payload)
    {
        if (name.EndsWith("Begin"))
        {
            var context = GetPropertyValue<object>(payload, "httpContext");
            if (context != null)
            {
                var request = GetPropertyValue<object>(context, "Request");
                var method = GetPropertyValue<string>(request, "Method") ?? "GET";
                var path = GetPropertyValue<string>(request, "Path") ?? "/";

                var activity = new Activity($"{method} {path}");
                activity.AddTag("http.method", method);
                activity.AddTag("http.url", path);
                activity.Start();
            }
        }
        else if (name.EndsWith("End"))
        {
            var activity = Activity.Current;
            if (activity != null)
            {
                activity.Stop();
                SendSpanToAgent(activity);
            }
        }
    }

    private void HandleRoutingSpan(string name, object? payload)
    {
        if (name.Contains("Match") && name.EndsWith("End"))
        {
            var activity = Activity.Current;
            if (activity != null)
            {
                var routeName = GetPropertyValue<string>(payload, "RouteName");
                if (!string.IsNullOrEmpty(routeName))
                {
                    activity.AddTag("http.route", routeName);
                }
            }
        }
    }

    private void SendSpanToAgent(Activity activity)
    {
        try
        {
            var traceId = ParseTraceId(activity.TraceId);
            var spanId = ParseSpanId(activity.SpanId);

            var span = new Span
            {
                Type = Protocol.PayloadType.Span,
                Name = activity.OperationName,
                TraceId = traceId,
                SpanId = spanId,
                StartTimestamp = activity.StartTimeUtc.Ticks,
                ServiceName = "TestApp",
                Metadata = new Dictionary<string, string>()
            };

            foreach (var tag in activity.Tags)
            {
                span.Metadata[tag.Key] = tag.Value ?? "";
            }

            span.EndTimestamp = DateTime.UtcNow.Ticks;

            if (Activity.Current?.IsAllDataRequested == true)
            {
                span.Status = Protocol.SpanStatus.Ok;
            }

            SpanChannel.Writer.TryWrite(span);
            Logger.Verbose("[OtelBridge] Sent span: {0}", activity.OperationName);
        }
        catch (Exception ex)
        {
            Logger.Warning("[OtelBridge] SendSpan error: {0}", ex.Message);
        }
    }

    private static Guid ParseTraceId(ActivityTraceId traceId)
    {
        try
        {
            var bytes = traceId.ToByteArray();
            return new Guid(bytes);
        }
        catch
        {
            return Guid.NewGuid();
        }
    }

    private static ulong ParseSpanId(ActivitySpanId spanId)
    {
        try
        {
            var bytes = spanId.ToByteArray();
            return BitConverter.ToUInt64(bytes, 0);
        }
        catch
        {
            return (ulong)Random.Shared.Next();
        }
    }

    private static T? GetPropertyValue<T>(object? obj, string propertyName)
    {
        if (obj == null) return default;

        var property = obj.GetType().GetProperty(propertyName);
        if (property == null) return default;

        var value = property.GetValue(obj);
        if (value is T typedValue)
            return typedValue;

        return default;
    }
}
