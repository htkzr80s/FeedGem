using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using FeedGem.Models;

namespace FeedGem.Data
{
    public class FeedRepository
    {
        // データベース接続文字列
        private readonly string _connectionString;

        public FeedRepository(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
        }

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
        }

        // 購読中のフィード一覧を取得する
        public async Task<List<FeedInfo>> GetAllFeedsAsync()
        {
            var feeds = new List<FeedInfo>();
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            string query = "SELECT id, folder_path, title, url FROM feeds ORDER BY folder_path, title";
            // データベースから全フィードのURLを取得
            using var command = new SqliteCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            // 各フィードの取得処理を順次実行
            while (await reader.ReadAsync())
            {
                feeds.Add(new FeedInfo
                {
                    Id = reader.GetInt64(0),
                    FolderPath = reader.GetString(1),
                    Title = reader.GetString(2),
                    Url = reader.GetString(3)
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

        // フィード情報を新規登録する
        public async Task AddFeedAsync(string folderPath, string title, string url)
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            string insertQuery = "INSERT OR IGNORE INTO feeds (folder_path, title, url) VALUES (@folderPath, @title, @url)";
            using var command = new SqliteCommand(insertQuery, connection);
            command.Parameters.AddWithValue("@folderPath", folderPath);
            command.Parameters.AddWithValue("@title", title);
            command.Parameters.AddWithValue("@url", url);

            await command.ExecuteNonQueryAsync();
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
    }

    // 内部管理用のシンプルなクラス
    public class FeedInfo
    {
        public long Id { get; set; }
        public string FolderPath { get; set; } = "/";
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
    }
}