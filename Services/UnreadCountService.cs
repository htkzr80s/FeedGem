using FeedGem.Data;
using System.Threading.Tasks;

namespace FeedGem.Services
{
    public class UnreadCountService(FeedRepository repository)
    {
        private readonly FeedRepository _repository = repository;

        // 全フィードの未読件数を合計する
        public async Task<int> GetTotalUnreadAsync()
        {
            var feeds = await _repository.GetAllFeedsAsync();

            int total = 0;

            foreach (var f in feeds)
            {
                total += await _repository.GetUnreadCountAsync(f.Id);
            }

            return total;
        }
    }
}