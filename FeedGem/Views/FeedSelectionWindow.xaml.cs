using FeedGem.Models;
using FeedGem.Services;
using System.Windows;
using static FeedGem.Services.LocalizationService;

namespace FeedGem.Views
{
    public partial class FeedSelectionWindow : Window
    {
        // 選択されたフィードのリストを取得するためのプロパティ
        public List<FeedCandidate> SelectedFeeds =>
            (CandidateListBox.ItemsSource as List<FeedCandidate>)?
            .Where(f => f.IsSelected).ToList() ?? [];

        private readonly bool _isLimitReached;

        // コンストラクタ
        public FeedSelectionWindow(List<FeedCandidate> candidates, bool isLimitReached)
        {
            InitializeComponent();
            _isLimitReached = isLimitReached;
            SetupWindowIcon();
            // 言語変更イベントに登録
            LocalizationService.Instance.LanguageChanged += ApplyTranslations;
            ApplyTranslations();
            this.Unloaded += Window_Unloaded;

            // まず全て未選択にする
            foreach (var c in candidates)
            {
                c.IsSelected = false;
            }

            // /feed を優先して選択
            var preferred = candidates.FirstOrDefault(c => c.Url.Contains("/feed"));

            if (preferred != null)
            {
                preferred.IsSelected = true;
            }
            else if (candidates.Count > 0)
            {
                candidates[0].IsSelected = true;
            }
            CandidateListBox.ItemsSource = candidates;
        }

        // 高DPIアイコンを適用する
        private void SetupWindowIcon()
        {
            var icon = App.GetHighDpiIcon();
            if (icon != null)
            {
                this.Icon = icon;
            }
        }

        // OKボタン押下時の処理
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true; // ダイアログの結果をtrueに設定して閉じる
        }

        // キャンセルボタン押下時の処理
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false; // ダイアログの結果をfalseに設定して閉じる
        }

        // 翻訳とテキスト設定を行うメソッド
        private void ApplyTranslations()
        {
            if (_isLimitReached)
            {
                // 上限に達した時専用の文言を表示
                SelectFeedText.Text = T("OtherWindow.Dlg.Select.LimitWarning");
            }
            else
            {
                SelectFeedText.Text = T("OtherWindow.Dlg.Select.Feed");
            }
        }

        private void Window_Unloaded(object sender, RoutedEventArgs e)
        {
            LocalizationService.Instance.LanguageChanged -= ApplyTranslations;
        }
    }
}