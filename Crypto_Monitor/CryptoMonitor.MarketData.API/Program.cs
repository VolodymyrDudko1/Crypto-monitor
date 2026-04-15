using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CryptoMonitor.Shared.DTOs;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton<ServerLogStore>();
builder.Services.AddHttpClient("CoinGecko", (sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();

    client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "CryptoMonitor.MarketData.API/1.0");

    var proApiKey = config["CoinGecko:ProApiKey"];
    var demoApiKey = config["CoinGecko:DemoApiKey"];

    if (!string.IsNullOrWhiteSpace(proApiKey))
    {
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-cg-pro-api-key", proApiKey);
    }
    else if (!string.IsNullOrWhiteSpace(demoApiKey))
    {
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-cg-demo-api-key", demoApiKey);
    }
});

var app = builder.Build();
var serverLogStore = app.Services.GetRequiredService<ServerLogStore>();
await serverLogStore.InitializeAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(correlationId))
    {
        correlationId = Guid.NewGuid().ToString("N");
    }

    context.Items["CorrelationId"] = correlationId;
    context.Response.Headers["X-Correlation-ID"] = correlationId;

    try
    {
        await next();
    }
    finally
    {
        var message = $"{context.Request.Method} {context.Request.Path}";
        await serverLogStore.LogAsync(correlationId, context.Request.Path, context.Response.StatusCode, message);
    }
});

app.MapGet("/api/v1/marketdata/health", async (
    HttpContext context,
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ServerLogStore logStore,
    CancellationToken cancellationToken) =>
{
    var baseUrl = config["CoinGecko:BaseUrl"] ?? "https://api.coingecko.com/api/";
    var version = config["CoinGecko:Version"] ?? "v3";
    var correlationId = context.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString("N");
    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));

    try
    {
        var httpClient = httpClientFactory.CreateClient("CoinGecko");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}{version}/ping");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);

        using var response = await httpClient.SendAsync(request, timeoutCts.Token);
        return Results.Ok(new { healthy = response.IsSuccessStatusCode, correlationId });
    }
    catch (Exception ex)
    {
        await logStore.LogAsync(correlationId, "/api/v1/marketdata/health", 503, ex.Message);
        return Results.Ok(new { healthy = false, correlationId });
    }
});

app.MapGet("/api/v1/marketdata/top-coins", async (
    HttpContext context,
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ServerLogStore logStore,
    string? vsCurrency,
    int? perPage,
    CancellationToken cancellationToken) =>
{
    var baseUrl = config["CoinGecko:BaseUrl"] ?? "https://api.coingecko.com/api/";
    var version = config["CoinGecko:Version"] ?? "v3";
    var correlationId = context.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString("N");

    var currency = string.IsNullOrWhiteSpace(vsCurrency) ? "usd" : vsCurrency;
    var pageSize = perPage is > 0 and <= 250 ? perPage.Value : 10;
    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));

    try
    {
        var httpClient = httpClientFactory.CreateClient("CoinGecko");
        var url = $"{baseUrl}{version}/coins/markets?vs_currency={currency}&order=market_cap_desc&per_page={pageSize}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);

        using var responseMessage = await httpClient.SendAsync(request, timeoutCts.Token);
        if (!responseMessage.IsSuccessStatusCode)
        {
            await logStore.LogAsync(correlationId, "/api/v1/marketdata/top-coins", (int)responseMessage.StatusCode, "Upstream call failed. Fallback used.");
            return Results.Ok(GetFallbackCoins(pageSize));
        }

        var response = await responseMessage.Content.ReadFromJsonAsync<List<CoinGeckoMarketCoinResponse>>(timeoutCts.Token);

        var result = (response ?? [])
            .Select(item => new CoinDto
            {
                CoinId = item.Id,
                CoinName = item.Name,
                Ticker = item.Symbol.ToUpperInvariant(),
                Price = item.CurrentPrice
            })
            .ToList();

        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        await logStore.LogAsync(correlationId, "/api/v1/marketdata/top-coins", 503, $"Exception: {ex.Message}. Fallback used.");
        return Results.Ok(GetFallbackCoins(pageSize));
    }
});

app.MapGet("/api/v1/marketdata/coin-info/{coinId}", async (
    HttpContext context,
    string coinId,
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ServerLogStore logStore,
    CancellationToken cancellationToken) =>
{
    var baseUrl = config["CoinGecko:BaseUrl"] ?? "https://api.coingecko.com/api/";
    var version = config["CoinGecko:Version"] ?? "v3";
    var correlationId = context.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString("N");

    if (string.IsNullOrWhiteSpace(coinId))
    {
        return Results.BadRequest("coinId is required.");
    }

    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));

    try
    {
        var httpClient = httpClientFactory.CreateClient("CoinGecko");
        var url = $"{baseUrl}{version}/coins/{coinId}?localization=false&tickers=false&community_data=false&developer_data=false&sparkline=false";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);

        using var responseMessage = await httpClient.SendAsync(request, timeoutCts.Token);
        if (!responseMessage.IsSuccessStatusCode)
        {
            await logStore.LogAsync(correlationId, "/api/v1/marketdata/coin-info", (int)responseMessage.StatusCode, "Coin info upstream error. Fallback used.");
            return Results.Ok(GetFallbackDetails(coinId));
        }

        var payload = await responseMessage.Content.ReadFromJsonAsync<CoinGeckoCoinDetailsResponse>(timeoutCts.Token);
        if (payload is null)
        {
            return Results.Ok(GetFallbackDetails(coinId));
        }

        var details = new CoinDetailsDto
        {
            CoinId = payload.Id,
            CoinName = payload.Name,
            Ticker = payload.Symbol.ToUpperInvariant(),
            CurrentPrice = payload.MarketData.CurrentPrice.Usd,
            MarketCap = payload.MarketData.MarketCap.Usd,
            PriceChangePercentage24h = payload.MarketData.PriceChangePercentage24h,
            ImageUrl = payload.Image.Large
        };

        return Results.Ok(details);
    }
    catch (Exception ex)
    {
        await logStore.LogAsync(correlationId, "/api/v1/marketdata/coin-info", 503, $"Exception: {ex.Message}. Fallback used.");
        return Results.Ok(GetFallbackDetails(coinId));
    }
});

app.Run();

static List<CoinDto> GetFallbackCoins(int count)
{
    var fallback = new List<CoinDto>
    {
        new() { CoinId = "bitcoin", CoinName = "Bitcoin (fallback)", Ticker = "BTC", Price = 0 },
        new() { CoinId = "ethereum", CoinName = "Ethereum (fallback)", Ticker = "ETH", Price = 0 },
        new() { CoinId = "tether", CoinName = "Tether (fallback)", Ticker = "USDT", Price = 0 }
    };

    return fallback.Take(Math.Max(1, count)).ToList();
}

static CoinDetailsDto GetFallbackDetails(string coinId)
{
    return new CoinDetailsDto
    {
        CoinId = coinId,
        CoinName = "Fallback coin info",
        Ticker = coinId.Length >= 3 ? coinId[..3].ToUpperInvariant() : coinId.ToUpperInvariant(),
        CurrentPrice = 0,
        MarketCap = 0,
        PriceChangePercentage24h = 0,
        ImageUrl = string.Empty
    };
}

internal sealed class CoinGeckoMarketCoinResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("current_price")]
    public decimal CurrentPrice { get; set; }
}

internal sealed class CoinGeckoCoinDetailsResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("image")]
    public CoinGeckoImageResponse Image { get; set; } = new();

    [JsonPropertyName("market_data")]
    public CoinGeckoMarketDataResponse MarketData { get; set; } = new();
}

internal sealed class CoinGeckoImageResponse
{
    [JsonPropertyName("large")]
    public string Large { get; set; } = string.Empty;
}

internal sealed class CoinGeckoMarketDataResponse
{
    [JsonPropertyName("current_price")]
    public CoinGeckoCurrencyValueResponse CurrentPrice { get; set; } = new();

    [JsonPropertyName("market_cap")]
    public CoinGeckoCurrencyValueResponse MarketCap { get; set; } = new();

    [JsonPropertyName("price_change_percentage_24h")]
    public decimal PriceChangePercentage24h { get; set; }
}

internal sealed class CoinGeckoCurrencyValueResponse
{
    [JsonPropertyName("usd")]
    public decimal Usd { get; set; }
}

internal sealed class ServerLogStore
{
    private readonly string _connectionString;

    public ServerLogStore(IConfiguration configuration)
    {
        var dbPath = configuration["ServerLogDb:Path"] ?? "server-logs.db";
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath
        }.ToString();
    }

    public async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ApiLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimestampUtc TEXT NOT NULL,
                CorrelationId TEXT NOT NULL,
                Path TEXT NOT NULL,
                StatusCode INTEGER NOT NULL,
                Message TEXT NOT NULL
            );
            """;

        await command.ExecuteNonQueryAsync();
    }

    public async Task LogAsync(string correlationId, string path, int statusCode, string message)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ApiLogs (TimestampUtc, CorrelationId, Path, StatusCode, Message)
            VALUES ($timestampUtc, $correlationId, $path, $statusCode, $message);
            """;
        command.Parameters.AddWithValue("$timestampUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$correlationId", correlationId);
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$statusCode", statusCode);
        command.Parameters.AddWithValue("$message", message);

        await command.ExecuteNonQueryAsync();
    }
}
