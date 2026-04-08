using System.Net.Http;

namespace FeedGem.Services
{
    public static class HttpClientProvider
    {
        // アプリ全体で共有するHttpClient
        public static readonly HttpClient Client;

        static HttpClientProvider()
        {
            Client = new HttpClient();

            // UserAgent（重要：拒否回避）
            Client.DefaultRequestHeaders.UserAgent.ParseAdd("FeedGem/1.0");

            // タイムアウト（長すぎると固まる）
            Client.Timeout = TimeSpan.FromSeconds(30);
        }
    }
}