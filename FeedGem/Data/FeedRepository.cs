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

            string createTableQuery = """
                CREATE TABLE IF NOT EXISTS feeds (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    folder_path TEXT NOT NULL DEFAULT '/',
                    title TEXT NOT NULL,
                    url TEXT UNIQUE NOT NULL
                );
                CREATE TABLE IF NOT EXISTS entries (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    feed_id INTEGER,
                    title TEXT NOT NULL,
                    url TEXT UNIQUE NOT NULL,
                    summary TEXT,
                    published_date TEXT,
                    is_read INTEGER DEFAULT 0,
                    FOREIGN KEY(feed_id) REFERENCES feeds(id)
                );
                """;

            using var command = new SqliteCommand(createTableQuery, connection);
            command.ExecuteNonQuery();

            // FeedId検索を高速化するインデックス
            using var indexCommand = new SqliteCommand("""
                CREATE INDEX IF NOT EXISTS idx_entries_feedid 
                ON entries(feed_id);
            """, connection);

            indexCommand.ExecuteNonQuery();

            // 既存テーブルへのカラム追加
            try
            {
                using var alterCommand = new SqliteCommand("ALTER TABLE feeds ADD COLUMN sort_order INTEGER DEFAULT 0;", connection);
                alterCommand.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                // カラムが既に存在する場合のエラー(SQLITE_ERROR)は無視し、それ以外を記録する
                if (ex.SqliteErrorCode != 1)
                {
                    Console.WriteLine($"[Error] sort_orderの追加に失敗しました: {ex.Message}");
                }
            }

            try
            {
                using var alterFolderCommand = new SqliteCommand("ALTER TABLE feeds ADD COLUMN folder_path TEXT NOT NULL DEFAULT '/';", connection);
                alterFolderCommand.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                if (ex.SqliteErrorCode != 1)
                {
                    Console.WriteLine($"[Error] folder_pathの追加に失敗しました: {ex.Message}");
                }
            }
        }

        // 購読中のフィード一覧を取得する
        public async Task<List<FeedInfo>> GetAllFeedsAsync()
        {
            var feeds = new List<FeedInfo>();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // sort_orderを追加し、ソート条件に含める
            string query = "SELECT id, folder_path, title, url, sort_order FROM feeds ORDER BY folder_path, sort_order, title";
            using var command = new SqliteCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                feeds.Add(new FeedInfo
                {
                    Id = reader.GetInt64(0),
                    FolderPath = reader.GetString(1),
                    Title = reader.GetString(2),
                    Url = reader.GetString(3),
                    SortOrder = reader.GetInt32(4)
                });
            }
            return feeds;
        }

        // 特定のフィードに属する記事一覧を取得する
        public async Task<List<ArticleItem>> GetEntriesByFeedIdAsync(long feedId)
        {
            var articles = new List<ArticleItem>();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // 日付の降順で記事を取得
            string query = """
                SELECT 
                    e.title, 
                    e.published_date, 
                    e.url, 
                    e.summary, 
                    e.is_read, 
                    f.title
                FROM entries e
                JOIN feeds f ON e.feed_id = f.id
                WHERE e.feed_id = @feedId
                ORDER BY e.published_date DESC
                """;

            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@feedId", feedId);

            using var reader = await command.ExecuteReaderAsync();
            // 取得したレコードをコレクションに追加
            while (await reader.ReadAsync())
            {
                // published_dateを安全に取得
                DateTime date;
                if (reader.IsDBNull(1))
                {
                    date = DateTime.MinValue;
                }
                else
                {
                    var raw = reader.GetString(1);

                    // パース失敗時は安全値にフォールバック
                    if (!DateTime.TryParse(raw, out date))
                    {
                        Console.WriteLine($"[Warn] 日付パース失敗: {raw}");
                        date = DateTime.MinValue;
                    }
                }

                // 記事データ生成
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
        public async Task<(long feedId, bool isNew)> AddFeedAsync(string folder, string title, string url)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // --- 既存チェック ---
            string checkQuery = "SELECT id FROM feeds WHERE url = @url;";
            using var checkCommand = new SqliteCommand(checkQuery, connection);
            checkCommand.Parameters.AddWithValue("@url", url);

            var existing = await checkCommand.ExecuteScalarAsync();
            if (existing != null)
            {
                return ((long)existing, false); // 既存
            }

            // --- 新規登録 ---
            string insertQuery = """
                INSERT INTO feeds (folder_path, title, url)
                VALUES (@folder, @title, @url);
                SELECT last_insert_rowid();
                """;

            using var insertCommand = new SqliteCommand(insertQuery, connection);
            insertCommand.Parameters.AddWithValue("@folder", folder);
            insertCommand.Parameters.AddWithValue("@title", title);
            insertCommand.Parameters.AddWithValue("@url", url);

            var result = await insertCommand.ExecuteScalarAsync();

            return ((long)(result ?? 0), true); // 新規
        }

        // 記事データを保存する
        public async Task SaveEntriesAsync(long feedId, List<ArticleItem> articles)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

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
                command.Parameters.AddWithValue("@summary", article.Summary);
                command.Parameters.AddWithValue("@pubDate", article.Date);
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

        // 指定フォルダ配下のすべての記事を既読にする
        public async Task MarkFolderAsReadAsync(string folderPath)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // 子フォルダも含めるパターン
            string childPattern = folderPath + "/%";

            string updateQuery = """
                UPDATE entries
                SET is_read = 1
                WHERE feed_id IN (
                    SELECT id FROM feeds
                    WHERE 
                        (folder_path = @path OR folder_path LIKE @child)
                        AND url NOT LIKE 'folder://%'
                )
            """;

            using var command = new SqliteCommand(updateQuery, connection);
            command.Parameters.AddWithValue("@path", folderPath);
            command.Parameters.AddWithValue("@child", childPattern);

            await command.ExecuteNonQueryAsync();
        }

        // 指定したフィードの未読記事数を取得する
        public async Task<int> GetUnreadCountByFeedIdAsync(long feedId)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // is_read が 0（未読）のレコード数をカウントするクエリ
            string query = "SELECT COUNT(*) FROM entries WHERE feed_id = @feedId AND is_read = 0";
            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@feedId", feedId);

            // ExecuteScalarAsyncで結果の最初の1行1列（カウント数）を取得
            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result); // int型に変換して返す
        }

        public async Task<Dictionary<long, int>> GetUnreadCountMapAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

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

        // フィード情報を更新する（タイトルやフォルダ、URLの変更用）
        public async Task UpdateFeedAsync(long id, string folderPath, string title, string url)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            string updateQuery = "UPDATE feeds SET folder_path = @folder, title = @title, url = @url WHERE id = @id";
            using var command = new SqliteCommand(updateQuery, connection);
            command.Parameters.AddWithValue("@folder", folderPath);
            command.Parameters.AddWithValue("@title", title);
            command.Parameters.AddWithValue("@url", url);
            command.Parameters.AddWithValue("@id", id);

            await command.ExecuteNonQueryAsync();
        }

        // フィードの並び順（sort_order）のみを更新する
        public async Task UpdateFeedOrderAsync(long id, int sortOrder)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            string updateQuery = "UPDATE feeds SET sort_order = @sortOrder WHERE id = @id";
            using var command = new SqliteCommand(updateQuery, connection);
            command.Parameters.AddWithValue("@sortOrder", sortOrder);
            command.Parameters.AddWithValue("@id", id);

            await command.ExecuteNonQueryAsync();
        }

        // 指定フィードの全記事を既読にする（一括更新）
        public async Task MarkAllAsReadAsync(long feedId)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE entries
                SET is_read = 1
                WHERE feed_id = @feedId;
                """;

            command.Parameters.AddWithValue("@feedId", feedId);

            await command.ExecuteNonQueryAsync();
        }

        // フィードを削除する（関連する記事も一緒に消す）
        public async Task DeleteFeedAsync(long id)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // 外部キー制約が有効なら記事も消えるが、念のため明示的に両方消す
            string deleteEntries = "DELETE FROM entries WHERE feed_id = @id";
            string deleteFeed = "DELETE FROM feeds WHERE id = @id";

            using var transaction = connection.BeginTransaction();
            try
            {
                using var cmd1 = new SqliteCommand(deleteEntries, connection, transaction);
                cmd1.Parameters.AddWithValue("@id", id);
                await cmd1.ExecuteNonQueryAsync();

                using var cmd2 = new SqliteCommand(deleteFeed, connection, transaction);
                cmd2.Parameters.AddWithValue("@id", id);
                await cmd2.ExecuteNonQueryAsync();

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // 指定したフォルダ内に「実体のあるフィード」が存在するか確認する
        public async Task<bool> IsFolderEmptyAsync(string folderPath)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // folder:// で始まる管理用データ以外の「本物のフィード」があるかカウントする
            string query = "SELECT COUNT(*) FROM feeds WHERE folder_path = @path AND url NOT LIKE 'folder://%'";
            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@path", folderPath);

            var count = (long)(await command.ExecuteScalarAsync() ?? 0L);
            return count == 0; // 0件なら空と判断
        }

        // フォルダとその配下の全データを削除する
        public async Task DeleteFolderAsync(string folderPath)
        {
            // フォルダパスを正規化（常に先頭に / を付ける）
            if (string.IsNullOrEmpty(folderPath))
                throw new ArgumentException("フォルダパスが空です", nameof(folderPath));

            if (!folderPath.StartsWith('/'))
                folderPath = "/" + folderPath.TrimEnd('/');

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // SqliteTransaction を正しく取得
            using var transaction = await connection.BeginTransactionAsync() as SqliteTransaction ?? throw new InvalidOperationException("トランザクションの開始に失敗しました");
            try
            {
                // 子フォルダすべてをカバーするLIKEパターン
                string childPattern = folderPath + "/%";

                // フォルダ名を取得（最後の部分）
                string folderName = folderPath.TrimStart('/').Split('/').LastOrDefault() ?? "";

                // --- 1. 記事（entries）の削除 ---
                // 対象フォルダおよび全子孫フォルダに属する記事をすべて削除
                string deleteEntries = """
                    DELETE FROM entries
                    WHERE feed_id IN (
                        SELECT id FROM feeds
                        WHERE folder_path = @folderPath
                           OR folder_path LIKE @childPattern ESCAPE '\'
                    )
                    """;

                using (var cmd1 = new SqliteCommand(deleteEntries, connection, transaction))
                {
                    cmd1.Parameters.AddWithValue("@folderPath", folderPath);
                    cmd1.Parameters.AddWithValue("@childPattern", childPattern);
                    await cmd1.ExecuteNonQueryAsync();
                }

                // --- 2. フィード（feeds）の削除 ---
                // フォルダ自体 + 子フォルダ + フォルダエントリ（folder://）をすべて削除
                string deleteFeeds = """
                    DELETE FROM feeds
                    WHERE folder_path = @folderPath
                       OR folder_path LIKE @childPattern ESCAPE '\'
                       OR (title = @folderName AND url LIKE 'folder://%')
                    """;

                using (var cmd2 = new SqliteCommand(deleteFeeds, connection, transaction))
                {
                    cmd2.Parameters.AddWithValue("@folderPath", folderPath);
                    cmd2.Parameters.AddWithValue("@childPattern", childPattern);
                    cmd2.Parameters.AddWithValue("@folderName", folderName);
                    await cmd2.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new InvalidOperationException($"フォルダ削除に失敗しました: {folderPath}", ex);
            }
        }

        // フォルダ名を変更する（配下すべて含む）
        public async Task RenameFolderAsync(string folderPath, string newName)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();

            try
            {
                // 新しいルートパス
                string newRoot = "/" + newName;

                // 子フォルダ用
                string childPattern = folderPath + "/%";

                // --- 1. フィードのパス更新 ---
                string updateFeeds = """
                    UPDATE feeds
                    SET folder_path = 
                        CASE
                            WHEN folder_path = @old THEN @new
                            WHEN folder_path LIKE @child THEN 
                                @new || SUBSTR(folder_path, LENGTH(@old) + 1)
                        END
                    WHERE folder_path = @old OR folder_path LIKE @child
                """;

                using (var cmd = new SqliteCommand(updateFeeds, connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@old", folderPath);
                    cmd.Parameters.AddWithValue("@new", newRoot);
                    cmd.Parameters.AddWithValue("@child", childPattern);

                    await cmd.ExecuteNonQueryAsync();
                }

                // --- 2. ダミーフォルダ更新 ---
                string folderName = folderPath.TrimStart('/').Split('/').Last();

                string updateDummy = """
                    UPDATE feeds
                    SET title = @newName
                    WHERE title = @oldName AND url LIKE 'folder://%'
                """;

                using (var cmd = new SqliteCommand(updateDummy, connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@newName", newName);
                    cmd.Parameters.AddWithValue("@oldName", folderName);

                    await cmd.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // 古い記事を削除し、各フィード最新{AppSettings.MaxArticleCount}件のみを保持する
        public async Task DeleteOldEntriesAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // ROW_NUMBER関数を使用して、各フィードの最新{AppSettings.MaxArticleCount}件以外を一括削除する
            string deleteQuery = $"""
                DELETE FROM entries 
                WHERE id IN (
                    SELECT id FROM (
                        SELECT id, ROW_NUMBER() OVER (PARTITION BY feed_id ORDER BY published_date DESC) as rn 
                        FROM entries
                    ) WHERE rn > {AppSettings.MaxArticleCount}
                )
                """;

            using var deleteCmd = new SqliteCommand(deleteQuery, connection);
            await deleteCmd.ExecuteNonQueryAsync();
        }
    }

    // 内部管理用のシンプルなクラス
    public class FeedInfo
    {
        public long Id { get; set; }
        public string FolderPath { get; set; } = "/";
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public int SortOrder { get; set; } = 0;

        // エラー状態
        public FeedErrorState ErrorState { get; set; } = FeedErrorState.None;

        // 最終成功時刻
        public DateTime? LastSuccessTime { get; set; }

        // 最終失敗時刻
        public DateTime? LastFailureTime { get; set; }

        public enum FeedErrorState
        {
            None,
            NotFound404,
            TemporaryFailure,
            LongFailure
        }
    }
}