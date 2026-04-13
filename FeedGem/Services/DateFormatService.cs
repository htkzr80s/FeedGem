using System.Globalization;

namespace FeedGem.Services
{
    public class DateFormatService
    {
        // アプリ全体で共有する唯一のインスタンス
        private static readonly Lazy<DateFormatService> _instance =
            new(() => new DateFormatService());

        public static DateFormatService Instance => _instance.Value;

        // 現在選択されているフォーマット
        private string _currentFormat = "G";

        public string CurrentFormat
        {
            get => _currentFormat;
            set => _currentFormat = value;
        }

        // 表示用にフォーマットした文字列を返す
        public string FormatDate(DateTime dateTime)
        {
            // CultureInfo.CurrentCulture を使うと、システムの地域設定に少し寄せる
            return dateTime.ToString(CurrentFormat, CultureInfo.CurrentCulture);
        }

        // ログ用（常に英語形式で統一したい場合）
        public static string FormatLogDate(DateTime dateTime)
        {
            return dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
    }
}