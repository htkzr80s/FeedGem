namespace FeedGem.Models
{
    // 記事リスト（中央ペイン）に表示するためのデータモデル
    public class ArticleItem
    {
        // 記事のタイトル
        public string Title { get; set; } = "";
        
        // 投稿日時（yyyy/MM/dd HH:mm 形式）
        public string Date { get; set; } = "";
        
        // 記事のURL（ブラウザで開く際に使用） 
        public string Url { get; set; } = "";
        
        // 記事の要約または本文（プレビューで使用） 
        public string Summary { get; set; } = "";
        
        // 未読・既読の判定フラグ（0: 未読, 1: 既読）
        public bool IsRead { get; set; } = false;
    }
}