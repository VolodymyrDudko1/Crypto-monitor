using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Crypto_Monitor
{
    
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
        private void SwitchTheme(object sender, RoutedEventArgs e)
        {
            

            if (Application.Current.ThemeMode.Value == "Dark")
            {
                Application.Current.ThemeMode = ThemeMode.Light;
                Resources["ThemeModeIcon"] = new BitmapImage(new Uri("pack://application:,,,/Assets/themeModeIcon.png"));
            }
            else
            {
                Application.Current.ThemeMode = ThemeMode.Dark;
                Resources["ThemeModeIcon"] = new BitmapImage(new Uri("pack://application:,,,/Assets/themeModeIconDark.png"));
                
            }
            
            
        }
    }
}