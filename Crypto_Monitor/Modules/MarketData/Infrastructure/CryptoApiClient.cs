using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using CryptoMonitor.Shared.DTOs;
using Microsoft.Extensions.Configuration;

namespace Crypto_Monitor.Modules.MarketData.Infrastructure;

/// <summary>
/// Client used by the host app to consume the MarketData microservice.
/// </summary>
public class CryptoApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _marketDataServiceBaseUrl;
    private readonly WpfLogStore _logStore;
    private readonly int _maxRetries;
    private readonly TimeSpan _requestTimeout;
    private readonly int _circuitFailureThreshold;
    private readonly TimeSpan _circuitBreakDuration;
    private readonly object _circuitLock = new();
    private int _consecutiveFailures;
    private DateTimeOffset _circuitOpenedUntil = DateTimeOffset.MinValue;

    public CryptoApiClient(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _marketDataServiceBaseUrl = config["MarketDataService:BaseUrl"] ?? "http://localhost:5135/api/v1/marketdata";
        _logStore = new WpfLogStore(config);
        _logStore.InitializeAsync().GetAwaiter().GetResult();

        _maxRetries = int.TryParse(config["Resilience:MaxRetries"], out var retries) ? retries : 2;
        _requestTimeout = TimeSpan.FromSeconds(int.TryParse(config["Resilience:RequestTimeoutSeconds"], out var timeoutSeconds) ? timeoutSeconds : 5);
        _circuitFailureThreshold = int.TryParse(config["Resilience:CircuitFailureThreshold"], out var threshold) ? threshold : 3;
        _circuitBreakDuration = TimeSpan.FromSeconds(int.TryParse(config["Resilience:CircuitBreakSeconds"], out var breakSeconds) ? breakSeconds : 20);
    }

    public async Task<HealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithResilienceAsync(
            path: "/health",
            requestFactory: correlationId =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_marketDataServiceBaseUrl}/health");
                request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
                return request;
            },
            responseFactory: async response =>
            {
                var payload = await response.Content.ReadFromJsonAsync<HealthResponse>(cancellationToken);
                return new HealthStatus(true, payload?.Healthy == true);
            },
            fallbackFactory: () => new HealthStatus(false, false),
            cancellationToken: cancellationToken);
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetHealthStatusAsync(cancellationToken);
        return status.IsApiReachable;
    }

    public async Task<List<CoinDto>> GetTopCoinsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithResilienceAsync(
            path: "/top-coins",
            requestFactory: correlationId =>
            {
                var url = $"{_marketDataServiceBaseUrl}/top-coins?vsCurrency=usd&perPage=10";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
                return request;
            },
            responseFactory: async response =>
            {
                var payload = await response.Content.ReadFromJsonAsync<List<CoinDto>>(cancellationToken);
                return payload ?? [];
            },
            fallbackFactory: CreateFallbackCoins,
            cancellationToken: cancellationToken);
    }

    public async Task<CoinDetailsDto> GetCoinInfoAsync(string coinId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithResilienceAsync(
            path: "/coin-info",
            requestFactory: correlationId =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_marketDataServiceBaseUrl}/coin-info/{coinId}");
                request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
                return request;
            },
            responseFactory: async response =>
            {
                var payload = await response.Content.ReadFromJsonAsync<CoinDetailsDto>(cancellationToken);
                return payload ?? CreateFallbackCoinInfo(coinId);
            },
            fallbackFactory: () => CreateFallbackCoinInfo(coinId),
            cancellationToken: cancellationToken);
    }

    private async Task<T> ExecuteWithResilienceAsync<T>(
        string path,
        Func<string, HttpRequestMessage> requestFactory,
        Func<HttpResponseMessage, Task<T>> responseFactory,
        Func<T> fallbackFactory,
        CancellationToken cancellationToken)
    {
        if (IsCircuitOpen())
        {
            var openCorrelationId = Guid.NewGuid().ToString("N");
            await _logStore.LogAsync(openCorrelationId, path, 503, "Circuit breaker is open. Fallback used.");
            return fallbackFactory();
        }

        Exception? lastError = null;

        for (var attempt = 1; attempt <= _maxRetries + 1; attempt++)
        {
            var correlationId = Guid.NewGuid().ToString("N");
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_requestTimeout);

            try
            {
                using var request = requestFactory(correlationId);
                using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Service returned HTTP {(int)response.StatusCode}", null, response.StatusCode);
                }

                var serverCorrelationId = response.Headers.TryGetValues("X-Correlation-ID", out var values)
                    ? values.FirstOrDefault() ?? correlationId
                    : correlationId;

                await _logStore.LogAsync(serverCorrelationId, path, (int)response.StatusCode, $"Attempt {attempt} succeeded.");
                RegisterSuccess();
                return await responseFactory(response);
            }
            catch (Exception ex) when (IsTransient(ex) && attempt <= _maxRetries + 1)
            {
                lastError = ex;
                await _logStore.LogAsync(correlationId, path, 500, $"Attempt {attempt} failed: {ex.Message}");

                if (attempt > _maxRetries)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), cancellationToken);
            }
        }

        RegisterFailure();
        var failedCorrelationId = Guid.NewGuid().ToString("N");
        await _logStore.LogAsync(failedCorrelationId, path, 503, $"Fallback used after retries: {lastError?.Message}");
        return fallbackFactory();
    }

    private bool IsTransient(Exception exception)
    {
        return exception is TaskCanceledException
               || exception is TimeoutException
               || exception is HttpRequestException;
    }

    private bool IsCircuitOpen()
    {
        lock (_circuitLock)
        {
            return DateTimeOffset.UtcNow < _circuitOpenedUntil;
        }
    }

    private void RegisterSuccess()
    {
        lock (_circuitLock)
        {
            _consecutiveFailures = 0;
            _circuitOpenedUntil = DateTimeOffset.MinValue;
        }
    }

    private void RegisterFailure()
    {
        lock (_circuitLock)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures >= _circuitFailureThreshold)
            {
                _circuitOpenedUntil = DateTimeOffset.UtcNow.Add(_circuitBreakDuration);
                _consecutiveFailures = 0;
            }
        }
    }

    private static List<CoinDto> CreateFallbackCoins()
    {
        return
        [
            new CoinDto { CoinId = "bitcoin", CoinName = "Bitcoin (fallback)", Ticker = "BTC", Price = 0 },
            new CoinDto { CoinId = "ethereum", CoinName = "Ethereum (fallback)", Ticker = "ETH", Price = 0 },
            new CoinDto { CoinId = "tether", CoinName = "Tether (fallback)", Ticker = "USDT", Price = 0 }
        ];
    }

    private static CoinDetailsDto CreateFallbackCoinInfo(string coinId)
    {
        return new CoinDetailsDto
        {
            CoinId = coinId,
            CoinName = "Coin info unavailable (fallback)",
            Ticker = coinId.Length >= 3 ? coinId[..3].ToUpperInvariant() : coinId.ToUpperInvariant(),
            CurrentPrice = 0,
            MarketCap = 0,
            PriceChangePercentage24h = 0,
            ImageUrl = string.Empty
        };
    }

    public sealed record HealthStatus(bool IsApiReachable, bool IsUpstreamHealthy);

    private sealed class HealthResponse
    {
        public bool Healthy { get; set; }
        public string CorrelationId { get; set; } = string.Empty;
    }
}
