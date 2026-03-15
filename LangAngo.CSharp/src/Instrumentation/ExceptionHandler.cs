using System.Diagnostics;
using LangAngo.CSharp.Core;
using LangAngo.CSharp.Transport;

namespace LangAngo.CSharp.Instrumentation;

public sealed class ExceptionHandler : BaseInstrumentationHandler
{
    public override string SourceName => "Microsoft.AspNetCore";

    public override bool CanHandle(string eventName) =>
        eventName is "Microsoft.AspNetCore.Hosting.UnhandledException"
            or "Microsoft.AspNetCore.Diagnostics.HandledException"
            or "Microsoft.AspNetCore.Mvc.AfterActionExecuted"
            or "Microsoft.AspNetCore.Routing.MatchFailed";

    public override void OnEvent(string eventName, object payload)
    {
        Logger.Info("[ExceptionHandler] Event: {0}", eventName);
        
        if (payload is Exception exception)
        {
            Logger.Info("[ExceptionHandler] Payload is Exception directly");
            OnException(exception, eventName);
            return;
        }
        
        Exception? extractedException = null;
        
        try
        {
            dynamic d = payload;
            
            if (d.Exception is Exception ex2)
            {
                Logger.Info("[ExceptionHandler] Found via d.Exception");
                extractedException = ex2;
            }
            else if (d.Error is Exception ex3)
            {
                Logger.Info("[ExceptionHandler] Found via d.Error");
                extractedException = ex3;
            }
            else if (d.ExceptionObject is Exception ex4)
            {
                Logger.Info("[ExceptionHandler] Found via d.ExceptionObject");
                extractedException = ex4;
            }
        }
        catch
        {
        }
        
        if (extractedException == null)
        {
            extractedException = ExtractExceptionViaReflection(payload);
        }
        
        if (extractedException != null)
        {
            OnException(extractedException, eventName);
        }
        else
        {
            Logger.Warning("[ExceptionHandler] No exception found in payload type: {0}", payload.GetType().FullName);
        }
    }

    private Exception? ExtractExceptionViaReflection(object payload)
    {
        try
        {
            var payloadType = payload.GetType();
            var properties = payloadType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            
            foreach (var prop in properties)
            {
                if (prop.PropertyType == typeof(Exception) || prop.PropertyType.IsSubclassOf(typeof(Exception)))
                {
                    var value = prop.GetValue(payload);
                    if (value is Exception ex)
                    {
                        Logger.Info("[ExceptionHandler] Found via reflection: {0}", prop.Name);
                        return ex;
                    }
                }
                
                if (prop.Name == "Exception" || prop.Name == "Error" || prop.Name == "ExceptionObject")
                {
                    var value = prop.GetValue(payload);
                    if (value is Exception ex)
                    {
                        Logger.Info("[ExceptionHandler] Found via reflection: {0}", prop.Name);
                        return ex;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("[ExceptionHandler] Reflection error: {0}", ex.Message);
        }
        
        return null;
    }

    private void OnException(Exception exception, string sourceEvent)
    {
        try
        {
            var exceptionType = exception.GetType().FullName ?? "Exception";
            var exceptionMessage = exception.Message ?? exception.ToString() ?? "";
            var stackTrace = exception.StackTrace;

            var traceId = TraceContext.TryGetActiveTraceId() ?? Guid.NewGuid();
            var spanId = GenerateSpanId();
            var currentCtx = TraceContext.TryGetCurrent();
            var serviceName = currentCtx?.ServiceName ?? "unknown-service";
            var kind = currentCtx?.Kind ?? Protocol.SpanKind.Server;
            
            var span = new Span
            {
                Type = Protocol.PayloadType.Exception,
                TraceId = traceId,
                SpanId = spanId,
                ParentId = currentCtx?.SpanId,
                Kind = kind,
                Name = $"Exception: {exceptionType}",
                ServiceName = serviceName,
                StartTimestamp = Stopwatch.GetTimestamp(),
                EndTimestamp = Stopwatch.GetTimestamp(),
                Status = Protocol.SpanStatus.Error
            };

            span.Metadata.TryAdd("exception.type", exceptionType);
            span.Metadata.TryAdd("exception.message", exceptionMessage.Length > 500 
                ? exceptionMessage[..500] + "..." 
                : exceptionMessage);
            span.Metadata.TryAdd("exception.source", sourceEvent);
            
            if (!string.IsNullOrEmpty(stackTrace))
            {
                span.Metadata.TryAdd("stacktrace", stackTrace.Length > 1000 
                    ? stackTrace[..1000] + "..." 
                    : stackTrace);
            }

            SpanChannel.Writer.TryWrite(span);
            Logger.Info("[ExceptionHandler] Exception captured: {0}", exceptionType);
        }
        catch (Exception ex)
        {
            Logger.Warning("[ExceptionHandler] Error: {0}", ex.Message);
        }
    }

    private static ulong GenerateSpanId()
    {
        var bytes = new byte[8];
        Random.Shared.NextBytes(bytes);
        return BitConverter.ToUInt64(bytes, 0);
    }
}
