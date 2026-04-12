
using System.Text.Json.Serialization;

namespace Crypto_Monitor.Modules.MarketData.Infrastructure;

public class CoinGeckoResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("current_price")]
    public decimal CurrentPrice { get; set; }

    [JsonPropertyName("market_cap")]
    public decimal MarketCap { get; set; }
}
