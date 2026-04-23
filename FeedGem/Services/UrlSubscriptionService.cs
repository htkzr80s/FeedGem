using FeedGem.Data;
using FeedGem.Models;
using System.IO;
using System.ServiceModel.Syndication;
using System.Xml;

namespace FeedGem.Services
{
    // サービスのすぐ上で定義
    public enum SubscribeResult
    {
        Success,
        NeedsSelection,
        NoCandidates,
        TooManyCandidates,
        SkippedOrEmpty,
        AlreadySubscribed,
        Canceled,
        Error
    }

    public class UrlSubscriptionService(FeedRepository repository)
    {
        private readonly FeedRepository _repository = repository; 

        public async Task<SubscribeResult> HandleCandidatesAsync(List<FeedCandidate> candidates)
        {
            if (candidates.Count == 0) return SubscribeResult.NoCandidates;

            if (candidates.Count > 10) return SubscribeResult.TooManyCandidates;

            if (candidates.Count == 1) return await AddFeedAsync(candidates[0]);

            if (candidates.Count > 1) return SubscribeResult.NeedsSelection;

            return SubscribeResult.Canceled;
        }

        public async Task<SubscribeResult> AddFeedAsync(FeedCandidate candidate)
        {
            long feedId = -1;
            bool isNew = false;

            try
            {
                string title = candidate.OriginalTitle;

                var http = HttpClientProvider.Client;
                using var stream = await http.GetStreamAsync(candidate.Url);

                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);

                // 空ならフィードから取得
                if (string.IsNullOrWhiteSpace(title))
                {
                    try
                    {
                        ms.Position = 0;
                        using var reader = XmlReader.Create(ms);
                        var feed = SyndicationFeed.Load(reader);

                        title = feed?.Title?.Text ?? "";
                    }
                    catch
                    {
                        // タイトル取得失敗は無視
                    }
                }

                // それでも空ならURL
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = candidate.Url;
                }

                // --- 登録 ---
                (feedId, isNew) = await _repository.AddFeedAsync("/", title, candidate.Url);

                // --- 既に登録済みかチェック ---
                if (!isNew)
                {
                    return SubscribeResult.AlreadySubscribed;
                }

                // パース
                ms.Position = 0;
                var parsedItems = FeedParser.Parse(ms);

                // 1件も取れなければ無効扱い
                if (parsedItems.Count == 0)
                {
                    await _repository.DeleteFeedAsync(feedId);
                    return SubscribeResult.SkippedOrEmpty;
                }

                // --- DB保存 ---
                await _repository.SaveEntriesAsync(feedId, parsedItems);

                return SubscribeResult.Success;
            }
            catch (Exception ex)
            {
                LoggingService.Error("購読処理失敗", ex);

                // 無効なフィードが残っていたら削除
                if (isNew && feedId > 0)
                {
                    try
                    {
                        await _repository.DeleteFeedAsync(feedId);
                    }
                    catch (Exception deleteEx)
                    {
                        LoggingService.Error("フィード削除失敗", deleteEx);
                    }
                }

                return SubscribeResult.Error;
            }
        }
    }
}