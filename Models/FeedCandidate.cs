using System.ComponentModel;

namespace FeedGem.Models
{
    // フィード追加時の選択ウィンドウで使用するデータモデル
    public class FeedCandidate : INotifyPropertyChanged
    {
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public string Type { get; set; } = "";
        public string OriginalTitle { get; set; } = "";

        private bool _isSelected;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}