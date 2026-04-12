
using Riok.Mapperly.Abstractions;
using Crypto_Monitor.Modules.MarketData.Domain;
using Crypto_Monitor.Modules.MarketData.Infrastructure;
using Crypto_Monitor.SharedAssets.DTOs;

namespace Crypto_Monitor.Modules.MarketData.Services;

[Mapper]
public partial class CoinMapper
{
    public partial Coin ToDomain(CoinGeckoResponse apiResponse);

    [MapProperty(nameof(Coin.Id), nameof(CoinDto.CoinId))]
    [MapProperty(nameof(Coin.Name), nameof(CoinDto.CoinName))]
    [MapProperty(nameof(Coin.Symbol), nameof(CoinDto.Ticker))]
    [MapProperty(nameof(Coin.CurrentPrice), nameof(CoinDto.Price))]
    public partial CoinDto ToDto(Coin coin);
}
