using FeedGem.Services;
using System.Windows;
using System.Windows.Controls;
using static FeedGem.Services.LocalizationService;

namespace FeedGem.Views
{
    public partial class SettingsWindow : Window
    {
        // キャンセル時に復元するため、元のテーマと言語を保持する
        private readonly string _originalTheme;
        private readonly string _originalLanguage;

        public SettingsWindow()
        {
            InitializeComponent();

            // 現在の設定をロード
            var config = App.LoadConfig();
            _originalTheme = config.Theme;
            _originalLanguage = config.Language ?? "en-US";

            // 利用可能な言語をスキャンしてコンボボックスを構築する
            PopulateLanguageCombo();

            // UIの初期選択状態をセット
            InitializeSelections(_originalTheme, _originalLanguage);

            // 初期化後のイベント購読
            RadioDark.Checked += RadioTheme_Checked;
            RadioLight.Checked += RadioTheme_Checked;
            RadioAuto.Checked += RadioTheme_Checked;
            ComboLanguage.SelectionChanged += ComboLanguage_SelectionChanged;

            // 言語変更イベントに登録
            LocalizationService.Instance.LanguageChanged += ApplyTranslations;

            // 翻訳を適用
            ApplyTranslations();
        }

        // Language フォルダをスキャンし、ComboBox を動的に構築する
        private void PopulateLanguageCombo()
        {
            ComboLanguage.Items.Clear();

            var languages = LocalizationService.DiscoverAvailableLanguages();
            foreach (var lang in languages)
            {
                ComboLanguage.Items.Add(new ComboBoxItem
                {
                    Content = lang.DisplayName,
                    Tag = lang.CultureCode
                });
            }
        }

        // コントロールの初期選択状態を反映
        private void InitializeSelections(string theme, string lang)
        {
            // テーマラジオボタンの選択
            switch (theme)
            {
                case "Dark": RadioDark.IsChecked = true; break;
                case "Light": RadioLight.IsChecked = true; break;
                default: RadioAuto.IsChecked = true; break;
            }

            // 言語コンボボックスの選択（Tagを基準に検索）
            foreach (ComboBoxItem item in ComboLanguage.Items)
            {
                if (item.Tag.ToString() == lang)
                {
                    ComboLanguage.SelectedItem = item;
                    break;
                }
            }
        }

        // ラジオボタン選択時にテーマを即時適用（保存は行わない）
        private void RadioTheme_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radio && radio.IsChecked == true)
            {
                string theme = radio.Name.Replace("Radio", "");
                App.ApplyThemePreview(theme);
            }
        }

        // 言語プルダウン変更時のイベント
        private void ComboLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboLanguage.SelectedItem is ComboBoxItem item)
            {
                // 選択されたカルチャコードを取得
                string lang = item.Tag.ToString()!;

                // 言語のプレビュー適用
                App.ApplyLanguagePreview(lang);

                // UIテキストの再適用
                ApplyTranslations();
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var config = App.LoadConfig();

            // 現在選択されているテーマを判定
            config.Theme = RadioDark.IsChecked == true ? "Dark" :
                          RadioLight.IsChecked == true ? "Light" : "Auto";

            // 現在選択されている言語を取得
            config.Language = (ComboLanguage.SelectedItem as ComboBoxItem)?.Tag.ToString() ?? "en-US";

            // 設定ファイルへ保存
            App.SaveConfig(config);

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // 保存せずに起動時の状態を再適用
            App.ApplyThemePreview(_originalTheme);
            App.ApplyLanguagePreview(_originalLanguage);

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