namespace FeedGem.Models
{
    // フィード追加時の選択ウィンドウで使用するデータモデル
    public class FeedCandidate
    {
        // チェックボックスの選択状態 
        public bool IsSelected { get; set; } = true;
        
        // フィードのタイトル 
        public string Title { get; set; } = string.Empty;
        
        // フィードのURL 
        public string Url { get; set; } = string.Empty;
    }
}