using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace FeedGem.UIHelpers
{
    public static class TreeViewHeaderFactory
    {
        public static StackPanel Create(string text, bool isFolder, string? url = null)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 2)
            };

            if (isFolder)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "📁",
                    FontSize = 16,
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            else
            {
                var image = new Image
                {
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                try
                {
                    if (!string.IsNullOrEmpty(url))
                    {
                        var uri = new Uri(url);
                        string faviconUrl = $"https://www.google.com/s2/favicons?domain={uri.Host}&sz=16";
                        image.Source = new BitmapImage(new Uri(faviconUrl));
                    }
                }
                catch { }

                panel.Children.Add(image);
            }

            panel.Children.Add(new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center
            });

            return panel;
        }
    }
}