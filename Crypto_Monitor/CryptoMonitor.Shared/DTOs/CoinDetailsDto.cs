namespace CryptoMonitor.Shared.DTOs;

public class CoinDetailsDto
{
    public string CoinId { get; set; } = string.Empty;
    public string CoinName { get; set; } = string.Empty;
    public string Ticker { get; set; } = string.Empty;
    public decimal CurrentPrice { get; set; }
    public decimal MarketCap { get; set; }
    public decimal PriceChangePercentage24h { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
}
