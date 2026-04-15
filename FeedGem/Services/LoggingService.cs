using System.Diagnostics;
using System.IO;

namespace FeedGem.Services
{
    public static class LoggingService
    {
        // 同時書き込み防止用ロック
        private static readonly Lock _lock = new();

        // ログディレクトリのパスを定義
        private static readonly string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

        // ログファイルのフルパスを定義
        private static readonly string LogFilePath = Path.Combine(LogDirectory, "log.txt");

        // 情報ログ
        public static void Info(string message)
        {
            Write("INFO", message);
        }

        // エラーログ
        public static void Error(string message, Exception ex)
        {
            Write("ERROR", $"{message} - {ex.Message}");
        }

        // 実際の書き込み処理
        private static void Write(string level, string message)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";

            Debug.WriteLine(line);

            try
            {
                // ログディレクトリが存在しない場合は作成
                if (!Directory.Exists(LogDirectory)) Directory.CreateDirectory(LogDirectory);

                // ロックをかけてファイルに追記
                lock (_lock)
                {
                    File.AppendAllText(LogFilePath, line + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LOG ERROR] {ex.Message}");
            }
        }
    }
}