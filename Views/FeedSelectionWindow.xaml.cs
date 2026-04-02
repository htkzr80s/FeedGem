using System.Collections.Generic;
using System.Linq;
using System.Windows;
using FeedGem.Models;

namespace FeedGem.Views
{
    public partial class FeedSelectionWindow : Window
    {
        // 選択されたフィードのリストを取得するためのプロパティ
        public List<FeedCandidate> SelectedFeeds =>
            (CandidateListBox.ItemsSource as List<FeedCandidate>)?
            .Where(f => f.IsSelected).ToList() ?? [];

        // コンストラクタ
        public FeedSelectionWindow(List<FeedCandidate> candidates)
        {
            InitializeComponent();
            SetupWindowIcon();
            CandidateListBox.ItemsSource = candidates; // リストボックスに候補をバインド
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
    }
}
