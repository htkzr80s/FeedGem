using FeedGem.Data;
using FeedGem.Models;
using FeedGem.Services;
using FeedGem.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Input = System.Windows.Input;
using Media = System.Windows.Media;
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

        // ContextMenu生成
        public Wpf.ContextMenu Build(TreeViewItem? treeViewItem)
        {
            var menu = new Wpf.ContextMenu();

            var refreshItem = new Wpf.MenuItem { Header = "今すぐ更新" };
            refreshItem.Click += async (s, e) => await RefreshAll();
            menu.Items.Add(refreshItem);

            var addFolderItem = new Wpf.MenuItem { Header = "フォルダを作成..." };
            addFolderItem.Click += async (s, e) => await AddFolder(treeViewItem);
            menu.Items.Add(addFolderItem);

            menu.Items.Add(new Separator());

            // OPMLインポート
            var importOpmlItem = new Wpf.MenuItem { Header = "OPMLをインポート..." };
            importOpmlItem.Click += async (s, e) => await _importOpml();
            menu.Items.Add(importOpmlItem);

            // OPMLエクスポート
            var exportOpmlItem = new Wpf.MenuItem { Header = "OPMLをエクスポート..." };
            exportOpmlItem.Click += async (s, e) => await _exportOpml();
            menu.Items.Add(exportOpmlItem);

            if (treeViewItem?.Tag is TreeTag tag)
            {
                menu.Items.Add(new Separator());

                if (tag.Type == TreeNodeType.Feed && tag.FeedId != null)
                {
                    long feedId = tag.FeedId.Value;
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

                    var deleteFeedItem = new Wpf.MenuItem { Header = "このフィードを削除", Foreground = Media.Brushes.Red };
                    deleteFeedItem.Click += async (s, e) => await DeleteFeed(feedId);
                    menu.Items.Add(deleteFeedItem);
                }
                else if (tag.Type == TreeNodeType.Folder && tag.FolderPath != null)
                {
                    string folderPath = tag.FolderPath;

                    // フォルダ向けのすべて既読メニューを追加
                    var markFolderReadItem = new Wpf.MenuItem { Header = "すべて既読にする" };

                    markFolderReadItem.Click += async (s, e) =>
                    {
                        await _feedService.MarkFolderAsReadAsync(folderPath);
                        await _reloadTree();
                        await _refreshCurrentListView();
                    };
                    menu.Items.Add(markFolderReadItem);

                    var deleteFolderItem = new Wpf.MenuItem { Header = "フォルダを削除", Foreground = Media.Brushes.Red };

                    deleteFolderItem.Click += async (s, e) =>
                    {
                        // 1. 全フィードを取得して、このフォルダ（またはサブフォルダ）に属するフィードがあるか確認
                        var allFeeds = await _repository.GetAllFeedsAsync();
                        bool hasContents = allFeeds.Any(f =>
                            !f.Url.StartsWith("folder://") && // ダミー記事を除外
                            (f.FolderPath == folderPath || f.FolderPath.StartsWith(folderPath + "/")));

                        // 2. 中身がある場合のみ警告を出す
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

                        // UIを先にクリアして不整合防止
                        if (System.Windows.Application.Current.MainWindow is MainWindow main)
                        {
                            main.ClearAllPanels();
                        }
                        // 3. 削除処理とツリーの再読み込み
                        await _feedService.DeleteFolderAsync(folderPath);
                        await _reloadTree();
                    };
                    menu.Items.Add(deleteFolderItem);
                }
            }
            return menu;
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
                // 正常終了でもエラー終了でも、ここでカーソルを戻す
                Mouse.OverrideCursor = null;
            }
        }

        // フォルダ追加
        private async Task AddFolder(TreeViewItem? target)
        {
            var dialog = new InputDialog("フォルダ名");
            if (System.Windows.Application.Current?.MainWindow != null)
            {
                dialog.Owner = System.Windows.Application.Current.MainWindow;
            }

            if (dialog.ShowDialog() != true) return;

            string name = dialog.InputText;
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

            // UIを先にクリアして不整合防止
            if (System.Windows.Application.Current.MainWindow is MainWindow main)
            {
                main.ClearAllPanels();
            }

            await _feedService.DeleteFeedAsync(id);
            await _reloadTree();
        }
    }
}