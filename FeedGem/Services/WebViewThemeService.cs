using Microsoft.Web.WebView2.Core;

namespace FeedGem.Services
{
    public static class WebViewThemeService
    {
        // テーマ設定に応じて適用するCSS文字列を取得する
        private static string GetCss(string theme, bool simple = false)
        {
            // ダークモードかどうかの判定
            bool isDark = theme == "Dark";

            // テーマに基づいた基本色の定義
            string bgColor = isDark ? "#2D2D30" : "#FFFFFF"; // 背景色
            string fgColor = isDark ? "#E8E8E8" : "#000000"; // 文字色
            string linkColor = isDark ? "#569CE6" : "#0066CC"; // リンク色
            string borderColor = isDark ? "#3F3F46" : "#E0E0E0"; // 境界線（h2の下線など）

            // 簡易適用モード（bodyのみ）か、全要素への強制適用かを分岐
            if (simple)
            {
                // bodyタグに対して背景色と文字色を適用
                return $@"
                    body {{
                        color: {fgColor} !important;
                        background-color: {bgColor} !important;
                    }}";
            }
            else
            {
                // 全ての要素に対して色を強制し、個別のタグやクラスも定義
                return $@"
                    body, body * {{
                        color: {fgColor} !important;
                        background-color: {bgColor} !important;
                    }}
                    a {{
                        color: {linkColor} !important;
                    }}
                    h2 {{
                        border-bottom-color: {borderColor} !important;
                    }}
                    .empty {{
                        color: {(isDark ? "#999999" : "#666666")} !important;
                    }}";
            }
        }

        // 初回ロード時に自動適用されるスクリプト登録
        public static async Task InitializeAsync(CoreWebView2 webView, string theme)
        {
            string css = GetCss(theme, simple: true);

            string js = $@"
                (function() {{
                    let style = document.createElement('style');
                    style.id = 'feedgem-theme-init';
                    style.textContent = `{css}`;
                    document.documentElement.appendChild(style);
                }})();
            ";

            await webView.AddScriptToExecuteOnDocumentCreatedAsync(js);
        }

        // 現在のページに強制適用（テーマ切替・ナビゲーション後）
        public static async Task ApplyAsync(CoreWebView2 webView, string theme)
        {
            string css = GetCss(theme, simple: false);

            string js = $@"
                (function() {{
                    let old = document.getElementById('feedgem-theme');
                    if (old) old.remove();

                    let style = document.createElement('style');
                    style.id = 'feedgem-theme';
                    style.textContent = `{css}`;
                    document.documentElement.appendChild(style);
                }})();
            ";

            await webView.ExecuteScriptAsync(js);
        }
    }
}