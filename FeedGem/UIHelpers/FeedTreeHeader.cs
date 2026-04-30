using FeedGem.Models;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FeedGem.UIHelpers
{
    public static partial class FeedTreeHeader
    {
        // 未読 "(数字)" 判定用（コンパイル時生成）
        [GeneratedRegex(@"\(\d+\)")]
        private static partial Regex UnreadRegex();

        // ヘッダUI生成
        public static FrameworkElement Create(
            string text,
            bool isFolder,
            string? url = null,
            FeedInfo.FeedErrorState errorState = FeedInfo.FeedErrorState.None)
        {
            // ルートパネル（行全体をクリック可能にするためGridに変更）
            var panel = new Grid
            {
                Margin = new Thickness(0, 1, 0, 1),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = Brushes.Transparent
            };

            // カラム定義（ファビコン列 + テキスト＋警告アイコンをまとめた列）
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // アイコン生成
            if (isFolder)
            {
                // フォルダアイコン（Segoe MDL2 Assets）
                var icon = new TextBlock
                {
                    Text = "\uE8B7",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
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
                var image = new Image
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

            // テキストと警告アイコンをまとめるパネル（幅が広がっても警告アイコンはテキスト直後に固定）
            var textPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            // テキスト（エラー時はグレーアウト）
            var textBlock = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            // エラー状態に応じてテキスト色を変える
            if (errorState == FeedInfo.FeedErrorState.None)
            {
                textBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            }
            else
            {
                textBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            }

            // 未読がある場合は太字
            if (UnreadRegex().IsMatch(text))
            {
                textBlock.FontWeight = FontWeights.Bold;
            }

            textPanel.Children.Add(textBlock);

            // 警告アイコン（エラー時のみ）
            if (errorState != FeedInfo.FeedErrorState.None)
            {
                string tooltip = errorState switch
                {
                    FeedInfo.FeedErrorState.NotFound404 => "404: フィードが見つかりません（更新をスキップ中）",
                    FeedInfo.FeedErrorState.LongFailure => "24時間以上更新に失敗しています",
                    _ => "一時的な更新エラーが発生しています"
                };

                var warningIcon = new TextBlock
                {
                    Text = "\uE7BA",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 13,
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = errorState == FeedInfo.FeedErrorState.NotFound404
                        ? new SolidColorBrush(Color.FromRgb(0xFF, 0x5F, 0x5F))
                        : new SolidColorBrush(Color.FromRgb(0xFF, 0xC0, 0x00)),
                    ToolTip = tooltip
                };

                textPanel.Children.Add(warningIcon);
            }

            Grid.SetColumn(textPanel, 1);
            panel.Children.Add(textPanel);

            return panel;
        }
    }
}