using FeedGem.Models;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Documents;

namespace FeedGem.Views
{
    public partial class LicenseWindow : Window
    {
        public LicenseWindow()
        {
            InitializeComponent();
            LoadLicenses();
        }

        private void LoadLicenses()
        {
            // 実行中のアセンブリを取得
            var assembly = Assembly.GetExecutingAssembly();

            string resourceName = "FeedGem.Resources.licenses.json";

            try
            {
                // リソースをストリームとして開く
                using Stream? stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) return;

                // ストリームリーダーで中身を読み出す
                using StreamReader reader = new(stream, Encoding.UTF8);
                string jsonString = reader.ReadToEnd();

                // JSONをデシリアライズしてListBoxに流し込む
                var licenses = JsonSerializer.Deserialize<List<LicenseInfo>>(jsonString);
                LicenseListBox.ItemsSource = licenses;
            }
            catch
            {
                // 読み込み失敗時の処理
            }
        }

        // リンクをクリックした時の共通処理
        private void UrlLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Hyperlink link && link.Tag is string url)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => this.Close();
    }
}