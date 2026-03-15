using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using LangAngo.CSharp.Core;
using LangAngo.CSharp.Transport;

namespace LangAngo.CSharp.Instrumentation;

public sealed class MethodTracer
{
    private static readonly Dictionary<int, Span> _activeSpans = new();
    private static readonly object _lock = new();
    private static string? _includes;
    private static string? _excludes;
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _includes = Environment.GetEnvironmentVariable("LANGANGO_INCLUDES");
        _excludes = Environment.GetEnvironmentVariable("LANGANGO_EXCLUDES");

        Logger.Info("MethodTracer initialized with includes: {0}, excludes: {1}", 
            _includes ?? "none", _excludes ?? "none");

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
    }

    public static bool ShouldTrace(string methodName)
    {
        if (string.IsNullOrEmpty(_includes) && string.IsNullOrEmpty(_excludes))
            return false;

        if (!string.IsNullOrEmpty(_includes))
        {
            var includes = _includes.Split(',');
            foreach (var pattern in includes)
            {
                if (MatchesPattern(methodName, pattern.Trim()))
                    return true;
            }
            return false;
        }

        if (!string.IsNullOrEmpty(_excludes))
        {
            var excludes = _excludes.Split(',');
            foreach (var pattern in excludes)
            {
                if (MatchesPattern(methodName, pattern.Trim()))
                    return false;
            }
        }

        return true;
    }

    private static bool MatchesPattern(string name, string pattern)
    {
        if (pattern.Contains("*"))
        {
            var prefix = pattern.Replace("*", "");
            return name.StartsWith(prefix) || name.Contains(prefix);
        }
        return name.Contains(pattern) || name.EndsWith(pattern);
    }

    public static void MethodEnter([CallerMemberName] string methodName = "", 
        [CallerFilePath] string filePath = "", 
        [CallerLineNumber] int lineNumber = 0)
    {
        Logger.Verbose("MethodEnter: {0}", methodName);
        
        if (!ShouldTrace(methodName))
        {
            Logger.Verbose("MethodEnter: {0} filtered out", methodName);
            return;
        }

        Logger.Info("MethodEnter traced: {0}", methodName);

        try
        {
            var currentCtx = TraceContext.TryGetCurrent() ?? TraceContext.CreateChild();
            currentCtx.SetAsCurrent();

            var span = new Span
            {
                Type = Protocol.PayloadType.Span,
                TraceId = currentCtx.TraceId,
                SpanId = currentCtx.SpanId,
                ParentId = currentCtx.ParentId,
                Kind = Protocol.SpanKind.Internal,
                Name = $"{methodName}",
                ServiceName = currentCtx.ServiceName,
                StartTimestamp = Stopwatch.GetTimestamp()
            };

            span.Metadata["method.name"] = methodName;
            span.Metadata["method.file"] = filePath;
            span.Metadata["method.line"] = lineNumber.ToString();

            var threadId = Environment.CurrentManagedThreadId;
            lock (_lock)
            {
                _activeSpans[threadId] = span;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("MethodEnter error: {0}", ex.Message);
        }
    }

    public static void MethodLeave()
    {
        var threadId = Environment.CurrentManagedThreadId;
        Logger.Verbose("MethodLeave: threadId={0}", threadId);
        
        Span? span;
        lock (_lock)
        {
            if (!_activeSpans.TryGetValue(threadId, out span))
            {
                Logger.Warning("MethodLeave: no span found for thread {0}", threadId);
                return;
            }
            _activeSpans.Remove(threadId);
        }

        if (span == null) return;

        try
        {
            span.EndTimestamp = Stopwatch.GetTimestamp();
            
            var durationNs = span.DurationNanoseconds;
            span.Metadata["duration.ns"] = durationNs.ToString("F0");

            Logger.Verbose("Sending method span to channel: {0}", span.Name);
            SpanChannel.Writer.TryWrite(span);
        }
        catch (Exception ex)
        {
            Logger.Warning("MethodLeave error: {0}", ex.Message);
        }
        finally
        {
            TraceContext.Clear();
        }
    }
}
