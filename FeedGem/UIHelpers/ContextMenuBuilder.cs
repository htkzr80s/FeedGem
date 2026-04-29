using FeedGem.Data;
using FeedGem.Models;
using FeedGem.Services;
using FeedGem.Views;
using Microsoft.Data.Sqlite;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
        public ContextMenu Build(TreeViewItem? treeViewItem)
        {
            var menu = new ContextMenu();

            // 共通メニュー
            var refreshItem = new MenuItem { Header = "今すぐ更新" };
            refreshItem.Click += async (s, e) => await RefreshAll();
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
                // 現在のフォルダパスをタグから取得する
                string folderPath = tag.FolderPath == "/" ? $"/{tag.Name}" : $"{tag.FolderPath}/{tag.Name}";

                // データベースから全フィード情報を取得する
                var allFeeds = await _repository.GetAllFeedsAsync();

                // 削除対象のフォルダ内（サブフォルダ含む）にフィードが存在するか確認する
                bool hasContents = allFeeds.Any(f =>
                    !f.Url.StartsWith("folder://") &&
                    (f.FolderPath == folderPath || f.FolderPath.StartsWith(folderPath + "/")));

                // 中身がある場合はユーザーに最終確認を行う
                if (hasContents)
                {
                    var result = MessageBox.Show(
                        "フォルダ内のフィードと記事もすべて削除されます。続行しますか？",
                        "フォルダの削除",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }

                // メインウィンドウの表示パネルをクリアして、不正な参照を防ぐ
                if (Application.Current.MainWindow is MainWindow main)
                {
                    main.ClearAllPanels();
                }

                // サービスを介してフォルダと配下のデータを削除する
                await _feedService.DeleteFolderAsync(folderId);

                // 画面のツリー構造を最新の状態に更新する
                await _reloadTree();

                // 現在表示中のリストビューを更新する
                await _refreshCurrentListView();
            };
            menu.Items.Add(deleteFolderItem);
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
            if (Application.Current?.MainWindow != null)
                dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() != true) return;

            string name = dialog.InputText;
            if (string.IsNullOrWhiteSpace(name)) return;

            if (await _repository.FolderExistsAsync(name))
            {
                MessageBox.Show("同名のフォルダが既に存在します。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string path = "/";
            if (target?.Tag is TreeTag tag && tag.Type == TreeNodeType.Folder && tag.FolderPath != null)
            {
                path = tag.FolderPath;
            }

            try
            {
                await _repository.AddFeedAsync(path, name, "folder://" + Guid.NewGuid());
            }
            catch (SqliteException)
            {
                MessageBox.Show("同名のフォルダが既に存在します。", "エラー");
            }
            await _reloadTree();
        }

        // 名前変更（フォルダ・フィード両対応）
        private async Task Rename(TreeViewItem item)
        {
            if (item.Tag is not TreeTag tag) return;

            if (tag.Type == TreeNodeType.Folder)
            {
                string folderPath = tag.Id;
                string current = folderPath.TrimStart('/');

                var dialog = new InputDialog("新しい名前", current);
                if (Application.Current?.MainWindow != null)
                    dialog.Owner = Application.Current.MainWindow;

                if (dialog.ShowDialog() != true) return;

                string name = dialog.InputText;
                if (string.IsNullOrWhiteSpace(name) || name == current) return;

                if (await _repository.FolderExistsAsync(name))
                {
                    MessageBox.Show("同名のフォルダが既に存在します。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    await _feedService.RenameFolderAsync(folderId, name);
                }
                catch (SqliteException)
                {
                    MessageBox.Show("同名のフォルダが既に存在します。", "エラー");
                }
                await _reloadTree();
            }
            else if (tag.Type == TreeNodeType.Feed)
            {
                long feedId = tag.Id;
                string currentTitle = tag.Name;
                var dialog = new InputDialog("新しい名前", currentTitle);

                // メインウィンドウを親に設定して、ダイアログが背面に隠れないようにする
                if (Application.Current?.MainWindow != null)
                    dialog.Owner = Application.Current.MainWindow;

                if (dialog.ShowDialog() != true) return;

                // 入力された文字列を取得し、空白チェックと変更有無を確認する
                string newName = dialog.InputText;
                if (string.IsNullOrWhiteSpace(newName) || newName == currentTitle) return;

                // データベースの名前を更新し、UI（ツリー）をリロードして反映させる
                await _feedService.RenameFeedAsync(feedId, newName);
                await _reloadTree();
            }
        }

        // フィード削除
        private async Task DeleteFeed(long id)
        {
            if (MessageBox.Show("削除しますか？", "確認", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            if (Application.Current.MainWindow is MainWindow main)
            {
                main.ClearAllPanels();
            }

            await _feedService.DeleteFeedAsync(id);
            await _reloadTree();
            await _refreshCurrentListView();
        }
    }
}