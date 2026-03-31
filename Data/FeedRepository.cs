using FeedGem.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

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

            string createTableQuery = @"
                CREATE TABLE IF NOT EXISTS feeds (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    folder_path TEXT NOT NULL,
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
                );";

            using var command = new SqliteCommand(createTableQuery, connection);
            command.ExecuteNonQuery();

            // 既存テーブルへのカラム追加（既に存在する場合はエラーになるため無視する）
            try
            {
                using var alterCommand = new SqliteCommand("ALTER TABLE feeds ADD COLUMN sort_order INTEGER DEFAULT 0;", connection);
                alterCommand.ExecuteNonQuery();
            }
            catch { /* 無視 */ }
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
                    SortOrder = reader.GetInt32(4) // sort_orderを読み込む
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
            string query = "SELECT title, published_date, url, summary, is_read FROM entries WHERE feed_id = @feedId ORDER BY published_date DESC";
            using var command = new SqliteCommand(query, connection);
            command.Parameters.AddWithValue("@feedId", feedId);

            using var reader = await command.ExecuteReaderAsync();
            // 取得したレコードをコレクションに追加
            while (await reader.ReadAsync())
            {
                articles.Add(new ArticleItem
                {
                    Title = reader.GetString(0),
                    Date = reader.GetString(1),
                    Url = reader.GetString(2),
                    Summary = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    IsRead = reader.GetInt32(4) == 1
                });
            }
            return articles;
        }

        // フィード情報・フォルダを登録する
        public async Task AddFeedAsync(string folderPath, string title, string url)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // 重複チェックを厳密に行うためのクエリ
            string insertQuery = "INSERT INTO feeds (folder_path, title, url) VALUES (@folderPath, @title, @url)";
            using var command = new SqliteCommand(insertQuery, connection);

            command.Parameters.AddWithValue("@folderPath", folderPath); // フォルダなら通常は "/"
            command.Parameters.AddWithValue("@title", title);          // フォルダ名
            command.Parameters.AddWithValue("@url", url);            // 識別用の unique な文字列

            try
            {
                await command.ExecuteNonQueryAsync();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // 19 は Constraint 違反（重複など）
            {
                // すでに存在する場合は何もしない、あるいはログを出す
                Debug.WriteLine("既に存在するURLのためスキップされました。");
            }
        }

        // 記事データを保存する
        public async Task SaveEntryAsync(long feedId, string title, string url, string summary, string pubDate)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            string insertQuery = @"INSERT OR IGNORE INTO entries (feed_id, title, url, summary, published_date)
                                 VALUES (@feedId, @title, @url, @summary, @pubDate)";
            using var command = new SqliteCommand(insertQuery, connection);
            command.Parameters.AddWithValue("@feedId", feedId);
            command.Parameters.AddWithValue("@title", title);
            command.Parameters.AddWithValue("@url", url);
            command.Parameters.AddWithValue("@summary", summary);
            command.Parameters.AddWithValue("@pubDate", pubDate);

            await command.ExecuteNonQueryAsync();
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

        // 指定したフィードの未読記事数を取得する
        public async Task<int> GetUnreadCountAsync(long feedId)
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
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // 明示的に SqliteTransaction にキャストして型不一致を防ぐ
            using var transaction = (SqliteTransaction)connection.BeginTransaction();

            try
            {
                // 1. 関連する記事データを削除
                string deleteEntries = "DELETE FROM entries WHERE feed_id IN (SELECT id FROM feeds WHERE folder_path = @path)";
                using var cmd1 = new SqliteCommand(deleteEntries, connection, transaction);
                cmd1.Parameters.AddWithValue("@path", folderPath);
                await cmd1.ExecuteNonQueryAsync();

                // 2. フィードデータ（管理用ダミー含む）を削除
                string deleteFeeds = "DELETE FROM feeds WHERE folder_path = @path";
                using var cmd2 = new SqliteCommand(deleteFeeds, connection, transaction);
                cmd2.Parameters.AddWithValue("@path", folderPath);
                await cmd2.ExecuteNonQueryAsync();

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        // 各フィードごとに最新20件を除いた古い記事を一括削除する
        public async Task DeleteOldEntriesAsync()
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // ROW_NUMBERを使って、各feed_idごとに新しい順に番号を振り、21番目以降を削除対象にする
            string deleteQuery = @"
                DELETE FROM entries 
                WHERE id IN (
                    SELECT id FROM (
                        SELECT id, ROW_NUMBER() OVER (PARTITION BY feed_id ORDER BY published_date DESC, id DESC) as row_num
                        FROM entries
                    ) WHERE row_num > 20
                )";

            using var command = new SqliteCommand(deleteQuery, connection);
            await command.ExecuteNonQueryAsync();
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
    }
}