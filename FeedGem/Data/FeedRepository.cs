using FeedGem.Core;
using FeedGem.Models;
using Microsoft.Data.Sqlite;

namespace FeedGem.Data
{
    public class FeedRepository(string dbPath)
    {
        // データベース接続文字列
        private readonly string _connectionString = $"Data Source={dbPath}";

        // テーブルの初期化を行う
        public void Initialize()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            // トランザクションを開始して、変数 'transaction' を作成する
            using var transaction = connection.BeginTransaction();

            try
            {
                // 外部キー制約を有効化する
                using (var cmd = new SqliteCommand("PRAGMA foreign_keys = ON;", connection, transaction))
                {
                    cmd.ExecuteNonQuery();
                }

                // --- 1. feedsテーブルの作成 ---
                using (var cmd = new SqliteCommand("""
                    CREATE TABLE IF NOT EXISTS feeds (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        parent_id INTEGER DEFAULT NULL,
                        title TEXT NOT NULL,
                        url TEXT NOT NULL DEFAULT '',
                        sort_order INTEGER DEFAULT 0,
                        error_state INTEGER DEFAULT 0,
                        last_success_time TEXT,
                        last_failure_time TEXT
                    );
                    """, connection, transaction))
                {
                    cmd.ExecuteNonQuery();
                }

                // --- 2. entriesテーブルの作成（外部キー制約があるため順序が大事） ---
                using (var cmd = new SqliteCommand("""
                    CREATE TABLE IF NOT EXISTS entries (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        feed_id INTEGER NOT NULL,
                        title TEXT NOT NULL,
                        url TEXT UNIQUE NOT NULL,
                        summary TEXT,
                        published_date TEXT,
                        is_read INTEGER DEFAULT 0,
                        FOREIGN KEY(feed_id) REFERENCES feeds(id) ON DELETE CASCADE
                    );
                    """, connection, transaction))
                {
                    cmd.ExecuteNonQuery();
                }

                // --- 3. インデックスの作成 ---
                using (var cmd = new SqliteCommand("""
                    CREATE INDEX IF NOT EXISTS idx_entries_feedid_date 
                    ON entries(feed_id, published_date DESC);
                    """, connection, transaction))
                {
                    cmd.ExecuteNonQuery();
                }

                // 4. URLのユニーク制約（空欄のフォルダは重複を許容する）
                using (var cmd = new SqliteCommand("""
                    CREATE UNIQUE INDEX IF NOT EXISTS idx_feed_url 
                    ON feeds(url) 
                    WHERE url != '';
                    """, connection, transaction))
                {
                    cmd.ExecuteNonQuery();
                }

                transaction.Commit(); // すべて成功したら確定
            }
            catch (Exception ex)
            {
                // ロールバック（作業の取り消し）
                transaction.Rollback();

                Console.WriteLine("--- Database Initialization Error Details ---");
                Console.WriteLine($"Message: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                Console.WriteLine("---------------------------------------------");

                throw;
            }
        }

        // 購読中のフィード一覧を取得する
        public async Task<List<FeedInfo>> GetAllFeedsAsync()
        {
            var feeds = new List<FeedInfo>();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // 親IDと並び順でソートして取得
            string query = """
                SELECT id, parent_id, title, url, sort_order,
                       error_state, last_success_time, last_failure_time
                FROM feeds
                ORDER BY 
                parent_id IS NOT NULL,
                parent_id,
                sort_order,
                title
                """;

            using var command = new SqliteCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                feeds.Add(new FeedInfo
                {
                    Id = reader.GetInt64(0),
                    ParentId = reader.IsDBNull(1) ? null : reader.GetInt64(1),
                    Title = reader.GetString(2),
                    Url = reader.GetString(3),
                    SortOrder = reader.GetInt32(4),

                    // エラー状態を読み込む（カラムがNULLの場合は None=0 にフォールバック）
                    ErrorState = reader.IsDBNull(5) ? FeedInfo.FeedErrorState.None : (FeedInfo.FeedErrorState)reader.GetInt32(5),
                    LastSuccessTime = reader.IsDBNull(6) ? null : DateTime.ParseExact(reader.GetString(6), "s", System.Globalization.CultureInfo.InvariantCulture),
                    LastFailureTime = reader.IsDBNull(7) ? null : DateTime.ParseExact(reader.GetString(7), "s", System.Globalization.CultureInfo.InvariantCulture),
                });
            }
            return feeds;
        }

        // 指定したID（フィードまたはフォルダ）に属する記事一覧を取得する
        public async Task<List<ArticleItem>> GetEntriesAsync(long id)
        {
            var articles = new List<ArticleItem>();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // 対象IDとその子孫を再帰的に取得し、紐づく記事を日付降順で抽出する
            string query = """
                WITH RECURSIVE folder_tree(id) AS (
                    SELECT @id
                    UNION ALL
                    SELECT f.id FROM feeds f
                    JOIN folder_tree t ON f.parent_id = t.id
                )  
                SELECT 
                    e.title, 
                    e.published_date, 
                    e.url, 
                    e.summary, 
                    e.is_read, 
                    f.title
                FROM entries e
                JOIN feeds f ON e.feed_id = f.id
                WHERE f.id IN folder_tree
                ORDER BY e.published_date DESC
                """;

            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@id", id);

            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                // DB上の文字列日付をDateTime型に変換。NULLの場合はMinValueを設定する
                DateTime date = reader.IsDBNull(1)
                    ? DateTime.MinValue
                    : DateTime.ParseExact(reader.GetString(1), "s", System.Globalization.CultureInfo.InvariantCulture);

                articles.Add(new ArticleItem
                {
                    Title = reader.GetString(0),
                    Date = date,
                    Url = reader.GetString(2),
                    Summary = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    IsRead = reader.GetInt32(4) == 1,
                    FeedTitle = reader.GetString(5)
                });
            }

            return articles;
        }

        // フィード情報・フォルダを登録する
        public async Task<(long feedId, bool isNew)> AddFeedAsync(long? parentId, string title, string url)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // フィードの重複をチェック
            if (!string.IsNullOrEmpty(url))
            {
                string checkQuery = "SELECT id FROM feeds WHERE url = @url;";
                using var checkCommand = new SqliteCommand(checkQuery, connection);
                checkCommand.Parameters.AddWithValue("@url", url);

                var existing = await checkCommand.ExecuteScalarAsync();
                if (existing != null)
                {
                    return ((long)existing, false);
                }
            }

            // 指定された親階層内での最大 sort_order を取得する
            string maxOrderQuery = parentId == null
                ? "SELECT IFNULL(MAX(sort_order), -1) FROM feeds WHERE parent_id IS NULL;"
                : "SELECT IFNULL(MAX(sort_order), -1) FROM feeds WHERE parent_id = @parentId;";
            using var maxOrderCommand = new SqliteCommand(maxOrderQuery, connection);
            if (parentId != null)
                maxOrderCommand.Parameters.AddWithValue("@parentId", parentId);
            int nextOrder = Convert.ToInt32(await maxOrderCommand.ExecuteScalarAsync()) + 1;

            // 新規登録
            string insertQuery = """
                INSERT INTO feeds (parent_id, title, url, sort_order)
                VALUES (@parentId, @title, @url, @nextOrder);
                SELECT last_insert_rowid();
                """;

            using var insertCommand = new SqliteCommand(insertQuery, connection);
            insertCommand.Parameters.AddWithValue("@parentId", (object?)parentId ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("@title", title);
            insertCommand.Parameters.AddWithValue("@url", url);
            insertCommand.Parameters.AddWithValue("@nextOrder", nextOrder);

            var result = await insertCommand.ExecuteScalarAsync();

            // INSERT が競合してIDが取れなかった場合を防御する
            if (result == null || result == DBNull.Value)
            {
                // ユニーク制約により挿入できなかった場合は既存IDを再取得して返す
                string fallbackQuery = "SELECT id FROM feeds WHERE url = @url;";
                using var fallbackCmd = new SqliteCommand(fallbackQuery, connection);
                fallbackCmd.Parameters.AddWithValue("@url", url);
                var existingId = await fallbackCmd.ExecuteScalarAsync();
                return ((long)(existingId ?? 0L), false);
            }

            return ((long)result, true);
        }

        // 記事データを保存する
        public async Task SaveEntriesAsync(long feedId, List<ArticleItem> articles)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using (var pragma = new SqliteCommand("PRAGMA foreign_keys = ON;", connection))
            {
                await pragma.ExecuteNonQueryAsync();
            }

            using var transaction = await connection.BeginTransactionAsync();

            foreach (var article in articles)
            {
                string insertQuery = """
                    INSERT OR IGNORE INTO entries (feed_id, title, url, summary, published_date)
                    VALUES (@feedId, @title, @url, @summary, @pubDate)
                    """;

                using var command = new SqliteCommand(insertQuery, connection);
                command.Parameters.AddWithValue("@feedId", feedId);
                command.Parameters.AddWithValue("@title", article.Title);
                command.Parameters.AddWithValue("@url", article.Url);
                command.Parameters.AddWithValue("@summary", (object?)article.Summary ?? DBNull.Value);

                // 日付を ISO8601 形式（yyyy-MM-ddTHH:mm:ss）の文字列にして保存する
                command.Parameters.AddWithValue("@pubDate", article.Date.ToString("s"));

                command.Transaction = (SqliteTransaction)transaction;
                await command.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }

        // 記事を既読状態（is_read = 1）に更新する
        public async Task MarkAsReadAsync(string url)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            string updateQuery = "UPDATE entries SET is_read = 1 WHERE url = @url";
            using var command = new SqliteCommand(updateQuery, connection);
            command.Parameters.AddWithValue("@url", url);

            await command.ExecuteNonQueryAsync();
        }

        // 指定したID（フィードまたはフォルダ）配下の全記事を既読にする
        public async Task MarkAsReadByIdAsync(long id)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // 対象IDとその子孫を特定し、一括で更新する
            string updateQuery = """
                WITH RECURSIVE folder_tree(id) AS (
                    SELECT @id
                    UNION ALL
                    SELECT f.id FROM feeds f
                    JOIN folder_tree t ON f.parent_id = t.id
                )
                UPDATE entries 
                SET is_read = 1 
                WHERE feed_id IN folder_tree;
                """;

            using var cmd = new SqliteCommand(updateQuery, connection);
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        // 全フィードの未読件数を一括取得し、辞書形式で返す
        public async Task<Dictionary<long, int>> GetUnreadCountMapAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // 未読記事（is_read = 0）をフィードIDごとにグループ化して集計する
            string query = """
                SELECT feed_id, COUNT(*) as count
                FROM entries
                WHERE is_read = 0
                GROUP BY feed_id
                """;

            using var command = new SqliteCommand(query, connection);
            var result = new Dictionary<long, int>();

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long feedId = reader.GetInt64(0);
                int count = reader.GetInt32(1);
                result[feedId] = count;
            }

            return result;
        }

        // フィードのタイトル（title）のみを更新する
        public async Task UpdateFeedTitleAsync(long feedId, string newTitle)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            string query = "UPDATE feeds SET title = @title WHERE id = @id;";
            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@title", newTitle);
            command.Parameters.AddWithValue("@id", feedId);
            await command.ExecuteNonQueryAsync();
        }

        // フィードまたはフォルダの配置（所属と並び順）を更新する
        public async Task UpdateFeedLayoutAsync(long feedId, long? newParentId, int newOrder)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // parent_id と sort_order を更新する
            const string query = """
                UPDATE feeds 
                SET parent_id = @parentId, sort_order = @order 
                WHERE id = @id;
                """;

            using var command = new SqliteCommand(query, connection);
            // 引数の newParentId が null の場合は DBNull.Value をセットする
            command.Parameters.AddWithValue("@parentId", (object?)newParentId ?? DBNull.Value);
            command.Parameters.AddWithValue("@order", newOrder);
            command.Parameters.AddWithValue("@id", feedId);

            await command.ExecuteNonQueryAsync();
        }

        // 同階層の全項目に対して、かぶらない連番を再割り当てする
        public async Task ReorderFolderItemsAsync(IEnumerable<(long Id, int Order)> items)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using (var pragma = new SqliteCommand("PRAGMA foreign_keys = ON;", connection))
            {
                await pragma.ExecuteNonQueryAsync();
            }

            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                foreach (var (id, order) in items)
                {
                    string query = "UPDATE feeds SET sort_order = @order WHERE id = @id;";
                    using var command = new SqliteCommand(query, connection);
                    command.Parameters.AddWithValue("@order", order);
                    command.Parameters.AddWithValue("@id", id);
                    command.Transaction = (SqliteTransaction)transaction;

                    await command.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // 指定したID（フィードまたはフォルダ配下全て）を削除し、同階層の並び順を詰める
        public async Task DeleteItemAsync(long id)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using (var pragma = new SqliteCommand("PRAGMA foreign_keys = ON;", connection))
            {
                await pragma.ExecuteNonQueryAsync();
            }

            using var transaction = await connection.BeginTransactionAsync() as SqliteTransaction
                ?? throw new InvalidOperationException("Failed to begin transaction.");

            try
            {
                // 削除対象の親IDと現在の並び順を取得する
                long? parentId = null;
                int sortOrder = 0;
                string getInfo = "SELECT parent_id, sort_order FROM feeds WHERE id = @id";

                using (var cmdInfo = new SqliteCommand(getInfo, connection, transaction))
                {
                    cmdInfo.Parameters.AddWithValue("@id", id);
                    using var reader = await cmdInfo.ExecuteReaderAsync();

                    // 対象レコードが存在しない場合はロールバックして終了する
                    if (await reader.ReadAsync())
                    {
                        parentId = reader.IsDBNull(0) ? null : reader.GetInt64(0);
                        sortOrder = reader.GetInt32(1);
                    }
                    else
                    {
                        await transaction.RollbackAsync();
                        return;
                    }
                }

                // CTEを用いて指定IDとその子孫を全て削除する
                string deleteFeeds = """
                    WITH RECURSIVE folder_tree(id) AS (
                        SELECT @id
                        UNION ALL
                        SELECT f.id FROM feeds f JOIN folder_tree t ON f.parent_id = t.id
                    )
                    DELETE FROM feeds WHERE id IN folder_tree;
                    """;

                using (var cmd = new SqliteCommand(deleteFeeds, connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync();
                }

                // 削除されたアイテムより後ろにあった同階層アイテムの順番を1つ繰り上げる
                string shiftOrder = parentId == null
                    ? "UPDATE feeds SET sort_order = sort_order - 1 WHERE parent_id IS NULL AND sort_order > @sortOrder"
                    : "UPDATE feeds SET sort_order = sort_order - 1 WHERE parent_id = @parentId AND sort_order > @sortOrder";

                using (var cmdShift = new SqliteCommand(shiftOrder, connection, transaction))
                {
                    if (parentId != null)
                        cmdShift.Parameters.AddWithValue("@parentId", parentId);

                    cmdShift.Parameters.AddWithValue("@sortOrder", sortOrder);
                    await cmdShift.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new InvalidOperationException($"Failed to delete item (ID: {id}).", ex);
            }
        }

        // 指定したフォルダIDの配下に、実体のあるフィードが存在するか確認する
        public async Task<bool> IsFolderEmptyAsync(long folderId)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // 子孫すべてを取得し、URLが空欄（フォルダ）でないアイテムの数を数える
            string countQuery = """
                WITH RECURSIVE folder_tree(id) AS (
                    SELECT @folderId
                    UNION ALL
                    SELECT f.id FROM feeds f
                    JOIN folder_tree t ON f.parent_id = t.id
                )
                SELECT COUNT(*) FROM feeds 
                WHERE id IN folder_tree
                  AND url != ''
                """;

            using var cmdCount = new SqliteCommand(countQuery, connection);
            cmdCount.Parameters.AddWithValue("@folderId", folderId);

            var count = (long)(await cmdCount.ExecuteScalarAsync() ?? 0L);
            return count == 0;
        }

        // フォルダ名を変更する
        public async Task RenameFolderAsync(long folderId, string newName)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            string updateFolder = "UPDATE feeds SET title = @newName WHERE id = @id;";
            using var cmd = new SqliteCommand(updateFolder, connection);
            cmd.Parameters.AddWithValue("@newName", newName);
            cmd.Parameters.AddWithValue("@id", folderId);
            await cmd.ExecuteNonQueryAsync();
        }

        // 古い記事を削除し、各フィード最新の指定件数のみを保持する
        public async Task DeleteOldEntriesAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            string deleteQuery = """
                DELETE FROM entries 
                WHERE id IN (
                    SELECT id FROM (
                        SELECT id, ROW_NUMBER() OVER (PARTITION BY feed_id ORDER BY (published_date IS NULL), published_date DESC) as rn 
                        FROM entries
                    ) WHERE rn > @maxCount
                )
                """;

            using var deleteCmd = new SqliteCommand(deleteQuery, connection);

            deleteCmd.Parameters.AddWithValue("@maxCount", AppSettings.MaxArticleCount);

            await deleteCmd.ExecuteNonQueryAsync();
        }

        // フィードの健康状態（エラーと時刻）のみを更新する
        public async Task UpdateFeedStatusAsync(FeedInfo feed)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE feeds 
                SET 
                    error_state = @errorState, 
                    last_success_time = @lastSuccessTime, 
                    last_failure_time = @lastFailureTime
                WHERE id = @id
                """;

            command.Parameters.AddWithValue("@errorState", (int)feed.ErrorState);
            command.Parameters.AddWithValue("@lastSuccessTime", feed.LastSuccessTime?.ToString("s") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@lastFailureTime", feed.LastFailureTime?.ToString("s") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@id", feed.Id);

            await command.ExecuteNonQueryAsync();
        }

        // エラー状態のあるフィードが1件以上存在するか確認する
        public async Task<bool> HasAnyFeedErrorAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();

            // フォルダ（url = ''）を除外
            command.CommandText = """
                SELECT 1 FROM feeds
                WHERE error_state != 0
                  AND url != ''
                LIMIT 1
                """;

            var result = await command.ExecuteScalarAsync();

            return result != null;
        }
    }
}