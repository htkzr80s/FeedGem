using FeedGem.Data;
using FeedGem.Core;
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

                if (!string.IsNullOrEmpty(mediaType) &&
                    !mediaType.Contains("xml") &&
                    !mediaType.Contains("rss") &&
                    !mediaType.Contains("atom") &&
                    !mediaType.Contains("html"))
                {
                    throw new Exception("RSSではないレスポンスです");
                }

                // --- Stream取得 ---
                using var stream = await response.Content.ReadAsStreamAsync();

                var articles = FeedParser.Parse(stream, targetUrl)
                    .OrderByDescending(a => a.Date)
                    .Take(AppSettings.MaxArticleCount)
                    .ToList();

                if (articles.Count == 0)
                {
                    throw new Exception("RSSではないレスポンスです");
                }

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

        // 指定されたIDとタイプに基づいて記事リストを返す
        public async Task<List<ArticleItem>> GetEntriesAsync(long id, TreeNodeType type)
        {
            if (type == TreeNodeType.Folder)
            {
                return await _repository.GetEntriesByFolderAsync(id);
            }
            else
            {
                return await _repository.GetEntriesByFeedIdAsync(id);
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
        public async Task MarkFolderAsReadAsync(long folderId)
        {
            await _repository.MarkFolderEntriesAsReadAsync(folderId);
        }

        // フィード名変更
        public async Task RenameFeedAsync(long feedId, string newName)
        {
            await _repository.UpdateFeedTitleAsync(feedId, newName);
        }

        // フォルダ名変更
        public async Task RenameFolderAsync(long folderId, string newName)
        {
            if (await _repository.FolderExistsAsync(newName))
            {
                throw new InvalidOperationException("同名のフォルダが既に存在します。");
            }
            await _repository.RenameFolderAsync(folderId, newName);
        }

        // フォルダ追加
        public async Task CreateFolderAsync(string folderName, TreeTag? parentTag)
        {
            if (await _repository.FolderExistsAsync(folderName))
            {
                throw new InvalidOperationException("同名のフォルダが既に存在します。");
            }

            // 親要素の情報を元に、新しいフォルダが属するパスを計算する
            string parentPath = "/";
            if (parentTag != null && parentTag.Type == TreeNodeType.Folder)
            {
                // ルート直下なら /名前、それ以外なら パス/名前 とする
                parentPath = parentTag.FolderPath == "/"
                    ? $"/{parentTag.Name}"
                    : $"{parentTag.FolderPath}/{parentTag.Name}";
            }

            string folderUrl = $"folder://{Guid.NewGuid()}";

            await _repository.AddFeedAsync(parentPath, folderName, folderUrl);
        }

        // フィード削除
        public async Task DeleteFeedAsync(long feedId)
        {
            await _repository.DeleteFeedAsync(feedId);
        }

        // フォルダ内にコンテンツ（フィード）が含まれているか確認する
        public async Task<bool> HasFolderContentsAsync(long folderId)
        {
            return !await _repository.IsFolderEmptyAsync(folderId);
        }

        // フォルダ内含め全て削除する
        public async Task DeleteFolderWithContentsAsync(long folderId)
        {
            await _repository.DeleteFolderAsync(folderId);
        }
    }

    public class FeedNotFoundException : Exception
    {
    }
}