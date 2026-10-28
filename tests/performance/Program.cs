using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;

var baseUrl = args[0].TrimEnd('/');
using var client = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 8 })
{
    Timeout = TimeSpan.FromSeconds(45)
};
var cases = new (string Name, string Method, string Path, string? Body)[]
{
    ("catalog", "GET", "/catalog?page=1&pageSize=20", null),
    ("search-empty", "POST", "/search", "{}"),
    ("price-sort", "GET", "/catalog?minPrice=100&maxPrice=300&sort=price-asc&page=1&pageSize=20", null),
    ("search-2-price-sort", "POST", "/search", "{\"attributes\":[{\"code\":\"perf-kolor\",\"values\":[\"czerwony\"]},{\"code\":\"perf-rozmiar\",\"values\":[\"S\"]}],\"minPrice\":100,\"maxPrice\":300,\"sort\":\"price-asc\",\"page\":1,\"pageSize\":20}"),
    ("search-5", "POST", "/search", "{\"attributes\":[{\"code\":\"perf-kolor\",\"values\":[\"czerwony\"]},{\"code\":\"perf-rozmiar\",\"values\":[\"S\"]},{\"code\":\"perf-marka\",\"values\":[\"MarkaA\"]},{\"code\":\"perf-material\",\"values\":[\"bawelna\"]},{\"code\":\"perf-styl\",\"values\":[\"casual\"]}],\"page\":1,\"pageSize\":20}")
};
foreach (var scenario in cases)
{
    async Task<(double Ms, int Status, string? Error)> RequestAsync()
    {
        using var request = new HttpRequestMessage(scenario.Method == "POST" ? HttpMethod.Post : HttpMethod.Get, baseUrl + scenario.Path);
        if (scenario.Body is not null)
            request.Content = new StringContent(scenario.Body, Encoding.UTF8, "application/json");
        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await client.SendAsync(request);
            await response.Content.ReadAsByteArrayAsync();
            sw.Stop();
            return (sw.Elapsed.TotalMilliseconds, (int)response.StatusCode, null);
        }
        catch (Exception exception)
        {
            sw.Stop();
            return (sw.Elapsed.TotalMilliseconds, 0, exception.GetType().Name);
        }
    }
    for (var i = 0; i < 3; i++) await RequestAsync();
    using var limiter = new SemaphoreSlim(5);
    var total = Stopwatch.StartNew();
    var tasks = Enumerable.Range(0, 50).Select(async _ =>
    {
        await limiter.WaitAsync();
        try { return await RequestAsync(); }
        finally { limiter.Release(); }
    }).ToArray();
    var results = await Task.WhenAll(tasks);
    total.Stop();
    var times = results.Select(result => result.Ms).Order().ToArray();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        @case = scenario.Name,
        concurrency = 5,
        requests = results.Length,
        errors = results.Count(result => result.Status != 200),
        statuses = results.GroupBy(result => result.Status).ToDictionary(group => group.Key.ToString(), group => group.Count()),
        p50Ms = Math.Round(times[24], 1),
        p95Ms = Math.Round(times[47], 1),
        maxMs = Math.Round(times[^1], 1),
        totalSeconds = Math.Round(total.Elapsed.TotalSeconds, 1),
        requestsPerSecond = Math.Round(results.Length / total.Elapsed.TotalSeconds, 1)
    }));
}
