using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Media = System.Windows.Media;
using Wpf = System.Windows.Controls;

namespace FeedGem.UIHelpers
{
    public static partial class FeedTreeHeader
    {
        // 未読 "(数字)" 判定用（コンパイル時生成）
        [GeneratedRegex(@"\(\d+\)")]
        private static partial Regex UnreadRegex();

        // ヘッダUI生成
        public static FrameworkElement Create(string text, bool isFolder, string? url = null)
        {
            // ルートパネル（行全体をクリック可能にするためGridに変更）
            var panel = new Grid
            {
                Margin = new Thickness(0, 1, 0, 1),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                Background = Media.Brushes.Transparent
            };

            // カラム定義（アイコン + テキスト）
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // アイコン生成
            if (isFolder)
            {
                // フォルダアイコン（Segoe MDL2 Assets）
                var icon = new TextBlock
                {
                    Text = "\uE8B7",
                    FontFamily = new Media.FontFamily("Segoe MDL2 Assets"),
                    FontSize = 16,
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                icon.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

                Grid.SetColumn(icon, 0);
                panel.Children.Add(icon);
            }
            else
            {
                var image = new Wpf.Image
                {
                    Width = 16,
                    Height = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                    SnapsToDevicePixels = true
                };

                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

                try
                {
                    if (!string.IsNullOrEmpty(url))
                    {
                        var uri = new Uri(url);
                        string faviconUrl = $"https://www.google.com/s2/favicons?domain={uri.Host}&sz=16";
                        image.Source = new BitmapImage(new Uri(faviconUrl));
                    }
                }
                catch
                {
                    // 読み込み失敗時は何もしない
                }

                // アイコン背景（テーマ対応）
                var iconBorder = new Border
                {
                    Width = 18,
                    Height = 18,
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = image
                };

                iconBorder.SetResourceReference(Border.BackgroundProperty, "IconBackgroundBrush");

                Grid.SetColumn(iconBorder, 0);
                panel.Children.Add(iconBorder);
            }

            // テキスト
            var textBlock = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 180
            };

            textBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

            // 未読がある場合は太字
            if (UnreadRegex().IsMatch(text))
            {
                textBlock.FontWeight = FontWeights.Bold;
            }

            Grid.SetColumn(textBlock, 1);
            panel.Children.Add(textBlock);

            return panel;
        }
    }
}