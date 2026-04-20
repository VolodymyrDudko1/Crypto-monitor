using System;
using System.Configuration;
using System.Data;
using System.Windows;
using Microsoft.Extensions.Configuration;

namespace Crypto_Monitor
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static IConfiguration Config { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Requirement 2: Centralized Exception Handling
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // Requirement 5: External Configuration
            var builder = new ConfigurationBuilder()
                .SetBasePath(System.IO.Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables();

            Config = builder.Build();

            base.OnStartup(e);
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                FormatExceptionDetails("Error", e.Exception),
                "Global Error Handler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true; // Prevent the application from crashing
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            MessageBox.Show(
                FormatExceptionDetails("Critical Error", exception),
                "Global Error Handler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private static string FormatExceptionDetails(string label, Exception? exception)
        {
            if (exception is null)
            {
                return $"[{label} {DateTime.Now:O}]: Unknown exception";
            }

            var details = $"[{label} {DateTime.Now:O}]\n"
                        + $"Type: {exception.GetType().FullName}\n"
                        + $"Message: {exception.Message}\n\n"
                        + $"StackTrace:\n{exception.StackTrace}";

            var inner = exception.InnerException;
            var level = 1;

            while (inner is not null)
            {
                details += $"\n\nInnerException #{level}\n"
                         + $"Type: {inner.GetType().FullName}\n"
                         + $"Message: {inner.Message}\n"
                         + $"StackTrace:\n{inner.StackTrace}";

                inner = inner.InnerException;
                level++;
            }

            return details;
        }
    }
}
