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
                        folder_path TEXT NOT NULL DEFAULT '/',
                        title TEXT NOT NULL,
                        url TEXT UNIQUE NOT NULL,
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
                        feed_id INTEGER,
                        title TEXT NOT NULL,
                        url TEXT UNIQUE NOT NULL,
                        summary TEXT,
                        published_date TEXT,
                        is_read INTEGER DEFAULT 0,
                        FOREIGN KEY(feed_id) REFERENCES feeds(id)
                    );
                    """, connection, transaction))
                {
                    cmd.ExecuteNonQuery();
                }

                // --- 3. インデックスの作成 ---
                using (var cmd = new SqliteCommand("""
                    CREATE INDEX IF NOT EXISTS idx_entries_feedid ON entries(feed_id);
                    """, connection, transaction))
                {
                    cmd.ExecuteNonQuery();
                }

                // --- 4 フォルダ名のユニーク制約（フォルダのみ対象） ---
                using (var cmd = new SqliteCommand("""
                    CREATE UNIQUE INDEX IF NOT EXISTS idx_folder_unique
                    ON feeds(title)
                    WHERE url LIKE 'folder://%';
                    """, connection, transaction))
                {
                    cmd.ExecuteNonQuery();
                }

                // --- 5. カラム追加のアップデート処理 ---
                AddColumnIfMissing(connection, transaction, "feeds", "sort_order", "INTEGER DEFAULT 0");
                AddColumnIfMissing(connection, transaction, "feeds", "folder_path", "TEXT NOT NULL DEFAULT '/'");

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

        // カラム追加用の補助メソッド（例外を出さない安全版）
        private static void AddColumnIfMissing(SqliteConnection conn, SqliteTransaction trans, string tableName, string columnName, string definition)
        {
            // テーブルの情報を取得して、指定した列名が既に存在するか確認する
            bool columnExists = false;
            using (var checkCmd = new SqliteCommand($"PRAGMA table_info({tableName});", conn, trans))
            {
                using var reader = checkCmd.ExecuteReader();
                while (reader.Read())
                {
                    // 1番目の列（name）にカラム名が入っている
                    if (reader.GetString(1).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        columnExists = true;
                        break;
                    }
                }
            }

            // 列が存在しない場合のみ、追加コマンドを実行する
            if (!columnExists)
            {
                using var command = new SqliteCommand($"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};", conn, trans);
                command.ExecuteNonQuery();
            }
        }

        // 購読中のフィード一覧を取得する
        public async Task<List<FeedInfo>> GetAllFeedsAsync()
        {
            var feeds = new List<FeedInfo>();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // sort_orderを追加し、ソート条件に含める
            // error_state / last_success_time / last_failure_time を追加取得する
            string query = """
                SELECT id, folder_path, title, url, sort_order,
                       error_state, last_success_time, last_failure_time
                FROM feeds
                ORDER BY folder_path, sort_order, title
                """;

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
                    SortOrder = reader.GetInt32(4),

                    // エラー状態を読み込む（カラムがNULLの場合は None=0 にフォールバック）
                    ErrorState = reader.IsDBNull(5) ? FeedInfo.FeedErrorState.None : (FeedInfo.FeedErrorState)reader.GetInt32(5),
                    LastSuccessTime = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    LastFailureTime = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
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
                // 文字列として保存された日付を、プログラムで扱える DateTime 型に戻す
                DateTime date = reader.IsDBNull(1)
                    ? DateTime.MinValue
                    : DateTime.Parse(reader.GetString(1));

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

        public async Task<List<ArticleItem>> GetEntriesByFolderAsync(long folderId)
        {
            var articles = new List<ArticleItem>();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // 1. まずフォルダのフルパスを特定する
            string getPathQuery = "SELECT folder_path, title FROM feeds WHERE id = @id;";
            string targetPath = "";
            using (var cmdPath = new SqliteCommand(getPathQuery, connection))
            {
                cmdPath.Parameters.AddWithValue("@id", folderId);
                using var reader = await cmdPath.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    string parentPath = reader.GetString(0);
                    string folderName = reader.GetString(1);
                    // ルート直下なら /名前、そうでなければ パス/名前
                    targetPath = parentPath == "/" ? $"/{folderName}" : $"{parentPath}/{folderName}";
                }
            }

            if (string.IsNullOrEmpty(targetPath)) return articles;

            // 2. そのパス、または配下のパスに属する全記事を取得
            string childPattern = targetPath + "/%";
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
                WHERE f.folder_path = @targetPath 
                   OR f.folder_path LIKE @childPattern
                ORDER BY e.published_date DESC
                """;

            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@targetPath", targetPath);
            command.Parameters.AddWithValue("@childPattern", childPattern);

            using var entryReader = await command.ExecuteReaderAsync();

            while (await entryReader.ReadAsync())
            {
                DateTime date = entryReader.IsDBNull(1)
                    ? DateTime.MinValue
                    : DateTime.Parse(entryReader.GetString(1));

                articles.Add(new ArticleItem
                {
                    Title = entryReader.GetString(0),
                    Date = date,
                    Url = entryReader.GetString(2),
                    Summary = entryReader.IsDBNull(3) ? "" : entryReader.GetString(3),
                    IsRead = entryReader.GetInt32(4) == 1,
                    FeedTitle = entryReader.GetString(5)
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
                return ((long)existing, false);
            }

            // 全データの中での最大 sort_order を取得する
            string maxOrderQuery = "SELECT IFNULL(MAX(sort_order), -1) FROM feeds;";
            using var maxOrderCommand = new SqliteCommand(maxOrderQuery, connection);

            // 全体の最大値に 1 を加算して、絶対的な最後尾番号を決定する
            int nextOrder = Convert.ToInt32(await maxOrderCommand.ExecuteScalarAsync()) + 1;

            // --- 新規登録 ---
            string insertQuery = """
                INSERT INTO feeds (folder_path, title, url, sort_order)
                VALUES (@folder, @title, @url, @nextOrder);
                SELECT last_insert_rowid();
                """;

            using var insertCommand = new SqliteCommand(insertQuery, connection);
            insertCommand.Parameters.AddWithValue("@folder", folder);
            insertCommand.Parameters.AddWithValue("@title", title);
            insertCommand.Parameters.AddWithValue("@url", url);
            insertCommand.Parameters.AddWithValue("@nextOrder", nextOrder);

            var result = await insertCommand.ExecuteScalarAsync();

            return ((long)(result ?? 0), true);
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

        // 指定したフォルダ配下のすべての記事を既読にする
        public async Task MarkFolderEntriesAsReadAsync(long folderId)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // 1. まずフォルダ自身の情報を取得して「配下を特定するためのパス」を作る
            string getPathQuery = "SELECT folder_path, title FROM feeds WHERE id = @id;";
            string fullPath = "";

            using (var cmd = new SqliteCommand(getPathQuery, connection))
            {
                cmd.Parameters.AddWithValue("@id", folderId);
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var parent = reader.GetString(0);
                    var title = reader.GetString(1);
                    fullPath = parent.EndsWith('/') ? parent + title : parent + "/" + title;
                }
            }

            if (string.IsNullOrEmpty(fullPath)) return;

            // 2. 該当する folder_path を持つフィードに属する記事を一括更新
            string updateQuery = """
                UPDATE entries 
                SET is_read = 1 
                WHERE feed_id IN (
                    SELECT id FROM feeds WHERE folder_path = @path
                );
                """;

            using (var cmd = new SqliteCommand(updateQuery, connection))
            {
                cmd.Parameters.AddWithValue("@path", fullPath);
                await cmd.ExecuteNonQueryAsync();
            }
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

            // データの読み取りを開始
            using var reader = await command.ExecuteReaderAsync();

            // 取得したレコードを一つずつ辞書に追加
            while (await reader.ReadAsync())
            {
                // 0列目がfeed_id、1列目がカウント数[cite: 1]
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
            // titleカラムのみを対象にする
            string query = "UPDATE feeds SET title = @title WHERE id = @id;";
            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@title", newTitle);
            command.Parameters.AddWithValue("@id", feedId);
            await command.ExecuteNonQueryAsync();
        }

        // フィードまたはフォルダの配置（所属パスと並び順）を更新する
        public async Task UpdateFeedLayoutAsync(long feedId, string newPath, int newOrder)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // 移動先のパスと新しい並び順を同時に更新する
            string query = "UPDATE feeds SET folder_path = @path, sort_order = @order WHERE id = @id;";

            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@path", newPath);
            command.Parameters.AddWithValue("@order", newOrder);
            command.Parameters.AddWithValue("@id", feedId);

            await command.ExecuteNonQueryAsync();
        }

        // フォルダ内の全項目に対して、かぶらない連番を再割り当てする一括更新メソッド
        public async Task ReorderFolderItemsAsync(IEnumerable<(long Id, int Order)> items)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // 複数の更新を一つの単位として扱い、整合性を保証する
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                foreach (var (id, order) in items)
                {
                    // IDをキーに、新しい重ならない連番(sort_order)をセットする
                    string query = "UPDATE feeds SET sort_order = @order WHERE id = @id;";
                    using var command = new SqliteCommand(query, connection);
                    command.Parameters.AddWithValue("@order", order);
                    command.Parameters.AddWithValue("@id", id);
                    command.Transaction = (SqliteTransaction)transaction;

                    await command.ExecuteNonQueryAsync();
                }

                // すべての更新が成功した場合のみ、データベースに反映する
                await transaction.CommitAsync();
            }
            catch (Exception)
            {
                // 途中でエラーが起きた場合は、すべての変更を破棄して以前の状態を守る
                await transaction.RollbackAsync();
                throw;
            }
        }

        // フィードを削除する（関連する記事も一緒に消す）
        public async Task DeleteFeedAsync(long feedId)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // トランザクションを開始し、nullの場合は例外をスローする
            using var transaction = await connection.BeginTransactionAsync() as SqliteTransaction
                ?? throw new InvalidOperationException("Failed to begin transaction.");

            try
            {
                string deleteEntries = "DELETE FROM entries WHERE feed_id = @id";
                string deleteFeed = "DELETE FROM feeds WHERE id = @id";

                using var cmd1 = new SqliteCommand(deleteEntries, connection, transaction);
                cmd1.Parameters.AddWithValue("@id", feedId);
                await cmd1.ExecuteNonQueryAsync();

                using var cmd2 = new SqliteCommand(deleteFeed, connection, transaction);
                cmd2.Parameters.AddWithValue("@id", feedId);
                await cmd2.ExecuteNonQueryAsync();

                // 確定処理も非同期で行う
                await transaction!.CommitAsync();
            }
            catch
            {
                // 失敗時の取り消し処理
                await transaction!.RollbackAsync();
                throw;
            }
        }

        // 指定したフォルダIDの配下に、実体のあるフィードが存在するか確認する
        public async Task<bool> IsFolderEmptyAsync(long folderId)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // 1. 自身の ID から、削除対象となるフォルダのフルパスを特定する
            string getPathQuery = "SELECT folder_path, title FROM feeds WHERE id = @id;";
            string targetFullPath = "";

            using (var cmdPath = new SqliteCommand(getPathQuery, connection))
            {
                cmdPath.Parameters.AddWithValue("@id", folderId);
                using var reader = await cmdPath.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    string parentPath = reader.GetString(0);
                    string folderName = reader.GetString(1);
                    targetFullPath = parentPath == "/" ? $"/{folderName}" : $"{parentPath}/{folderName}";
                }
            }

            if (string.IsNullOrEmpty(targetFullPath)) return true;

            // 2. 特定したパス、またはその配下にある実体フィード（folder:// 以外）を数える
            string childPattern = targetFullPath + "/%";
            string countQuery = """
                SELECT COUNT(*) FROM feeds 
                WHERE (folder_path = @path OR folder_path LIKE @childPattern)
                  AND url NOT LIKE 'folder://%'
                """;

            using var cmdCount = new SqliteCommand(countQuery, connection);
            cmdCount.Parameters.AddWithValue("@path", targetFullPath);
            cmdCount.Parameters.AddWithValue("@childPattern", childPattern);

            var count = (long)(await cmdCount.ExecuteScalarAsync() ?? 0L);
            return count == 0;
        }

        // フォルダとその配下の全データを削除する
        public async Task DeleteFolderAsync(long folderId)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // トランザクションを非同期で開始
            using var transaction = await connection.BeginTransactionAsync() as SqliteTransaction
                ?? throw new InvalidOperationException("Failed to begin transaction.");

            try
            {
                // 1. 削除対象となるフォルダの情報を取得する
                string getPathQuery = "SELECT folder_path, title FROM feeds WHERE id = @id;";
                string targetPath = "";
                using (var cmdPath = new SqliteCommand(getPathQuery, connection, transaction))
                {
                    cmdPath.Parameters.AddWithValue("@id", folderId);
                    using var reader = await cmdPath.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        // 親のパスと自身の名前を組み合わせてフルパスを構築する
                        string parentPath = reader.GetString(0);
                        string folderName = reader.GetString(1);
                        targetPath = parentPath == "/" ? $"/{folderName}" : $"{parentPath}/{folderName}";
                    }
                }

                // 対象が見つからない場合は何もしない
                if (string.IsNullOrEmpty(targetPath)) return;

                // 子階層を特定するためのパターンを作成する（SQLのLIKE用）
                string childPattern = targetPath + "/%";

                // 2. 配下のフィードに紐付く記事(entries)を先に削除する
                string deleteEntries = """
                    DELETE FROM entries
                    WHERE feed_id IN (
                        SELECT id FROM feeds
                        WHERE folder_path = @targetPath
                           OR folder_path LIKE @childPattern
                    );
                    """;

                using (var cmd1 = new SqliteCommand(deleteEntries, connection, transaction))
                {
                    cmd1.Parameters.AddWithValue("@targetPath", targetPath);
                    cmd1.Parameters.AddWithValue("@childPattern", childPattern);
                    await cmd1.ExecuteNonQueryAsync();
                }

                // 3. フォルダ自身、配下のフィード、および配下のフォルダを削除する
                string deleteFeeds = """
                    DELETE FROM feeds
                    WHERE id = @id
                       OR folder_path = @targetPath
                       OR folder_path LIKE @childPattern;
                    """;

                using (var cmd2 = new SqliteCommand(deleteFeeds, connection, transaction))
                {
                    cmd2.Parameters.AddWithValue("@id", folderId);
                    cmd2.Parameters.AddWithValue("@targetPath", targetPath);
                    cmd2.Parameters.AddWithValue("@childPattern", childPattern);
                    await cmd2.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                // 失敗した場合はロールバックして例外を再送出する
                await transaction.RollbackAsync();
                throw new InvalidOperationException($"Failed to delete folder (ID: {folderId}).", ex);
            }
        }

        // フォルダ名を変更する（配下すべて含む）
        public async Task RenameFolderAsync(long folderId, string newName)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // 抽象型として開始
            using var transaction = await connection.BeginTransactionAsync();

            // SqliteCommandに渡すために、SqliteTransactionとして扱えるか確認しながらキャストする
            if (transaction is not SqliteTransaction sqliteTrans)
            {
                throw new InvalidOperationException("Failed to start SQLite transaction.");
            }

            try
            {
                // --- 1. データベースから現在の情報を取得 ---
                string oldName = "";
                string parentPath = "";

                string selectQuery = "SELECT title, folder_path FROM feeds WHERE id = @id;";
                using (var cmd = new SqliteCommand(selectQuery, connection, sqliteTrans))
                {
                    cmd.Parameters.AddWithValue("@id", folderId);
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        oldName = reader.GetString(0);
                        parentPath = reader.GetString(1);
                    }
                }

                if (string.IsNullOrEmpty(oldName)) return;

                // 2. 現在のフルパスと、新しいフルパスを組み立てる
                string oldFullPath = parentPath.EndsWith('/') ? parentPath + oldName : parentPath + "/" + oldName;
                string newFullPath = parentPath.EndsWith('/') ? parentPath + newName : parentPath + "/" + newName;

                // 3. フォルダの中身（配下のフィードやサブフォルダ）のパスを一括更新
                string updateChildren = "UPDATE feeds SET folder_path = @newPath WHERE folder_path = @oldPath;";
                using (var cmd = new SqliteCommand(updateChildren, connection, sqliteTrans))
                {
                    cmd.Parameters.AddWithValue("@oldPath", oldFullPath);
                    cmd.Parameters.AddWithValue("@newPath", newFullPath);
                    await cmd.ExecuteNonQueryAsync();
                }

                // --- 4. フォルダ自身の名前を更新 ---
                string updateFolder = "UPDATE feeds SET title = @newName WHERE id = @id;";
                using (var cmd = new SqliteCommand(updateFolder, connection, sqliteTrans))
                {
                    cmd.Parameters.AddWithValue("@newName", newName);
                    cmd.Parameters.AddWithValue("@id", folderId);
                    await cmd.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await sqliteTrans.RollbackAsync();
                throw;
            }
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
                        SELECT id, ROW_NUMBER() OVER (PARTITION BY feed_id ORDER BY published_date DESC) as rn 
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
            // 接続を生成し、usingスコープを抜けるときに自動で閉じる
            using var connection = new SqliteConnection(_connectionString);
            // 接続を非同期で開始する
            await connection.OpenAsync();

            // 必要な列だけを更新するSQLコマンドを作成
            var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE feeds 
                SET 
                    error_state = $ErrorState, 
                    last_success_time = $LastSuccessTime, 
                    last_failure_time = $LastFailureTime
                WHERE Id = $Id
                """;

            // パラメータを個別に追加。Valueは後で代入することで可読性を確保する
            command.Parameters.Add("$ErrorState", SqliteType.Integer);
            command.Parameters.Add("$LastSuccessTime", SqliteType.Text);
            command.Parameters.Add("$LastFailureTime", SqliteType.Text);
            command.Parameters.Add("$Id", SqliteType.Integer);

            // 各パラメータに値を代入。nullの場合はDBNull.Valueをセットする
            command.Parameters["$ErrorState"].Value = (int)feed.ErrorState;
            command.Parameters["$LastSuccessTime"].Value = (object?)feed.LastSuccessTime ?? DBNull.Value;
            command.Parameters["$LastFailureTime"].Value = (object?)feed.LastFailureTime ?? DBNull.Value;
            command.Parameters["$Id"].Value = feed.Id;

            // 更新処理を非同期で実行する
            await command.ExecuteNonQueryAsync();
        }

        // エラー状態のあるフィードが1件以上存在するか確認する
        public async Task<bool> HasAnyFeedErrorAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();

            // COUNT(*)の代わりにLIMIT 1を使用し、1件でも見つかれば即座に結果を返す
            command.CommandText = """
                SELECT 1 FROM feeds
                WHERE error_state != 0
                  AND url NOT LIKE 'folder://%'
                LIMIT 1
                """;

            var result = await command.ExecuteScalarAsync();

            // 取得結果がnullでなければエラーが存在すると判定する
            return result != null;
        }

        // フォルダ名をユニークにする
        public async Task<string> GetUniqueFolderNameAsync(string baseName)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            string name = baseName;
            int counter = 1;

            while (true)
            {
                string query = """
                    SELECT COUNT(*)
                    FROM feeds
                    WHERE folder_path = '/'
                      AND title = @name
                      AND url LIKE 'folder://%';
                    """;

                using var cmd = new SqliteCommand(query, connection);
                cmd.Parameters.AddWithValue("@name", name);

                var count = (long)(await cmd.ExecuteScalarAsync() ?? 0);
                if (count == 0)
                    return name;

                name = $"{baseName} ({counter})";
                counter++;
            }
        }

        // 同名フォルダ存在チェック
        public async Task<bool> FolderExistsAsync(string name)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // 存在確認のみのためLIMIT 1で検索を打ち切る
            string query = """
                SELECT 1
                FROM feeds
                WHERE folder_path = '/'
                  AND title = @name
                  AND url LIKE 'folder://%'
                LIMIT 1;
                """;

            using var cmd = new SqliteCommand(query, connection);
            cmd.Parameters.AddWithValue("@name", name);

            var result = await cmd.ExecuteScalarAsync();
            return result != null;
        }
    }
}