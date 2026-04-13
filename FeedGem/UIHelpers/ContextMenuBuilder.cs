using FeedGem.Data;
using FeedGem.Models;
using FeedGem.Services;
using FeedGem.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Input = System.Windows.Input;
using MsgBox = System.Windows.MessageBox;
using Wpf = System.Windows.Controls;

namespace FeedGem.UIHelpers
{
    public class ContextMenuBuilder(
        FeedRepository repository,
        FeedService feedService,
        FeedUpdateService updateService,
        Func<Task> reloadTree,
        TextBlock log,
        Action updateTime,
        Func<Task> importOpml,
        Func<Task> exportOpml,
        Func<Task> refreshCurrentListView)
    {
        private readonly FeedRepository _repository = repository;
        private readonly FeedService _feedService = feedService;
        private readonly FeedUpdateService _updateService = updateService;
        private readonly Func<Task> _reloadTree = reloadTree;
        private readonly TextBlock _log = log;
        private readonly Action _updateTime = updateTime;
        private readonly Func<Task> _importOpml = importOpml;
        private readonly Func<Task> _exportOpml = exportOpml;
        private readonly Func<Task> _refreshCurrentListView = refreshCurrentListView;

        // 右クリックメニューを構築する
        public Wpf.ContextMenu Build(TreeViewItem? treeViewItem)
        {
            var menu = new Wpf.ContextMenu();

            // 共通メニュー
            var refreshItem = new Wpf.MenuItem { Header = "今すぐ更新" };
            refreshItem.Click += async (s, e) => await RefreshAll();
            menu.Items.Add(refreshItem);

            var addFolderItem = new Wpf.MenuItem { Header = "フォルダを作成..." };
            addFolderItem.Click += async (s, e) => await AddFolder(treeViewItem);
            menu.Items.Add(addFolderItem);

            menu.Items.Add(new Separator());

            // OPML
            var importOpmlItem = new Wpf.MenuItem { Header = "OPMLをインポート..." };
            importOpmlItem.Click += async (s, e) => await _importOpml();
            menu.Items.Add(importOpmlItem);

            var exportOpmlItem = new Wpf.MenuItem { Header = "OPMLをエクスポート..." };
            exportOpmlItem.Click += async (s, e) => await _exportOpml();
            menu.Items.Add(exportOpmlItem);

            // ノード固有メニュー
            if (treeViewItem?.Tag is TreeTag tag)
            {
                menu.Items.Add(new Separator());

                if (tag.Type == TreeNodeType.Feed && tag.FeedId != null)
                {
                    AddFeedSpecificItems(menu, tag.FeedId.Value, treeViewItem);
                }
                else if (tag.Type == TreeNodeType.Folder && tag.FolderPath != null)
                {
                    AddFolderSpecificItems(menu, tag.FolderPath, treeViewItem);
                }
            }
            return menu;
        }

        // フィード固有メニュー
        private void AddFeedSpecificItems(Wpf.ContextMenu menu, long feedId, TreeViewItem treeViewItem)
        {
            var markAllReadItem = new Wpf.MenuItem { Header = "すべて既読にする" };
            markAllReadItem.Click += async (s, e) =>
            {
                await _feedService.MarkAllAsReadAsync(feedId);
                await _reloadTree();
                await _refreshCurrentListView();
            };
            menu.Items.Add(markAllReadItem);

            var renameItem = new Wpf.MenuItem { Header = "名前を変更..." };
            renameItem.Click += async (s, e) => await Rename(treeViewItem);
            menu.Items.Add(renameItem);

            var deleteFeedItem = new Wpf.MenuItem
            {
                Header = "このフィードを削除",
                Tag = "Danger"
            };
            deleteFeedItem.Click += async (s, e) => await DeleteFeed(feedId);
            menu.Items.Add(deleteFeedItem);
        }

        // フォルダ固有メニュー（名前変更を追加済み）
        private void AddFolderSpecificItems(Wpf.ContextMenu menu, string folderPath, TreeViewItem treeViewItem)
        {
            // すべて既読にする
            var markFolderReadItem = new Wpf.MenuItem { Header = "すべて既読にする" };
            markFolderReadItem.Click += async (s, e) =>
            {
                await _feedService.MarkFolderAsReadAsync(folderPath);
                await _reloadTree();
                await _refreshCurrentListView();
            };
            menu.Items.Add(markFolderReadItem);

            // 名前を変更...
            var renameItem = new Wpf.MenuItem { Header = "名前を変更..." };
            renameItem.Click += async (s, e) => await Rename(treeViewItem);
            menu.Items.Add(renameItem);

            // フォルダを削除
            var deleteFolderItem = new Wpf.MenuItem
            {
                Header = "フォルダを削除",
                Tag = "Danger"
            };
            deleteFolderItem.Click += async (s, e) =>
            {
                var allFeeds = await _repository.GetAllFeedsAsync();
                bool hasContents = allFeeds.Any(f =>
                    !f.Url.StartsWith("folder://") &&
                    (f.FolderPath == folderPath || f.FolderPath.StartsWith(folderPath + "/")));

                if (hasContents)
                {
                    var result = MsgBox.Show(
                        "フォルダ内のフィードと記事もすべて削除されます。続行しますか？",
                        "確認",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result != MessageBoxResult.Yes)
                        return;
                }

                if (System.Windows.Application.Current.MainWindow is MainWindow main)
                {
                    main.ClearAllPanels();
                }

                await _feedService.DeleteFolderAsync(folderPath);
                await _reloadTree();
                await _refreshCurrentListView();
            };
            menu.Items.Add(deleteFolderItem);
        }

        // 全更新
        private async Task RefreshAll()
        {
            _log.Text = "記事を更新中...";
            Mouse.OverrideCursor = Input.Cursors.Wait;

            try
            {
                await _updateService.UpdateAllAsync();
                await _reloadTree();
                _updateTime();
                await _refreshCurrentListView();
                _log.Text = "更新が完了しました。";
            }
            catch (Exception ex)
            {
                LoggingService.Error("全体更新失敗", ex);
                _log.Text = "更新中にエラーが発生しました。";
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        // フォルダ追加
        private async Task AddFolder(TreeViewItem? target)
        {
            var dialog = new InputDialog("フォルダ名");
            if (System.Windows.Application.Current?.MainWindow != null)
                dialog.Owner = System.Windows.Application.Current.MainWindow;

            if (dialog.ShowDialog() != true) return;

            string name = dialog.InputText;
            if (string.IsNullOrWhiteSpace(name)) return;

            string path = "/";
            if (target?.Tag is TreeTag tag && tag.Type == TreeNodeType.Folder && tag.FolderPath != null)
            {
                path = tag.FolderPath;
            }

            await _repository.AddFeedAsync(path, name, "folder://" + Guid.NewGuid());
            await _reloadTree();
        }

        // 名前変更（フォルダ・フィード両対応）
        private async Task Rename(TreeViewItem item)
        {
            if (item.Tag is not TreeTag tag) return;

            if (tag.Type == TreeNodeType.Folder && tag.FolderPath != null)
            {
                string folderPath = tag.FolderPath;
                string current = folderPath.TrimStart('/');

                var dialog = new InputDialog("新しい名前", current);
                if (System.Windows.Application.Current?.MainWindow != null)
                    dialog.Owner = System.Windows.Application.Current.MainWindow;

                if (dialog.ShowDialog() != true) return;

                string name = dialog.InputText;
                if (string.IsNullOrWhiteSpace(name) || name == current) return;

                await _feedService.RenameFolderAsync(folderPath, name);
                await _reloadTree();
            }
            else if (tag.Type == TreeNodeType.Feed && tag.FeedId != null)
            {
                long feedId = tag.FeedId.Value;

                var feeds = await _repository.GetAllFeedsAsync();
                var target = feeds.FirstOrDefault(f => f.Id == feedId);
                if (target == null) return;

                var dialog = new InputDialog("新しい名前", target.Title);
                if (System.Windows.Application.Current?.MainWindow != null)
                    dialog.Owner = System.Windows.Application.Current.MainWindow;

                if (dialog.ShowDialog() != true) return;

                string name = dialog.InputText;
                if (string.IsNullOrWhiteSpace(name) || name == target.Title) return;

                await _feedService.RenameFeedAsync(feedId, name);
                await _reloadTree();
            }
        }

        // フィード削除
        private async Task DeleteFeed(long id)
        {
            if (MsgBox.Show("削除しますか？", "確認", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            if (System.Windows.Application.Current.MainWindow is MainWindow main)
            {
                main.ClearAllPanels();
            }

            await _feedService.DeleteFeedAsync(id);
            await _reloadTree();
            await _refreshCurrentListView();
        }
    }
}