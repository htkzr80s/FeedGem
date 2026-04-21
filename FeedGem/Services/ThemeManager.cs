using Microsoft.Win32;
using System.Windows;

namespace FeedGem.Services
{
    public static class ThemeManager
    {
        // テーマ変更時にWebView2などに通知するためのイベント
        public static event Action<string>? ThemeChanged;

        public static void ApplyTheme(string theme)
        {
            var appResources = Application.Current.Resources.MergedDictionaries;
            appResources.Clear();

            // Auto の場合はシステムテーマを使用
            string themeName = theme == "Auto" ? GetSystemTheme() : theme;

            string uriString = themeName switch
            {
                "Dark" => "pack://application:,,,/Themes/DarkTheme.xaml",
                "Light" => "pack://application:,,,/Themes/LightTheme.xaml",
                _ => "pack://application:,,,/Themes/LightTheme.xaml"
            };

            var dict = new ResourceDictionary
            {
                Source = new Uri(uriString, UriKind.Absolute)
            };

            appResources.Add(dict);

            // イベントを発火してMainWindowへ通知
            ThemeChanged?.Invoke(themeName);
        }

        // Windowsのテーマ設定（ダークモードか否か）をレジストリから取得
        public static string GetSystemTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("AppsUseLightTheme") is int lightTheme && lightTheme == 0)
                {
                    return "Dark";
                }
            }
            catch
            {
                // レジストリ読み取り失敗時はLightを返す
            }
            return "Light";
        }
    }
}