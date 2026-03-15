var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/api/welcome", () => new { message = "Hello World", timestamp = DateTime.UtcNow });

app.MapGet("/api/users/{id}", async (int id) =>
{
    await Task.Delay(10);
    return new { id, name = $"User {id}", email = $"user{id}@test.com" };
});

app.MapGet("/api/slow", async () =>
{
    await Task.Delay(100);
    return new { message = "Slow response", delay_ms = 100 };
});

app.MapGet("/api/error", () =>
{
    throw new InvalidOperationException("Test error for exception handling");
});

app.MapGet("/api/handled-error", () =>
{
    try
    {
        throw new InvalidOperationException("Handled error test");
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

app.MapGet("/api/parallel", async () =>
{
    var task1 = Task.Run(() => DoWork("Job1"));
    var task2 = Task.Run(() => DoWork("Job2"));
    await Task.WhenAll(task1, task2);
    return new { results = new[] { task1.Result, task2.Result } };
});

app.MapGet("/api/nested", async () =>
{
    return await Level1();
});

app.MapGet("/api/gc-test", async () =>
{
    var list = new List<int>(100_000_000);
    for (int i = 0; i < 100_000_000; i++)
    {
        list.Add(i);
    }
    await Task.Delay(10);
    list.Clear();
    GC.Collect(2, GCCollectionMode.Forced, true, true);
    GC.WaitForPendingFinalizers();
    GC.Collect();
    return new { message = "GC test completed", allocated = 100_000_000 };
});

app.MapGet("/api/sampling-test", async () =>
{
    var result = CalculateRisk(12345);
    return new { risk = result, message = "Sampling test completed" };
});

app.MapGet("/api/exception-test", () =>
{
    var data = new[] { "a", "b", null, "c" };
    foreach (var item in data)
    {
        if (item == null)
        {
            throw new NullReferenceException("Item is null!");
        }
    }
    return new { message = "No exception" };
});

app.MapGet("/api/calculate-risk", (int userId) =>
{
    var risk = CalculateRisk(userId);
    return new { userId, risk, score = risk * 10.5 };
});

app.MapGet("/api/heavy-calculation", async () =>
{
    var result = HeavyBusinessMethod();
    return new { result, message = "Heavy calculation done" };
});

app.MapGet("/api/http-outbound", async () =>
{
    using var client = new HttpClient();
    var response = await client.GetAsync("https://httpbin.org/delay/1");
    return new { message = "Outbound call completed", status = (int)response.StatusCode };
});

app.MapGet("/api/db-simulation", async () =>
{
    await Task.Delay(15);
    return new { message = "Simulated DB query", rows = 10, duration_ms = 15 };
});

app.MapGet("/api/search", (string? q, int? page, string? sort) =>
{
    return new { query = q, page = page ?? 1, sort = sort ?? "relevance", results = new[] { "item1", "item2" } };
});

app.MapGet("/api/filter", (string category, string? tags, int minPrice, int? maxPrice) =>
{
    return new { category, tags, minPrice, maxPrice, items = new[] { "itemA", "itemB" } };
});

app.MapGet("/api/error-qs", (string code, string? message) =>
{
    var errorCode = int.TryParse(code, out var c) ? c : 500;
    throw new ArgumentException(message ?? $"Error with code {errorCode}");
});

app.MapGet("/api/user/{id}/profile", (int id, string? fields) =>
{
    return new { id, name = $"User {id}", fields = fields ?? "all", created = DateTime.UtcNow };
});

app.Run();

static async Task<string> DoWork(string jobName)
{
    await Task.Delay(5);
    return $"{jobName} completed";
}

static async Task<object> Level1()
{
    await Task.Delay(2);
    return await Level2();
}

static async Task<object> Level2()
{
    await Task.Delay(2);
    return await Level3();
}

static async Task<object> Level3()
{
    return new { level = 3, message = "Deep nested call" };
}

static double CalculateRisk(int userId)
{
    var random = new Random(userId);
    double risk = 0;
    for (int i = 0; i < 10000; i++)
    {
        risk += Math.Sqrt(random.NextDouble() * userId);
        risk *= 0.999;
    }
    return risk;
}

static string HeavyBusinessMethod()
{
    var sb = new System.Text.StringBuilder();
    for (int i = 0; i < 1000; i++)
    {
        sb.AppendLine($"Processing item {i}");
    }
    Thread.Sleep(50);
    return $"Processed {sb.Length} characters";
}
