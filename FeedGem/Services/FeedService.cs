using FeedGem.Data;
using FeedGem.Models;
using System.Net.Http;

namespace FeedGem.Services
{
    public class FeedService(FeedRepository repository)
    {
        private readonly FeedRepository _repository = repository;

        // フィード取得＆記事保存
        public async Task FetchEntriesAsync(long feedId, string url)
        {
            var http = HttpClientProvider.Client;

            try
            {
                // --- URL補正 ---
                string targetUrl = FeedUrlNormalizer.Normalize(url);

                // --- レスポンス取得 ---
                using var response = await http.GetAsync(targetUrl);

                // --- ステータスコードチェック ---
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    throw new FeedNotFoundException();
                }

                response.EnsureSuccessStatusCode();

                // --- Content-Typeチェック ---
                string mediaType = response.Content.Headers.ContentType?.MediaType ?? "";

                if (!mediaType.Contains("xml") &&
                    !mediaType.Contains("rss") &&
                    !mediaType.Contains("atom"))
                {
                    throw new Exception("RSSではないレスポンスです");
                }

                // --- Stream取得 ---
                using var stream = await response.Content.ReadAsStreamAsync();

                var articles = FeedParser.Parse(stream)
                    .OrderByDescending(a => a.Date)
                    .Take(30)
                    .ToList();

                await _repository.SaveEntriesAsync(feedId, articles);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new FeedNotFoundException();
            }
            catch
            {
                throw;
            }
        }

        // 記事を既読にする
        public async Task MarkArticleAsReadAsync(ArticleItem article)
        {
            // 状態変更（UI反映）
            if (!article.IsRead)
            {
                article.IsRead = true;

                // DB更新
                await _repository.MarkAsReadAsync(article.Url);
            }
        }

        // 指定フィードをすべて既読にする
        public async Task MarkAllAsReadAsync(long feedId)
        {
            await _repository.MarkAllAsReadAsync(feedId);
        }

        // 指定したフォルダ内のすべてのフィード記事を既読にする
        public async Task MarkFolderAsReadAsync(string folderPath)
        {
            await _repository.MarkFolderAsReadAsync(folderPath);
        }

        // フィード名変更
        public async Task RenameFeedAsync(long feedId, string newName)
        {
            var feeds = await _repository.GetAllFeedsAsync();
            var target = feeds.FirstOrDefault(f => f.Id == feedId);
            if (target == null) return;

            await _repository.UpdateFeedAsync(target.Id, target.FolderPath, newName, target.Url);
        }

        // フォルダ名変更
        public async Task RenameFolderAsync(string folderPath, string newName)
        {
            await _repository.RenameFolderAsync(folderPath, newName);
        }

        // フィード削除
        public async Task DeleteFeedAsync(long feedId)
        {
            await _repository.DeleteFeedAsync(feedId);
        }

        // フォルダ削除
        public async Task DeleteFolderAsync(string folderPath)
        {
            await _repository.DeleteFolderAsync(folderPath);
        }
    }

    public class FeedNotFoundException : Exception
    {
    }
}