using FeedGem.Data;
using FeedGem.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Input = System.Windows.Input;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfPoint = System.Windows.Point;

namespace FeedGem.UIHelpers
{
    public class TreeDragDropHandler(FeedRepository repository, Func<Task> reloadTree)
    {
        private readonly FeedRepository _repository = repository;
        private readonly Func<Task> _reloadTree = reloadTree;

        private WpfPoint _startPoint;
        private TreeViewItem? _dragSourceItem;

        // ドラッグ開始位置記録
        public void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(null);
        }

        // ドラッグ開始判定
        public void OnMouseMove(object sender, Input.MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;

            WpfPoint pos = e.GetPosition(null);
            Vector diff = _startPoint - pos;

            if (System.Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                System.Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                var item = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
                if (item == null) return;

                _dragSourceItem = item;
                DragDrop.DoDragDrop(item, item, WpfDragDropEffects.Move);
            }
        }

        // ドラッグ中
        public static void OnDragOver(WpfDragEventArgs e)
        {
            e.Effects = WpfDragDropEffects.Move;
            e.Handled = true;
        }

        // ドロップ処理
        public async Task OnDrop(object sender, WpfDragEventArgs e)
        {
            if (_dragSourceItem?.Tag is not TreeTag sourceTag) return;

            // フォルダの移動かどうかを判定
            bool isFolderMove = sourceTag.Type == TreeNodeType.Folder;
            if (!isFolderMove && sourceTag.FeedId == null) return;

            var targetItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
            if (targetItem == _dragSourceItem) return;

            string newFolderPath = "/";
            int targetIndex = -1;
            ItemsControl targetParent = (ItemsControl)sender;

            if (targetItem?.Tag is TreeTag targetTag)
            {
                if (targetTag.Type == TreeNodeType.Folder && targetTag.FolderPath != null)
                {
                    newFolderPath = targetTag.FolderPath;
                    targetParent = targetItem;
                    targetIndex = targetItem.Items.Count;
                }
                else if (targetTag.Type == TreeNodeType.Feed)
                {
                    var parentItem = FindAncestor<TreeViewItem>(VisualTreeHelper.GetParent(targetItem));
                    if (parentItem?.Tag is TreeTag parentTag && parentTag.FolderPath != null)
                    {
                        newFolderPath = parentTag.FolderPath;
                    }

                    targetParent = parentItem ?? (ItemsControl)sender;
                    targetIndex = targetParent.Items.IndexOf(targetItem);
                }
            }

            var feeds = await _repository.GetAllFeedsAsync();

            if (isFolderMove)
            {
                string sourceFolderPath = sourceTag.FolderPath ?? "/";
                string folderName = sourceFolderPath.TrimStart('/');

                // サブフォルダ化を禁止し、強制的にルート階層へ配置する
                newFolderPath = "/";

                // 移動対象となるフォルダのダミーフィードを特定
                var dummyFolder = feeds.FirstOrDefault(f => f.FolderPath == "/" && f.Title == folderName && f.Url.StartsWith("folder://"));
                if (dummyFolder == null) return;

                // フォルダ単体のリストを作成して順序を入れ替える
                var folderList = feeds
                    .Where(f => f.FolderPath == "/" && f.Url.StartsWith("folder://") && f.Id != dummyFolder.Id)
                    .OrderBy(f => f.SortOrder)
                    .ToList();

                // ターゲット位置の計算
                int insertIndex = folderList.Count;
                if (targetItem?.Tag is TreeTag tTag)
                {
                    if (tTag.Type == TreeNodeType.Folder)
                    {
                        var targetDummy = folderList.FirstOrDefault(f => f.Title == tTag.FolderPath?.TrimStart('/'));
                        if (targetDummy != null)
                        {
                            insertIndex = folderList.IndexOf(targetDummy);
                        }
                    }
                    else if (tTag.Type == TreeNodeType.Feed)
                    {
                        // フィードにドロップされた場合、親フォルダの位置を探す
                        var parentItem = FindAncestor<TreeViewItem>(VisualTreeHelper.GetParent(targetItem));
                        if (parentItem?.Tag is TreeTag pTag && pTag.Type == TreeNodeType.Folder)
                        {
                            var targetDummy = folderList.FirstOrDefault(f => f.Title == pTag.FolderPath?.TrimStart('/'));
                            if (targetDummy != null)
                            {
                                insertIndex = folderList.IndexOf(targetDummy);
                            }
                        }
                    }
                }

                // 計算したインデックスにフォルダを挿入
                folderList.Insert(insertIndex, dummyFolder);

                // フォルダの新しい並び順を保存
                for (int i = 0; i < folderList.Count; i++)
                {
                    await _repository.UpdateFeedOrderAsync(folderList[i].Id, i);
                }
            }
            else
            {
                // 通常のフィードの移動処理
                long sourceId = sourceTag.FeedId!.Value;
                var source = feeds.FirstOrDefault(f => f.Id == sourceId);
                if (source == null) return;

                await _repository.UpdateFeedAsync(sourceId, newFolderPath, source.Title, source.Url);

                var list = feeds
                    .Where(f => f.FolderPath == newFolderPath && f.Id != sourceId)
                    .OrderBy(f => f.SortOrder)
                    .ToList();

                if (targetIndex >= 0 && targetIndex <= list.Count)
                    list.Insert(targetIndex, source);
                else
                    list.Add(source);

                for (int i = 0; i < list.Count; i++)
                {
                    await _repository.UpdateFeedOrderAsync(list[i].Id, i);
                }
            }

            await _reloadTree();
        }

        // 親探索
        private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T t) return t;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}