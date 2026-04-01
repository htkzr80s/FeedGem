using FeedGem.Data;
using FeedGem.Services;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;

namespace FeedGem.UIHelpers
{
    public class ContextMenuBuilder(
        FeedRepository repository,
        FeedService feedService,
        FeedUpdateService updateService,
        OpmlService opmlService,
        Func<Task> reloadTree,
        TextBlock log)
    {
        private readonly FeedRepository _repository = repository;
        private readonly FeedService _feedService = feedService;
        private readonly FeedUpdateService _updateService = updateService;
        private readonly OpmlService _opmlService = opmlService;
        private readonly Func<Task> _reloadTree = reloadTree;
        private readonly TextBlock _log = log;

        // ContextMenu生成
        public ContextMenu Build(TreeViewItem? treeViewItem)
        {
            var menu = new ContextMenu();

            // --- 全体メニュー ---
            var refreshItem = new MenuItem { Header = "今すぐ更新" };
            refreshItem.Click += async (s, e) => await RefreshAll();
            menu.Items.Add(refreshItem);

            var addFolderItem = new MenuItem { Header = "フォルダを作成..." };
            addFolderItem.Click += async (s, e) => await AddFolder(treeViewItem);
            menu.Items.Add(addFolderItem);

            // --- OPMLインポート ---
            var importOpmlItem = new MenuItem { Header = "OPMLをインポート..." };
            importOpmlItem.Click += async (s, e) =>
            {
                var dialog = new OpenFileDialog { Filter = "OPMLファイル (*.opml;*.xml)|*.opml;*.xml" };
                if (dialog.ShowDialog() != true) return;

                try
                {
                    int count = await _opmlService.ImportAsync(dialog.FileName);
                    _log.Text = $"{count}件のフィードをインポートしました。";
                    await _reloadTree();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"インポート失敗: {ex.Message}");
                }
            };
            menu.Items.Add(importOpmlItem);

            // --- OPMLエクスポート ---
            var exportOpmlItem = new MenuItem { Header = "OPMLをエクスポート..." };
            exportOpmlItem.Click += async (s, e) =>
            {
                var dialog = new SaveFileDialog { Filter = "OPMLファイル (*.opml)|*.opml", FileName = "feeds.opml" };
                if (dialog.ShowDialog() != true) return;

                try
                {
                    var doc = await _opmlService.ExportAsync();
                    doc.Save(dialog.FileName);
                    _log.Text = "エクスポートが完了しました。";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"エクスポート失敗: {ex.Message}");
                }
            };
            menu.Items.Add(exportOpmlItem);

            if (treeViewItem != null)
            {
                menu.Items.Add(new Separator());

                var renameItem = new MenuItem { Header = "名前を変更..." };
                renameItem.Click += async (s, e) => await Rename(treeViewItem);
                menu.Items.Add(renameItem);

                if (treeViewItem.Tag is string folderPath)
                {
                    var deleteFolderItem = new MenuItem { Header = "フォルダを削除", Foreground = Brushes.Red };
                    deleteFolderItem.Click += async (s, e) =>
                    {
                        // フォルダが空かチェック
                        bool isEmpty = await _repository.IsFolderEmptyAsync(folderPath);

                        if (!isEmpty)
                        {
                            var result = MessageBox.Show(
                                "このフォルダにはフィードが含まれています。すべて削除しますか？",
                                "確認",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning
                            );

                            if (result != MessageBoxResult.Yes)
                                return;
                        }

                        await _feedService.DeleteFolderAsync(folderPath);
                        await _reloadTree();
                    };
                    menu.Items.Add(deleteFolderItem);
                }
                else if (treeViewItem.Tag is long feedId)
                {
                    var deleteFeedItem = new MenuItem { Header = "このフィードを削除", Foreground = Brushes.Red };
                    deleteFeedItem.Click += async (s, e) => await DeleteFeed(feedId);
                    menu.Items.Add(deleteFeedItem);
                }
            }

            return menu;
        }

        // 全更新
        private async Task RefreshAll()
        {
            _log.Text = "記事を更新中...";
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                await _updateService.UpdateAllAsync();
                await _reloadTree();
                _log.Text = "更新が完了しました。";
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        // フォルダ追加
        private async Task AddFolder(TreeViewItem? target)
        {
            string name = Interaction.InputBox("フォルダ名", "作成", "");
            if (string.IsNullOrWhiteSpace(name)) return;

            string path = "/";

            // フォルダ上で右クリックした場合
            if (target?.Tag is string folderPath)
            {
                path = folderPath;
            }

            await _repository.AddFeedAsync(path, name, "folder://" + Guid.NewGuid());
            await _reloadTree();
        }

        // 名前変更
        private async Task Rename(TreeViewItem item)
        {
            if (item.Tag is string folderPath)
            {
                string current = folderPath.TrimStart('/');
                string name = Interaction.InputBox("新しい名前", "変更", current);

                if (string.IsNullOrWhiteSpace(name) || name == current) return;

                await _feedService.RenameFolderAsync(folderPath, name);
                await _reloadTree();
            }
            else if (item.Tag is long feedId)
            {
                var feeds = await _repository.GetAllFeedsAsync();
                var target = feeds.FirstOrDefault(f => f.Id == feedId);
                if (target == null) return;

                string name = Interaction.InputBox("新しい名前", "変更", target.Title);
                if (string.IsNullOrWhiteSpace(name) || name == target.Title) return;

                await _feedService.RenameFeedAsync(feedId, name);
                await _reloadTree();
            }
        }

        // フィード削除
        private async Task DeleteFeed(long id)
        {
            if (MessageBox.Show("削除しますか？", "確認", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            await _feedService.DeleteFeedAsync(id);
            await _reloadTree();
        }
    }
}