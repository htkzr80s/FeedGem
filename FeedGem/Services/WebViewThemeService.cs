using Microsoft.Web.WebView2.Core;

namespace FeedGem.Services
{
    public static class WebViewThemeService
    {
        // テーマに応じたCSSを生成
        private static string GetCss(string theme, bool simple = false)
        {
            if (theme == "Dark")
            {
                return simple
                    ? @"
                        body {
                            color: #E8E8E8 !important;
                            background-color: #2D2D30 !important;
                        }"
                    : @"
                        body, body * {
                            color: #E8E8E8 !important;
                            background-color: #2D2D30 !important;
                        }
                        a {
                            color: #569CE6 !important;
                        }";
            }
            else
            {
                return simple
                    ? @"
                        body {
                            color: #000000 !important;
                            background-color: #FFFFFF !important;
                        }"
                    : @"
                        body, body * {
                            color: #000000 !important;
                            background-color: #FFFFFF !important;
                        }";
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