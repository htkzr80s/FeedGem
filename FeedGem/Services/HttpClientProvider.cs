using FeedGem.Core;
using System.Net.Http;
using System.Net.Http.Headers;

namespace FeedGem.Services
{
    public static class HttpClientProvider
    {
        // アプリ全体で共有するHttpClient
        public static readonly HttpClient Client;

        static HttpClientProvider()
        {
            // 自動デコンプレッションを有効化したハンドラーの生成
            var handler = new HttpClientHandler()
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All,

                // リダイレクトを自動で追う（デフォルトは有効だが明示する）
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
            };

            Client = new HttpClient(handler);

            // Chrome 124 相当の User-Agent
            Client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

            // ブラウザが送る標準的な Accept ヘッダー
            Client.DefaultRequestHeaders.Accept.ParseAdd(
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");

            // 圧縮を許容することをサーバーに伝える
            Client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate, br");

            // 接続を使い回す（Keep-Alive）
            Client.DefaultRequestHeaders.Connection.ParseAdd("keep-alive");

            // キャッシュを使わず常に最新を取得する
            Client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
            {
                NoCache = true
            };

            // Sec-Fetch 系ヘッダー（フィード取得に適した値に設定する）
            Client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
            Client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
            Client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");

            // 起動時の言語設定を反映
            UpdateAcceptLanguage();

            // 通信タイムアウトの設定
            Client.Timeout = TimeSpan.FromSeconds(30);
        }

        // 現在のアプリ設定に基づきAccept-Languageヘッダーを更新する
        public static void UpdateAcceptLanguage()
        {
            // 既存のヘッダーを一度クリア
            Client.DefaultRequestHeaders.AcceptLanguage.Clear();

            // アプリで設定されている現在の言語コードを取得
            string currentLang = AppSettings.Language;

            if (currentLang == "ja")
            {
                // 日本語設定の場合：日本語を最優先、次に英語を候補に挙げる
                Client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("ja"));
                Client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-US", 0.7));
                Client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en", 0.3));
            }
            else
            {
                // 英語やその他の場合：設定言語を最優先、次に汎用的な英語
                Client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(currentLang));
                Client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en", 0.5));
            }
        }
    }
}