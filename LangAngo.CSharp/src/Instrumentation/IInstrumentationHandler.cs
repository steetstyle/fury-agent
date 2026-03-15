using System.Diagnostics;
using LangAngo.CSharp;

namespace LangAngo.CSharp.Instrumentation;

public interface IInstrumentationHandler
{
    string SourceName { get; }
    bool CanHandle(string eventName);
    void OnEvent(string eventName, object payload);
    void Subscribe(IObserver<DiagnosticListener> listener);
}

public abstract class BaseInstrumentationHandler : IInstrumentationHandler
{
    public abstract string SourceName { get; }
    public abstract bool CanHandle(string eventName);
    public abstract void OnEvent(string eventName, object payload);

    public virtual void Subscribe(IObserver<DiagnosticListener> listener)
    {
        DiagnosticListener.AllListeners.Subscribe(listener);
    }
}

public sealed class HandlerObserver : IObserver<DiagnosticListener>
{
    private readonly IInstrumentationHandler _handler;

    public HandlerObserver(IInstrumentationHandler handler)
    {
        _handler = handler;
    }

    public void OnCompleted() { }
    public void OnError(Exception error) { }
    public void OnNext(DiagnosticListener listener)
    {
        if (listener.Name == _handler.SourceName)
        {
            var payloadObserver = new PayloadObserver(_handler);
            listener.Subscribe(payloadObserver);
            Logger.Info("Subscribed to: {0}", listener.Name);
        }
    }
}

internal sealed class PayloadObserver : IObserver<KeyValuePair<string, object?>>
{
    private readonly IInstrumentationHandler _handler;

    public PayloadObserver(IInstrumentationHandler handler)
    {
        _handler = handler;
    }

    public void OnCompleted() { }
    public void OnError(Exception error) { }
    public void OnNext(KeyValuePair<string, object?> keyValuePair)
    {
        if (keyValuePair.Value != null && _handler.CanHandle(keyValuePair.Key))
        {
            _handler.OnEvent(keyValuePair.Key, keyValuePair.Value);
        }
    }
}

public static class InstrumentationInitializer
{
    private static bool _initialized;
    private static readonly IInstrumentationHandler[] Handlers;
    private static readonly HashSet<string> _subscribedListeners = new();

    static InstrumentationInitializer()
    {
        Handlers = new IInstrumentationHandler[]
        {
            new HttpInboundHandler(),
            new HttpOutboundHandler(),
            new DbHandler(),
            new ExceptionHandler()
        };
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        Logger.Info("[InstrumentationInitializer] Starting initialization");

        foreach (var handler in Handlers)
        {
            Logger.Info("[InstrumentationInitializer] Subscribing handler: {0}", handler.SourceName);
            handler.Subscribe(new HandlerObserver(handler));
            _subscribedListeners.Add(handler.SourceName);
        }
        
        Logger.Info("[InstrumentationInitializer] Initialization complete");
    }

    public static void ReSubscribe()
    {
        DiagnosticListener.AllListeners.Subscribe(new ReSubscribeObserver());
    }

    private sealed class ReSubscribeObserver : IObserver<DiagnosticListener>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(DiagnosticListener listener)
        {
            foreach (var handler in Handlers)
            {
                if (listener.Name == handler.SourceName && !_subscribedListeners.Contains(listener.Name))
                {
                    listener.Subscribe(new PayloadObserver(handler));
                    _subscribedListeners.Add(listener.Name);
                    Logger.Info("[InstrumentationInitializer] Re-subscribed handler: {0}", handler.SourceName);
                }
            }
        }
    }
}
