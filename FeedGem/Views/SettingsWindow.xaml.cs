using System.Windows;
using static FeedGem.Services.LocalizationService;

namespace FeedGem.Views
{
    public partial class SettingsWindow : Window
    {
        // ダイアログ起動時の元のテーマを記憶（キャンセル時に復元するため）
        private readonly string originalTheme;

        public SettingsWindow()
        {
            InitializeComponent();

            ApplyTranslations();

            // 現在の設定を反映
            var config = App.LoadConfig();
            originalTheme = config.Theme;  // 元のテーマを保存

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

            // ラジオボタンのCheckedイベントを購読（XAML変更不要）
            // 初期設定後に購読することで不要なイベント発火を防止
            RadioDark.Checked += RadioTheme_Checked;
            RadioLight.Checked += RadioTheme_Checked;
            RadioAuto.Checked += RadioTheme_Checked;
        }

        // ラジオボタン選択時にテーマを即時適用（保存は行わない）
        private void RadioTheme_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.RadioButton radio && radio.IsChecked == true)
            {
                string theme = radio.Name.Replace("Radio", "");
                App.ApplyThemePreview(theme);
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            string selectedTheme = "Auto";
            if (RadioDark.IsChecked == true) selectedTheme = "Dark";
            else if (RadioLight.IsChecked == true) selectedTheme = "Light";

            // すでに適用済みなので保存のみ実行（App経由でConfigManager.Saveを呼ぶ）
            var config = App.LoadConfig();
            config.Theme = selectedTheme;
            App.SaveConfig(config);

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // キャンセル時は元のテーマに戻す（保存はしない）
            App.ApplyThemePreview(originalTheme);

            DialogResult = false;
            Close();
        }

        private void ApplyTranslations()
        {
            ThemeTextBlock.Text = T("OtherWindow.Settings.Text.Theme");
            RadioAuto.Content = T("OtherWindow.Settings.Radio.ThemeAuto");
            LangTextBlock.Text = T("OtherWindow.Settings.Text.Language");
        }
    }
}