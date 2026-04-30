using FeedGem.Models;
using FeedGem.Services;
using FeedGem.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FeedGem.UIHelpers
{
    public class ContextMenuBuilder(
        FeedService feedService,
        FeedUpdateService updateService,
        Func<Task> reloadTree,
        TextBlock log,
        Action updateTime,
        Func<Task> importOpml,
        Func<Task> exportOpml,
        Func<Task> refreshCurrentListView)
    {
        private readonly FeedService _feedService = feedService;
        private readonly FeedUpdateService _updateService = updateService;
        private readonly Func<Task> _reloadTree = reloadTree;
        private readonly TextBlock _log = log;
        private readonly Action _updateTime = updateTime;
        private readonly Func<Task> _importOpml = importOpml;
        private readonly Func<Task> _exportOpml = exportOpml;
        private readonly Func<Task> _refreshCurrentListView = refreshCurrentListView;

        // 右クリックメニューを構築する
        public ContextMenu Build(TreeViewItem? treeViewItem)
        {
            var menu = new ContextMenu();

            // 共通メニュー
            var refreshItem = new MenuItem { Header = "今すぐ更新" };
            refreshItem.Click += async (s, e) => await SyncAll();
            menu.Items.Add(refreshItem);

            var addFolderItem = new MenuItem { Header = "フォルダを作成..." };
            addFolderItem.Click += async (s, e) => await AddFolder(treeViewItem);
            menu.Items.Add(addFolderItem);

            menu.Items.Add(new Separator());

            // OPML
            var importOpmlItem = new MenuItem { Header = "OPMLをインポート..." };
            importOpmlItem.Click += async (s, e) => await _importOpml();
            menu.Items.Add(importOpmlItem);

            var exportOpmlItem = new MenuItem { Header = "OPMLをエクスポート..." };
            exportOpmlItem.Click += async (s, e) => await _exportOpml();
            menu.Items.Add(exportOpmlItem);

            // ノード（フィードまたはフォルダ）固有のメニュー項目を構築する
            if (treeViewItem?.Tag is TreeTag tag)
            {
                // IDが有効な場合のみ、固有メニューを追加する
                if (tag.Id > 0)
                {
                    menu.Items.Add(new Separator());

                    // ノードの種類に応じて専用のメニュー項目を追加する
                    if (tag.Type == TreeNodeType.Feed)
                    {
                        AddFeedSpecificItems(menu, tag.Id, treeViewItem);
                    }
                    else if (tag.Type == TreeNodeType.Folder)
                    {
                        AddFolderSpecificItems(menu, tag.Id, treeViewItem);
                    }
                }
            }
            return menu;
        }

        // フィード固有メニュー
        private void AddFeedSpecificItems(ContextMenu menu, long feedId, TreeViewItem treeViewItem)
        {
            if (treeViewItem.Tag is not TreeTag tag) return;

            // すべて既読にする
            var markAllReadItem = new MenuItem { Header = "すべて既読にする" };
            markAllReadItem.Click += async (s, e) =>
            {
                await _feedService.MarkAllAsReadAsync(feedId);
                await _reloadTree();
                await _refreshCurrentListView();
            };
            menu.Items.Add(markAllReadItem);

            // 名前を変更する
            var renameItem = new MenuItem { Header = "名前を変更..." };
            renameItem.Click += async (s, e) => await Rename(treeViewItem);
            menu.Items.Add(renameItem);

            // URLをコピーする
            var copyUrlItem = new MenuItem { Header = "URLをコピー" };
            copyUrlItem.Click += (s, e) =>
            {
                // クリップボードにURLを格納
                Clipboard.SetText(tag.Url);
                // 操作結果をユーザーに通知
                _log.Text = $"URLをコピーしました: {tag.Url}";
            };
            menu.Items.Add(copyUrlItem);

            menu.Items.Add(new Separator());

            // フィードを削除
            var deleteFeedItem = new MenuItem
            {
                Header = "このフィードを削除",
                Tag = "Danger"
            };
            deleteFeedItem.Click += async (s, e) => await DeleteFeed(feedId);
            menu.Items.Add(deleteFeedItem);
        }

        // フォルダ固有メニュー
        private void AddFolderSpecificItems(ContextMenu menu, long folderId, TreeViewItem treeViewItem)
        {
            if (treeViewItem.Tag is not TreeTag tag) return;

            // すべて既読にする
            var markFolderReadItem = new MenuItem { Header = "すべて既読にする" };
            markFolderReadItem.Click += async (s, e) =>
            {
                await _feedService.MarkFolderAsReadAsync(folderId);
                await _reloadTree();
                await _refreshCurrentListView();
            };
            menu.Items.Add(markFolderReadItem);

            // 名前を変更...
            var renameItem = new MenuItem { Header = "名前を変更..." };
            renameItem.Click += async (s, e) => await Rename(treeViewItem);
            menu.Items.Add(renameItem);

            // フォルダを削除
            var deleteFolderItem = new MenuItem
            {
                Header = "フォルダを削除",
                Tag = "Danger"
            };
            deleteFolderItem.Click += async (s, e) =>
            {
                bool hasContents = await _feedService.HasFolderContentsAsync(folderId);

                // 中身がある場合のみ、ユーザーに最終確認を行う
                if (hasContents)
                {
                    var result = MessageBox.Show(
                        "フォルダ内のフィードと記事もすべて削除されます。続行しますか？",
                        "フォルダの削除",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result != MessageBoxResult.Yes) return;
                }

                if (Application.Current.MainWindow is MainWindow main)
                {
                    main.ClearAllPanels();
                }

                await _feedService.DeleteFolderWithContentsAsync(folderId);
                await _reloadTree();
                await _refreshCurrentListView();
            };
            menu.Items.Add(deleteFolderItem);
        }

        // 全更新
        private async Task SyncAll()
        {
            _log.Text = "記事を更新中...";
            Mouse.OverrideCursor = Cursors.Wait;

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
                LoggingService.Error("Failed to sync all", ex);
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
            var dialog = new InputDialog("新しいフォルダ名");
            if (Application.Current?.MainWindow != null)
                dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() != true) return;

            string name = dialog.InputText;
            if (string.IsNullOrWhiteSpace(name)) return;

            try
            {
                var parentTag = target?.Tag as TreeTag;

                await _feedService.CreateFolderAsync(name, parentTag);
                await _reloadTree();
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show($"{ex.Message}\n別の名前を指定してください。", "名前の重複", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"予期せぬエラーが発生しました：{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 名前変更（フォルダ・フィード両対応）
        private async Task Rename(TreeViewItem item)
        {
            if (item.Tag is not TreeTag tag) return;

            string currentName = tag.Name;
            var dialog = new InputDialog("新しい名前", currentName);

            if (Application.Current?.MainWindow != null)
                dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() != true) return;

            string newName = dialog.InputText;

            if (string.IsNullOrWhiteSpace(newName) || newName == currentName) return;

            try
            {
                // フォルダかフィードかで分岐してサービスに投げる
                if (tag.Type == TreeNodeType.Folder)
                {
                    await _feedService.RenameFolderAsync(tag.Id, newName);
                }
                else
                {
                    await _feedService.RenameFeedAsync(tag.Id, newName);
                }

                await _reloadTree();
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show($"{ex.Message}\n別の名前を指定してください。", "名前の重複", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"予期せぬエラーが発生しました：{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // フィード削除
        private async Task DeleteFeed(long feedId)
        {
            if (MessageBox.Show("削除しますか？", "確認", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            if (Application.Current.MainWindow is MainWindow main)
            {
                main.ClearAllPanels();
            }

            await _feedService.DeleteFeedAsync(feedId);
            await _reloadTree();
            await _refreshCurrentListView();
        }
    }
}