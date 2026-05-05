using FeedGem.Models;
using FeedGem.Services;
using FeedGem.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static FeedGem.Services.LocalizationService;


namespace FeedGem.UIHelpers
{
    public class ContextMenuBuilder(
        FeedService feedService,
        FeedUpdateService updateService,
        TextBlock log,
        Action updateTime,
        Func<Task> importOpml,
        Func<Task> exportOpml)
    {
        private readonly FeedService _feedService = feedService;
        private readonly FeedUpdateService _updateService = updateService;
        private readonly TextBlock _log = log;
        private readonly Action _updateTime = updateTime;
        private readonly Func<Task> _importOpml = importOpml;
        private readonly Func<Task> _exportOpml = exportOpml;

        // 右クリックメニューを構築する
        public ContextMenu Build(TreeViewItem? treeViewItem)
        {
            var menu = new ContextMenu();
            var tag = treeViewItem?.Tag as TreeTag;

            // 共通メニュー
            var refreshItem = new MenuItem { Header = T("TreeView.CtxM.UpdateAll") };
            refreshItem.Click += async (s, e) => await SyncAll();
            menu.Items.Add(refreshItem);

            var addFolderItem = new MenuItem { Header = T("TreeView.CtxM.Create.Folder") };
            addFolderItem.Click += async (s, e) => await AddFolder(treeViewItem);
            menu.Items.Add(addFolderItem);

            menu.Items.Add(new Separator());

            // OPML
            var importOpmlItem = new MenuItem { Header = T("TreeView.CtxM.Opml.Import") };
            importOpmlItem.Click += async (s, e) => await _importOpml();
            menu.Items.Add(importOpmlItem);

            var exportOpmlItem = new MenuItem { Header = T("TreeView.CtxM.Opml.Export") };
            exportOpmlItem.Click += async (s, e) => await _exportOpml();
            menu.Items.Add(exportOpmlItem);

            // ノード（フィードまたはフォルダ）固有のメニュー項目を構築する
            // treeViewItem（右クリックした項目）の Tag に TreeTag というデータが入っているか確認する
            if (treeViewItem?.Tag is TreeTag SpecTag)
            {
                // データベース上のIDが正しく割り振られている（0より大きい）場合のみ処理を続行する
                if (SpecTag.Id > 0)
                {
                    menu.Items.Add(new Separator());

                    // 種類問わず統一されたメソッドを呼び出す
                    AddNodeSpecificItems(menu, SpecTag.Id, treeViewItem);
                }
            }
            return menu;
        }

        // ノード（フィード・フォルダ共通）固有メニュー
        private void AddNodeSpecificItems(ContextMenu menu, long id, TreeViewItem treeViewItem)
        {
            if (treeViewItem.Tag is not TreeTag tag) return;

            // すべて既読にする
            var markAllReadItem = new MenuItem { Header = T("TreeView.CtxM.MarkRead.AllItems") };
            markAllReadItem.Click += async (s, e) =>
            {
                await _feedService.MarkAllAsReadAsync(id);
            };
            menu.Items.Add(markAllReadItem);

            // 名前を変更する
            var renameItem = new MenuItem { Header = T("TreeView.CtxM.Rename.Item") };
            renameItem.Click += async (s, e) => await Rename(treeViewItem);
            menu.Items.Add(renameItem);

            // フィードの場合のみURLコピー機能を追加する
            if (tag.Type == TreeNodeType.Feed)
            {
                var copyUrlItem = new MenuItem { Header = T("TreeView.CtxM.Copy.FeedUrl") };
                copyUrlItem.Click += (s, e) =>
                {
                    // クリップボードにURLを格納
                    Clipboard.SetText(tag.Url);
                    // 操作結果をユーザーに通知
                    _log.Text = TF("LogText.Log.Copy.FeedUrl", tag.Url);
                };
                menu.Items.Add(copyUrlItem);
            }

            menu.Items.Add(new Separator());

            // 削除メニュー（フィードとフォルダで表示名を切り替える）
            var deleteItem = new MenuItem
            {
                Header = tag.Type == TreeNodeType.Folder ?
                    T("TreeView.CtxM.Delete.Folder") : T("TreeView.CtxM.Delete.Feed"),
                Tag = "Danger"
            };

            // 削除実行時の処理（フィードとフォルダで確認処理を分岐する）
            deleteItem.Click += async (s, e) =>
            {
                if (tag.Type == TreeNodeType.Folder)
                {
                    bool hasContents = await _feedService.HasFolderContentsAsync(id);

                    // 中身がある場合のみ、ユーザーに最終確認を行う
                    if (hasContents)
                    {
                        var result = MessageBox.Show(
                            T("TreeView.Msg.Delete.Folder"),
                            T("TreeView.CtxM.Delete.Folder"),
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (result != MessageBoxResult.Yes) return;
                    }
                }
                else
                {
                    // フィードの場合は常に確認を行う
                    if (MessageBox.Show(T("TreeView.Msg.Delete.Feed"), T("TreeView.Msg.Confirm"),
                            MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                        return;
                }

                if (Application.Current.MainWindow is MainWindow main)
                {
                    main.ClearAllPanels();
                }

                await _feedService.DeleteItemAsync(id);
            };
            menu.Items.Add(deleteItem);
        }

        // 全更新
        private async Task SyncAll()
        {
            _log.Text = T("LogText.Log.UpdateAll");
            Mouse.OverrideCursor = Cursors.Wait;

            try
            {
                await _updateService.UpdateAllAsync();
                _updateTime();
                _log.Text = T("LogText.Log.UpdateCompleted");
            }
            catch (Exception ex)
            {
                LoggingService.Error("Failed to sync all", ex);
                _log.Text = T("LogText.Log.UpdateError");
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        // フォルダ追加
        private async Task AddFolder(TreeViewItem? target)
        {
            var dialog = new InputDialog(T("TreeView.Dlg.Input.Name"));

            if (Application.Current?.MainWindow != null)
                dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() != true) return;

            string name = dialog.InputText?.Trim() ?? string.Empty;

            // 名前が空文字の場合はエラーを表示して終了する
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(T("TreeView.Msg.Create.Folder"), "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                long? parentId = null;

                if (target?.Tag is TreeTag tag)
                {
                    parentId = null;
                }

                await _feedService.CreateFolderAsync(name, parentId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(TF("TreeView.Msg.Error", ex.Message), "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 名前変更（フォルダ・フィード両対応）
        private async Task Rename(TreeViewItem item)
        {
            if (item.Tag is not TreeTag tag) return;

            string currentName = tag.Name;
            var dialog = new InputDialog(T("TreeView.Dlg.Input.Name"), currentName);

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
            }
            catch (Exception ex)
            {
                MessageBox.Show(TF("TreeView.Msg.Error", ex.Message), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}