using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Crypto_Monitor.Modules.MarketData.Infrastructure;
using CryptoMonitor.Shared.DTOs;

namespace Crypto_Monitor.Host.Pages;

public partial class FrontPage : Page
{
    private readonly CryptoApiClient _apiClient;
    private readonly ObservableCollection<CoinDto> _coins = [];

    public FrontPage()
    {
        InitializeComponent();
        _apiClient = new CryptoApiClient(new HttpClient(), App.Config);
        CoinsGrid.ItemsSource = _coins;
        CoinsGrid.MouseDoubleClick += CoinsGrid_MouseDoubleClick;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await CheckHealthAndLoadCoinsAsync();
    }

    private async void OpenOverviewButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckHealthAndLoadCoinsAsync();
    }

    private async void RefreshDataButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckHealthAndLoadCoinsAsync();
    }

    private async Task CheckHealthAndLoadCoinsAsync()
    {
        try
        {
            SetBusyState(true);

            var health = await _apiClient.GetHealthStatusAsync();

            if (!health.IsApiReachable)
            {
                ApiStatusText.Text = "Offline ❌ MarketData API is not reachable.";
                _coins.Clear();
                return;
            }

            ApiStatusText.Text = health.IsUpstreamHealthy
                ? "Online ✅ API reachable. Upstream provider healthy."
                : "Online ⚠️ API reachable, but upstream provider is unhealthy.";

            var coins = await _apiClient.GetTopCoinsAsync();

            _coins.Clear();
            foreach (var coin in coins)
            {
                _coins.Add(coin);
            }

            if (coins.Exists(c => c.CoinName.Contains("fallback", StringComparison.OrdinalIgnoreCase)))
            {
                ApiStatusText.Text += " Fallback data is currently shown.";
            }

            LastUpdatedText.Text = $"Last updated: {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            ApiStatusText.Text = "Error ❌ Could not load data.";
            MessageBox.Show($"Failed to load coins: {ex.Message}", "Load error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private void CoinsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenCoinInfoForSelection();
    }

    private void OpenCoinInfoButton_Click(object sender, RoutedEventArgs e)
    {
        OpenCoinInfoForSelection();
    }

    private void OpenCoinInfoForSelection()
    {
        if (CoinsGrid.SelectedItem is not CoinDto selectedCoin)
        {
            MessageBox.Show("Please select a coin first.", "Coin info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        NavigationService?.Navigate(new CoinInfoPage(_apiClient, selectedCoin));
    }

    private void SetBusyState(bool isBusy)
    {
        OpenOverviewButton.IsEnabled = !isBusy;
        RefreshDataButton.IsEnabled = !isBusy;
    }
}
