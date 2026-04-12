
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Crypto_Monitor.Modules.MarketData.Domain;
using Crypto_Monitor.Modules.MarketData.Services;
using Crypto_Monitor.SharedAssets.DTOs;

namespace Crypto_Monitor.Modules.MarketData.Infrastructure;

/// <summary>
/// Infrastructure Client for fetching Market Data.
/// Implements API interactions including Versioning, parsing, and Health Checks.
/// </summary>
public class CryptoApiClient
{
    private readonly HttpClient _httpClient;
    private readonly CoinMapper _mapper;
    private readonly string _baseUrl;
    private readonly string _version;

    public CryptoApiClient(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _mapper = new CoinMapper();

        // Requirement 4 & 5: Versioning and External Configuration
        // The API version (v3) is provided by the configuration file and embedded into the URL path.
        _baseUrl = config["ApiSettings:BaseUrl"] ?? "https://api.coingecko.com/api/";
        _version = config["ApiSettings:Version"] ?? "v3";
    }

    /// <summary>
    /// Requirement 6: Health Check
    /// Verifies the availability of the external API provider.
    /// WPF App can use this endpoint to display a red/green status dot.
    /// </summary>
    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}{_version}/ping", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<CoinDto>> GetTopCoinsAsync(CancellationToken cancellationToken = default)
    {
        // Construct the versioned API endpoint URL
        string url = $"{_baseUrl}{_version}/coins/markets?vs_currency=usd&order=market_cap_desc&per_page=10";

        var response = await _httpClient.GetFromJsonAsync<List<CoinGeckoResponse>>(url, cancellationToken);

        var coinDtos = new List<CoinDto>();
        if (response != null)
        {
            foreach (var item in response)
            {
                var domainModel = _mapper.ToDomain(item);
                var dto = _mapper.ToDto(domainModel);

                // Note: Normally DTO validation (Requirement 1) is invoked manually or 
                // in an ASP.NET pipeline. In WPF we perform validate here or in the UI constraints.
                coinDtos.Add(dto);
            }
        }
        return coinDtos;
    }
}
