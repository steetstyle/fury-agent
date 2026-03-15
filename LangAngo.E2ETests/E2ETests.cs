using System.Diagnostics;
using System.Net;
using Xunit;

namespace LangAngo.E2ETests;

public class E2ETests : IAsyncLifetime
{
    private readonly string _socketPath = "/tmp/langango.sock";
    private Process? _agentProcess;
    private Process? _appProcess;
    private readonly string _appUrl = "http://127.0.0.1:5100";
    private HttpClient? _httpClient;

    public async Task InitializeAsync()
    {
        await StartAgentAsync();
        await StartTestAppAsync();
        _httpClient = new HttpClient { BaseAddress = new Uri(_appUrl) };
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task DisposeAsync()
    {
        _httpClient?.Dispose();
        _appProcess?.Kill(true);
        _agentProcess?.Kill(true);
        await Task.Delay(500);
    }

    private async Task StartAgentAsync()
    {
        var agentPath = Path.GetFullPath("../../LangAngo.Agent/cmd/agent/agent");
        
        if (!File.Exists(agentPath))
        {
            Console.WriteLine($"Agent not found at: {agentPath}");
            return;
        }
        
        var startInfo = new ProcessStartInfo
        {
            FileName = agentPath,
            Arguments = $"-socket {_socketPath}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _agentProcess = Process.Start(startInfo);
        await Task.Delay(1000);
    }

    private async Task StartTestAppAsync()
    {
        var appPath = Path.GetFullPath("../../LangAngo.TestApp/bin/Debug/net8.0/LangAngo.TestApp.dll");
        
        if (!File.Exists(appPath))
        {
            Console.WriteLine($"App not found at: {appPath}");
            return;
        }
        
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run -f net8.0 --no-build --project {appPath}",
            EnvironmentVariables =
            {
                { "LANGANGO_SOCKET", _socketPath },
                { "LANGANGO_EVENTPIPE", "true" },
                { "LANGANGO_USE_OPENTELEMETRY", "true" },
                { "DOTNET_STARTUP_HOOKS", "../../LangAngo.CSharp/bin/Debug/net8.0/LangAngo.CSharp.dll" },
                { "ASPNETCORE_URLS", _appUrl }
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetFullPath("../../LangAngo.TestApp")
        };

        _appProcess = Process.Start(startInfo);
        await Task.Delay(3000);
    }

    [Fact]
    public async Task Test01_StandardEntry_InboundHTTP()
    {
        var sw = Stopwatch.StartNew();
        var response = await _httpClient!.GetAsync("/api/welcome");
        sw.Stop();

        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Hello World", content);

        Console.WriteLine($"[Test01] Inbound HTTP: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Test02_Messenger_OutboundHttpClient()
    {
        var sw = Stopwatch.StartNew();
        var response = await _httpClient!.GetAsync("/api/http-outbound");
        sw.Stop();

        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

        Console.WriteLine($"[Test02] Outbound HTTP: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Test03_DB_Simulation()
    {
        var sw = Stopwatch.StartNew();
        var response = await _httpClient!.GetAsync("/api/db-simulation");
        sw.Stop();

        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Simulated DB query", content);

        Console.WriteLine($"[Test03] DB Simulation: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Test04_CallStack_Sampling()
    {
        var sw = Stopwatch.StartNew();
        var response = await _httpClient!.GetAsync("/api/heavy-calculation");
        sw.Stop();

        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

        Console.WriteLine($"[Test04] Call Stack Sampling: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Test05_SilentException_Capture()
    {
        var response = await _httpClient!.GetAsync("/api/exception-test");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        Console.WriteLine($"[Test05] Exception captured: {response.StatusCode}");
    }

    [Fact]
    public async Task Test06_Nested_Calls()
    {
        var sw = Stopwatch.StartNew();
        var response = await _httpClient!.GetAsync("/api/nested");
        sw.Stop();

        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Deep nested call", content);

        Console.WriteLine($"[Test06] Nested Calls: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Test07_Performance_Overhead()
    {
        var iterations = 100;
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            await _httpClient!.GetAsync("/api/welcome");
        }

        sw.Stop();
        var avgMs = sw.ElapsedMilliseconds / (double)iterations;

        Console.WriteLine($"[Test07] Performance: {iterations} requests in {sw.ElapsedMilliseconds}ms (avg: {avgMs:F2}ms/request)");

        Assert.True(avgMs < 100, $"Average latency too high: {avgMs}ms");
    }

    [Fact]
    public async Task Test08_GC_Event_Capture()
    {
        var sw = Stopwatch.StartNew();
        var response = await _httpClient!.GetAsync("/api/gc-test");
        sw.Stop();

        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

        Console.WriteLine($"[Test08] GC Event: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Test09_Parallel_Requests()
    {
        var sw = Stopwatch.StartNew();
        
        var task1 = _httpClient!.GetAsync("/api/slow");
        var task2 = _httpClient!.GetAsync("/api/welcome");
        
        await Task.WhenAll(task1, task2);
        sw.Stop();

        Assert.True(task1.Result.IsSuccessStatusCode);
        Assert.True(task2.Result.IsSuccessStatusCode);

        Console.WriteLine($"[Test09] Parallel: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Test10_Risky_Calculation()
    {
        var sw = Stopwatch.StartNew();
        var response = await _httpClient!.GetAsync("/api/calculate-risk?userId=123");
        sw.Stop();

        Assert.True(response.IsSuccessStatusCode, $"Expected success, got {response.StatusCode}");

        Console.WriteLine($"[Test10] Risky Calculation: {sw.ElapsedMilliseconds}ms");
    }
}
