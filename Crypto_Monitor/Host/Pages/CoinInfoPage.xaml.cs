using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Crypto_Monitor.Modules.MarketData.Infrastructure;
using CryptoMonitor.Shared.DTOs;

namespace Crypto_Monitor.Host.Pages;

public partial class CoinInfoPage : Page
{
    private readonly CryptoApiClient _apiClient;
    private readonly CoinDto _selectedCoin;

    public CoinInfoPage(CryptoApiClient apiClient, CoinDto selectedCoin)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _selectedCoin = selectedCoin;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadCoinInfoAsync();
    }

    private async Task LoadCoinInfoAsync()
    {
        try
        {
            var info = await _apiClient.GetCoinInfoAsync(_selectedCoin.CoinId);
            TitleText.Text = info.CoinName;
            TickerText.Text = $"Ticker: {info.Ticker}";
            PriceText.Text = $"Price (USD): {info.CurrentPrice:N2}";
            MarketCapText.Text = $"Market cap: {info.MarketCap:N0}";
            ChangeText.Text = $"24h change: {info.PriceChangePercentage24h:N2}%";
            SourceText.Text = info.CoinName.Contains("fallback", StringComparison.OrdinalIgnoreCase)
                ? "Source: fallback response"
                : "Source: MarketData API";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load coin details: {ex.Message}", "Coin info", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (NavigationService?.CanGoBack == true)
        {
            NavigationService.GoBack();
        }
    }
}
