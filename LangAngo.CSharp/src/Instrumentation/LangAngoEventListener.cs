using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;
using LangAngo.CSharp.Core;
using LangAngo.CSharp.Transport;

namespace LangAngo.CSharp.Instrumentation;

public sealed class LangAngoEventListener : EventListener
{
    private const string RuntimeEventSourceName = "Microsoft-Windows-DotNETRuntime";
    private const string RuntimeEventSourceNamePrivate = "Microsoft-Windows-DotNETRuntimePrivate";
    private const string EventCounterEventSourceName = "EventCounterIntervalSec";
    private const string SampleProfilerName = "Microsoft-DotNETRuntime-SampleProfiler";

    // Keywords: JIT (0x10) ve MethodDiagnostic (0x40) sembol çözümü için kritiktir.
    private const EventKeywords JITKeyword = (EventKeywords)0x10;
    private const EventKeywords MethodDiagnosticKeyword = (EventKeywords)0x40;

    private bool _isInitialized;
    private bool _enableRuntimeEvents;
    private bool _enableGCEvents;
    private bool _enableJITEvents;
    private bool _enableContentionEvents;
    private bool _enableThreadPoolEvents;
    private bool _enableSampling;

    private readonly ConcurrentDictionary<ulong, SpanContext> _activeSpans = new();
    private readonly SymbolMapCache _symbolCache = new();
    private int _eventCount;
    private int _sampleIntervalMs = 100;

    public static LangAngoEventListener? Instance { get; private set; }

    public void Initialize()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        var enableRuntime = Environment.GetEnvironmentVariable("LANGANGO_EVENTPIPE_RUNTIME");
        var enableGC = Environment.GetEnvironmentVariable("LANGANGO_EVENTPIPE_GC");
        var enableJIT = Environment.GetEnvironmentVariable("LANGANGO_EVENTPIPE_JIT");
        var enableContention = Environment.GetEnvironmentVariable("LANGANGO_EVENTPIPE_CONTENTION");
        var enableThreadPool = Environment.GetEnvironmentVariable("LANGANGO_EVENTPIPE_THREADPOOL");
        var enableSampling = Environment.GetEnvironmentVariable("LANGANGO_EVENTPIPE_SAMPLING");
        var sampleInterval = Environment.GetEnvironmentVariable("LANGANGO_SAMPLE_INTERVAL_MS");

        _enableRuntimeEvents = enableRuntime == "true";
        _enableGCEvents = enableGC == "true" || _enableRuntimeEvents || enableGC == null;
        _enableJITEvents = enableJIT == "true" || _enableRuntimeEvents || enableJIT == null;
        _enableContentionEvents = enableContention == "true" || _enableRuntimeEvents || enableContention == null;
        _enableThreadPoolEvents = enableThreadPool == "true" || _enableRuntimeEvents || enableThreadPool == null;
        _enableSampling = enableSampling == "true" || enableSampling == null;

        if (int.TryParse(sampleInterval, out var interval) && interval > 0)
        {
            _sampleIntervalMs = interval;
        }

        Instance = this;

        Logger.Info("[EventListener] Initialized - Runtime:{0} GC:{1} JIT:{2} Contention:{3} ThreadPool:{4} Sampling:{5}",
            _enableRuntimeEvents, _enableGCEvents, _enableJITEvents, _enableContentionEvents, _enableThreadPoolEvents, _enableSampling);
    }

    private readonly List<string> printedEventSources = new();

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == RuntimeEventSourceName || 
            eventSource.Name == RuntimeEventSourceNamePrivate)
        {
            // JIT ve MethodDiagnostic olaylarını açıkça dahil ediyoruz
            EnableEvents(eventSource, EventLevel.Verbose, 
                EventKeywords.All | JITKeyword | MethodDiagnosticKeyword);
        }
        else if (eventSource.Name.Contains("EventCounter"))
        {
            EnableEvents(eventSource, EventLevel.Verbose, EventKeywords.All);
        }
        else if (_enableSampling && eventSource.Name == SampleProfilerName)
        {
            EnableEvents(eventSource, EventLevel.Verbose, EventKeywords.All);
        }

        if (!printedEventSources.Contains(eventSource.Name))
        {
            printedEventSources.Add(eventSource.Name);
            Logger.Verbose("[EventListener] Event source created: {0}", eventSource.Name);
        }
    }

    private readonly List<string> printedEventNames = new();

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (Interlocked.Increment(ref _eventCount) % 1000 == 0)
        {
            Logger.Verbose("[EventListener] Events processed: {0}", _eventCount);
        }

        if (!printedEventNames.Contains(eventData.EventName))
        {
            printedEventNames.Add(eventData.EventName);
            Logger.Verbose("[EventListener] Event written: {0}", eventData.EventName);
        }

        try
        {
            switch (eventData.EventName)
            {
                case "GCStart_V2":
                case "GCEnd_V1":
                    if (_enableGCEvents)
                        HandleGCEvent(eventData);
                    break;

                case "JITMethodInlined":
                case "JITInliningFailed":
                    if (_enableJITEvents)
                        HandleJITEvent(eventData);
                    break;


                case "ContentionStart_V2":
                case "ContentionStop":
                    if (_enableContentionEvents)
                        HandleContentionEvent(eventData);
                    break;

                case "ThreadPoolWorkerThreadAdjustment":
                case "ThreadPoolIODequeue":
                case "ThreadPoolIOPack":
                    if (_enableThreadPoolEvents)
                        HandleThreadPoolEvent(eventData);
                    break;

                case "ExceptionThrown_V1":
                    HandleExceptionEvent(eventData);
                    break;

                case "ThreadSample":
                case "ClrStackWalk":
                case "ClrMethod":
                    if (_enableSampling)
                        HandleSamplingEvent(eventData);
                    break;

                // Runtime method events (https://learn.microsoft.com/en-us/dotnet/fundamentals/diagnostics/runtime-method-events)
                case "MethodLoad_V1":
                case "MethodLoad_V2":
                case "MethodUnLoad_V1":
                case "MethodUnLoad_V2":
                case "MethodLoadVerbose_V1":
                case "MethodLoadVerbose_V2":
                case "MethodUnLoadVerbose_V1":
                case "MethodUnLoadVerbose_V2":
                case "MethodJittingStarted_V1":
                case "R2RGetEntryPoint":
                case "R2RGetEntryPointStart":
                case "MethodR2RLoadVerbose_V1":
                case "MethodILToNativeMap":
                case "MethodJitInliningSucceeded":
                case "MethodJitInliningFailed":
                case "MethodJitTailCallSucceeded":
                case "MethodJitTailCallFailed":
                    if (_enableJITEvents)
                        HandleRuntimeMethodEvent(eventData);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("[EventListener] Error processing event: {0}", ex.Message);
        }
    }

    private void HandleRuntimeMethodEvent(EventWrittenEventArgs eventData)
    {
        try
        {
            var tags = new Dictionary<string, string>();
            var names = eventData.PayloadNames;
            var payload = eventData.Payload;
            if (payload != null)
            {
                for (var i = 0; i < payload.Count; i++)
                {
                    var key = (names != null && i < names.Count) ? names[i] ?? $"Payload{i}" : $"Payload{i}";
                    var val = payload[i];
                    tags[key] = val?.ToString() ?? "";
                }
            }
            var evt = EventPipeEvent.CreateRuntimeMethodEvent(eventData.EventName ?? "RuntimeMethod", tags);
            SendEvent(evt);

            if ((eventData.EventName == "MethodLoadVerbose_V1" || eventData.EventName == "MethodLoadVerbose_V2") &&
                payload != null && payload.Count >= 8)
            {
                var startAddress = Convert.ToUInt64(payload[3]);
                var methodSize = Convert.ToUInt32(payload[4]);
                var methodName = payload[7]?.ToString() ?? "UnknownMethod";
                _symbolCache.Add(startAddress, methodSize, methodName);
                SendEvent(EventPipeEvent.CreateSymbolMap(startAddress, methodSize, methodName));
            }
        }
        catch (Exception ex)
        {
            Logger.Verbose("[EventListener] RuntimeMethodEvent error: {0}", ex.Message);
        }
    }

    private void HandleMethodLoadEvent(EventWrittenEventArgs eventData)
    {
        try
        {
            // Payload Indexleri: 3: MethodStartAddress, 4: MethodSize, 7: MethodName
            if (eventData.Payload == null || eventData.Payload.Count < 8) return;

            var startAddress = Convert.ToUInt64(eventData.Payload[3]);
            var methodSize = Convert.ToUInt32(eventData.Payload[4]);
            var methodName = eventData.Payload[7]?.ToString() ?? "UnknownMethod";

            Logger.Verbose("[EventListener] MethodLoad event: {0} 0x{1:X} {2}", methodName, startAddress, methodSize);

            var evt = EventPipeEvent.CreateSymbolMap(startAddress, methodSize, methodName);
            SendEvent(evt);
        }
        catch (Exception ex)
        {
            Logger.Verbose("[EventListener] MethodLoad Parse Error: {0}", ex.Message);
        }
    }

    private void HandleSamplingEvent(EventWrittenEventArgs eventData)
    {
        try
        {
            var activity = Activity.Current;
            var spanId = GenerateSpanId();
            var parentSpanId = activity?.Id;

            Logger.Verbose("[EventListener] Sampling event: {0}", eventData.EventName);

            var ips = new List<ulong>();
            if (eventData.Payload != null)
            {
                foreach (var item in eventData.Payload)
                {
                    if (item is ulong u) ips.Add(u);
                    else if (item is long l && l >= 0) ips.Add((ulong)l);
                    else if (item is uint u32) ips.Add(u32);
                    else if (item is int i32 && i32 >= 0) ips.Add((ulong)i32);
                }
            }

            var stackTrace = ips.Count > 0 ? string.Join(",", ips.Select(ip => $"0x{ip:X}")) : "";
            var callStack = ips.Count > 0 ? _symbolCache.BuildCallStack(ips) : null;

            if (!string.IsNullOrEmpty(stackTrace))
            {
                var guidTraceId = GetCurrentTraceId();
                Logger.Verbose("[EventListener] Trace ID: {0}", guidTraceId);

                var evt = EventPipeEvent.CreateSamplingEvent(
                    guidTraceId,
                    spanId,
                    parentSpanId != null ? GenerateSpanId() : (ulong?)null,
                    stackTrace,
                    Duration.FromMicroseconds(_sampleIntervalMs * 1000),
                    callStack
                );
                SendEvent(evt);
            }
        }
        catch (Exception ex)
        {
            Logger.Verbose("[EventListener] Sampling error: {0}", ex.Message);
        }
    }

    private void HandleGCEvent(EventWrittenEventArgs eventData)
    {
        var gen = eventData.Payload?[0] as int? ?? 0;
        var reason = eventData.Payload?[2]?.ToString() ?? "Unknown";
        
        if (eventData.EventName == "GCStart_V2")
        {
            var spanId = GenerateSpanId();
            var traceId = GetCurrentTraceId();

            Logger.Verbose("[EventListener] GCStart event: {0} {1} {2}", gen, reason, spanId);
            
            _activeSpans.TryAdd(spanId, new SpanContext
            {
                Name = $"GC.Gen{gen}",
                StartTime = Stopwatch.GetTimestamp(),
                TraceId = traceId
            });
        }
        else if (eventData.EventName == "GCEnd_V1")
        {
            var bytesBefore = eventData.Payload?[0] as int? ?? 0;
            var bytesAfter = eventData.Payload?[1] as int? ?? 0;
            
            var endTime = Stopwatch.GetTimestamp();
            SpanContext? context = null;
            
            foreach (var kvp in _activeSpans)
            {
                if (kvp.Value.Name == $"GC.Gen{gen}")
                {
                    context = kvp.Value;
                    _activeSpans.TryRemove(kvp.Key, out _);
                    break;
                }
            }

            if (context != null)
            {
                var duration = Duration.FromStopwatchTicks(endTime - context.Value.StartTime);
                var evt = EventPipeEvent.CreateGCEvent(gen, reason, bytesBefore, bytesAfter, duration, context.Value.TraceId);
                SendEvent(evt);
            }
        }
    }

    private void HandleJITEvent(EventWrittenEventArgs eventData)
    {
        var methodName = eventData.Payload?[0]?.ToString() ?? "Unknown";
        var duration = Duration.FromMicroseconds(eventData.Payload?[1] as double? ?? 0);
        var traceId = GetCurrentTraceId();

        Logger.Verbose("[EventListener] JIT event: {0} {1}", methodName, duration.TotalMilliseconds);
        
        var evt = EventPipeEvent.CreateJITEvent(methodName, duration, traceId);
        SendEvent(evt);
    }

    private DateTime _contentionStartTime;
    private string? _contentionLockName;
    private Guid _contentionTraceId;

    private void HandleContentionEvent(EventWrittenEventArgs eventData)
    {
        if (eventData.EventName == "ContentionStart_V2")
        {
            _contentionStartTime = DateTime.UtcNow;
            _contentionLockName = eventData.Payload?[0]?.ToString() ?? "Unknown";
            _contentionTraceId = GetCurrentTraceId();
        }
        else if (eventData.EventName == "ContentionStop")
        {
            if (_contentionLockName != null)
            {
                var duration = DateTime.UtcNow - _contentionStartTime;
                var evt = EventPipeEvent.CreateContentionEvent(_contentionLockName, 
                    Duration.FromMicroseconds(duration.TotalMilliseconds * 1000),
                    _contentionTraceId);
                SendEvent(evt);
            }
            _contentionLockName = null;
        }
    }

    private void HandleThreadPoolEvent(EventWrittenEventArgs eventData)
    {
        int workerThreads = 0, ioThreads = 0;

        if (eventData.Payload != null && eventData.Payload.Count > 0)
        {
            if (eventData.Payload[0] is float f)
            {
                workerThreads = (int)f;
            }
        }

        var traceId = GetCurrentTraceId();
        var evt = EventPipeEvent.CreateThreadPoolEvent(workerThreads, ioThreads, traceId);
        SendEvent(evt);
    }

    private void HandleExceptionEvent(EventWrittenEventArgs eventData)
    {
        var type = eventData.Payload?[0]?.ToString() ?? "Exception";
        var message = eventData.Payload?[1]?.ToString() ?? "";
        
        Logger.Info("[EventListener] ExceptionThrown_V1: {0} - {1}", type, message);
        
        var traceId = GetCurrentTraceId();
        
        var currentActivity = Activity.Current;
        if (currentActivity != null)
        {
            Logger.Info("[EventListener] Setting Activity error status. Activity ID: {0}", currentActivity.Id);
            currentActivity.SetStatus(ActivityStatusCode.Error, message);
            currentActivity.AddTag("error.type", type);
            currentActivity.AddTag("error.message", message);
        }
        else
        {
            Logger.Warning("[EventListener] No current Activity when exception thrown");
        }
        
        var evt = EventPipeEvent.CreateException(type, message, null, traceId);
        SendEvent(evt);
    }

    private static Guid GetCurrentTraceId()
    {
        var activity = Activity.Current;
        if (activity != null)
        {
            if (activity.Id != null && activity.Id.Length >= 34)
            {
                try
                {
                    var id = activity.Id;
                    if (id.StartsWith("00-") && id.Length >= 35)
                    {
                        return new Guid(id.Substring(3, 32));
                    }
                }
                catch
                {
                }
            }
        }
        
        var activeTraceId = TraceContext.TryGetActiveTraceId();
        if (activeTraceId.HasValue)
            return activeTraceId.Value;
        
        var ctx = TraceContext.TryGetCurrent();
        return ctx?.TraceId ?? Guid.NewGuid();
    }

    private static ulong? GetCurrentSpanId()
    {
        var activity = Activity.Current;
        if (activity != null && activity.Id != null && activity.Id.Length >= 36)
        {
            try
            {
                var id = activity.Id;
                if (id.Length >= 36)
                {
                    var spanIdStr = id.Substring(35, 16);
                    return Convert.ToUInt64(spanIdStr, 16);
                }
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    private void SendEvent(in EventPipeEvent evt)
    {
        try
        {
            var span = new Span
            {
                Type = Protocol.PayloadType.Span,
                TraceId = evt.TraceId != Guid.Empty ? evt.TraceId : Guid.NewGuid(),
                SpanId = evt.SpanId != 0 ? evt.SpanId : GenerateSpanId(),
                ParentId = evt.ParentSpanId,
                Name = $"[EventPipe] {evt.Name ?? "unknown"}",
                ServiceName = "eventpipe",
                StartTimestamp = (long)evt.Timestamp,
                Status = Protocol.SpanStatus.Unset
            };

            if (evt.Tags != null)
            {
                foreach (var tag in evt.Tags)
                {
                    span.Metadata.TryAdd(tag.Key, tag.Value);
                }
            }

            span.Metadata.TryAdd("event_type", ((byte)evt.EventType).ToString("X2"));
            if (!string.IsNullOrEmpty(evt.Value))
            {
                span.Metadata.TryAdd("event_value", evt.Value);
            }

            SpanChannel.Writer.TryWrite(span);
            
            if(!evt.EventType.ToString().Contains("Runtime") && !evt.EventType.ToString().Contains("Symbol"))
            {
                Logger.Verbose("[EventListener] Event sent: {0} {1}", evt.EventType, evt.Name);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning("[EventListener] SendEvent error: {0}", ex.Message);
        }
    }

    private int CalculateEventSize(in EventPipeEvent evt)
    {
        var nameLen = evt.Name?.Length ?? 0;
        var valueLen = evt.Value?.Length ?? 0;
        var tagsLen = evt.Tags?.Sum(k => k.Key.Length + k.Value.Length + 4) ?? 0;
        
        return 1 + 1 + 8 + 16 + 8 + 8 + 1 + 4 + nameLen + 4 + valueLen + 2 + tagsLen;
    }

    private void WriteHeader(SpanWriter writer, int payloadSize, byte payloadType)
    {
        writer.WriteByte(Protocol.Magic1);
        writer.WriteByte(Protocol.Magic2);
        writer.WriteByte(Protocol.Version);
        writer.WriteByte(payloadType);
        writer.WriteInt32(payloadSize);
        writer.WriteUInt64((ulong)DateTimeOffset.UtcNow.UtcTicks);
        writer.WriteUInt64(0);
        writer.WriteUInt64(0);
    }

    private static ulong GenerateSpanId()
    {
        var bytes = new byte[8];
        Random.Shared.NextBytes(bytes);
        return BitConverter.ToUInt64(bytes, 0);
    }

    public void RecordSpanStart(string name, Guid traceId, ulong spanId, ulong? parentSpanId)
    {
        if (traceId == default)
        {
            traceId = GetCurrentTraceId();
        }
        
        if (!parentSpanId.HasValue)
        {
            parentSpanId = GetCurrentSpanId();
        }
        
        var evt = EventPipeEvent.CreateSpanStart(name, traceId, spanId, parentSpanId);
        SendEvent(evt);
    }

    public void RecordSpanEnd(string name, Guid traceId, ulong spanId)
    {
        if (traceId == default)
        {
            traceId = GetCurrentTraceId();
        }
        
        var evt = EventPipeEvent.CreateSpanEnd(name, traceId, spanId);
        SendEvent(evt);
    }

    private struct SpanContext
    {
        public string Name;
        public long StartTime;
        public Guid TraceId;
    }
}