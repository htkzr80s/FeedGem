using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FeedGem.Models
{
    // 記事リスト（中央ペイン）に表示するためのデータモデル
    public class ArticleItem : INotifyPropertyChanged
    {
        public string Title { get; set; } = "";
        public string Date { get; set; } = "";
        public string Url { get; set; } = "";
        public string Summary { get; set; } = "";

        private bool _isRead = false;
        
        // 未読・既読の判定フラグ（false: 未読, true: 既読）
        public bool IsRead
        {
            get => _isRead;
            set
            {
                if (_isRead != value)
                {
                    _isRead = value;
                    OnPropertyChanged(); // 値が変化したらUIへ通知
                }
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;

        // プロパティ変更イベントを発火させるヘルパーメソッド
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}