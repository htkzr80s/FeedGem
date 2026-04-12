using FeedGem.Services;
using System.Windows;

namespace FeedGem.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();

            // 現在の設定を反映
            var config = App.LoadConfig();
            switch (config.Theme)
            {
                case "Dark":
                    RadioDark.IsChecked = true;
                    break;
                case "Light":
                    RadioLight.IsChecked = true;
                    break;
                default: // Auto
                    RadioAuto.IsChecked = true;
                    break;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            string selectedTheme = "Auto";
            if (RadioDark.IsChecked == true) selectedTheme = "Dark";
            else if (RadioLight.IsChecked == true) selectedTheme = "Light";

            // App経由でテーマ適用
            App.ChangeTheme(selectedTheme);

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}