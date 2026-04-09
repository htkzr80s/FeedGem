using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FeedGem.Models
{
    // 記事リスト（中央ペイン）に表示するためのデータモデル
    public class ArticleItem : INotifyPropertyChanged
    {
        private string _title = "";
        private DateTime _date = DateTime.Now;
        private string _url = "";
        private string _summary = "";
        private string _feedTitle = "";
        private bool _isRead = false;
        private long _feedId;
        private string _folderPath = "";

        // 記事タイトル
        public string Title
        {
            get => _title;
            set { if (_title != value) { _title = value; OnPropertyChanged(); } }
        }

        // 内部計算用のDateTime型
        public DateTime Date
        {
            get => _date;
            set
            {
                if (_date != value)
                {
                    _date = value;
                    OnPropertyChanged();
                    // Dateが変わったら、表示用のDisplayDateも変わったことを通知する
                    OnPropertyChanged(nameof(DisplayDate));
                }
            }
        }

        // UI（画面）の表示に使うための専用プロパティ
        public string DisplayDate => Date.ToString("yyyy/MM/dd HH:mm");

        // 記事URL
        public string Url
        {
            get => _url;
            set { if (_url != value) { _url = value; OnPropertyChanged(); } }
        }

        // 記事の概要（三点リーダー表示用など）
        public string Summary
        {
            get => _summary;
            set { if (_summary != value) { _summary = value; OnPropertyChanged(); } }
        }

        // 購読サイト名（2行目表示用）
        public string FeedTitle
        {
            get => _feedTitle;
            set { if (_feedTitle != value) { _feedTitle = value; OnPropertyChanged(); } }
        }

        // フィードID（内部識別用）
        public long FeedId
        {
            get => _feedId;
            set { if (_feedId != value) { _feedId = value; OnPropertyChanged(); } }
        }

        // フォルダパス（フォルダ既読判定用）
        public string FolderPath
        {
            get => _folderPath;
            set { if (_folderPath != value) { _folderPath = value; OnPropertyChanged(); } }
        }

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