using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using LangAngo.CSharp.Core;
using LangAngo.CSharp.Transport;

namespace LangAngo.CSharp.Instrumentation;

public sealed class MethodTracer
{
    private static readonly AsyncLocal<Stack<(TraceContext? Prev, Span Span)>?> _activeSpanStack = new();
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

    /// <summary>Source generator or manual calls (Caller* attributes).</summary>
    public static void MethodEnter([CallerMemberName] string methodName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        MethodEnterCore(methodName, null, filePath, lineNumber);
    }

    /// <summary>Cecil and Profiler instrumentation: explicit method/type names (no Caller*).</summary>
    public static void MethodEnter(string methodName, string? declaringTypeFullName = null, string? filePath = null, int lineNumber = 0)
    {
        MethodEnterCore(methodName, declaringTypeFullName, filePath ?? "", lineNumber);
    }

    /// <summary>Weaver-only: exactly 2 args so IL can emit ldstr, ldstr, call without optional params.</summary>
    public static void MethodEnter(string methodName, string? declaringTypeFullName)
    {
        MethodEnterCore(methodName, declaringTypeFullName, "", 0);
    }

    private static void MethodEnterCore(string methodName, string? declaringTypeFullName, string filePath, int lineNumber)
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
            var previous = TraceContext.TryGetCurrent();
            var child = TraceContext.CreateChild(Protocol.SpanKind.Internal);
            child.SetAsCurrent();

            var spanName = string.IsNullOrEmpty(declaringTypeFullName) ? methodName : $"{declaringTypeFullName}.{methodName}";
            var span = new Span
            {
                Type = Protocol.PayloadType.Span,
                TraceId = child.TraceId,
                SpanId = child.SpanId,
                ParentId = child.ParentId,
                Kind = Protocol.SpanKind.Internal,
                Name = spanName,
                ServiceName = child.ServiceName,
                StartTimestamp = Stopwatch.GetTimestamp()
            };
            span.Metadata["method.name"] = methodName;
            span.Metadata["method.file"] = filePath;
            span.Metadata["method.line"] = lineNumber.ToString();
            if (!string.IsNullOrEmpty(declaringTypeFullName))
                span.Metadata["method.declaringType"] = declaringTypeFullName;

            lock (_lock)
            {
                var stack = _activeSpanStack.Value ??= new Stack<(TraceContext? Prev, Span Span)>();
                stack.Push((Prev: previous, Span: span));
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("MethodEnter error: {0}", ex.Message);
        }
    }

    public static void MethodLeave()
    {
        Logger.Verbose("MethodLeave");
        
        lock (_lock)
        {
            var stack = _activeSpanStack.Value;
            if (stack == null || stack.Count == 0)
            {
                Logger.Warning("MethodLeave: no span found");
                return;
            }
            var data = stack.Pop();
            if (stack.Count == 0)
                _activeSpanStack.Value = null;

            var methodSpan = data.Span;
            if (methodSpan == null) return;

            try
            {
                methodSpan.EndTimestamp = Stopwatch.GetTimestamp();
                methodSpan.Metadata["duration.ns"] = methodSpan.DurationNanoseconds.ToString("F0");

                Logger.Verbose("Sending method span to channel: {0}", methodSpan.Name);
                SpanChannel.Writer.TryWrite(methodSpan);
            }
            catch (Exception ex)
            {
                Logger.Warning("MethodLeave error: {0}", ex.Message);
            }
            finally
            {
                if (data.Prev != null)
                    data.Prev.SetAsCurrent();
                else
                    TraceContext.Clear();
            }
        }
    }
}
