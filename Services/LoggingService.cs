using System;
using System.Diagnostics;
using System.IO;

namespace FeedGem.Services
{
    public static class LoggingService
    {
        private static readonly string LogFilePath = "log.txt";

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
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
            catch
            {
                // ファイル書き込み失敗は無視
            }
        }
    }
}