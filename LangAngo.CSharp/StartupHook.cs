using LangAngo.CSharp;
using LangAngo.CSharp.Instrumentation;
using LangAngo.CSharp.Transport;

public class StartupHook
{
    private static bool _initialized = false;
    private static UdsClient? _client;
    private static LangAngoEventListener? _eventListener;

    public static void Initialize()
    {
        var envLevel = Environment.GetEnvironmentVariable("LANGANGO_LOG_LEVEL");
        Logger.Initialize(LogLevel.Verbose);
        Logger.SetLevel(envLevel);

        Logger.Info("StartupHook Initialized!");

        InstrumentationInitializer.Initialize();
        MethodTracer.Initialize();

        InitializeEventPipe();

        SetInitialized();

        _ = Task.Run(async () =>
        {
            await Task.Delay(500);

            try
            {
                _client = new UdsClient();
                await _client.ConnectAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("UDS Connection error: {0}", ex.Message);
            }
        });

        _ = Task.Run(async () =>
        {
            while (!_initialized)
            {
                await Task.Delay(100);
            }

            await Task.Delay(1500);

            InstrumentationInitializer.ReSubscribe();
            
            Logger.Info("Instrumentation ready!");
        });
    }

    private static void InitializeEventPipe()
    {
        var enableEventPipe = Environment.GetEnvironmentVariable("LANGANGO_EVENTPIPE");

        if (enableEventPipe == "true")
        {
            Logger.Info("Initializing EventPipe listener...");
            _eventListener = new LangAngoEventListener();
            _eventListener.Initialize();
        }
    }

    internal static void SetInitialized()
    {
        _initialized = true;
    }
}
