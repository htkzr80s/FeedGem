using FeedGem.Data;
using FeedGem.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FeedGem.UIHelpers
{
    public class TreeDragDropHandler(FeedRepository repository, Func<Task> reloadTree)
    {
        private readonly FeedRepository _repository = repository;
        private readonly Func<Task> _reloadTree = reloadTree;

        private Point _startPoint;
        private TreeViewItem? _dragSourceItem;

        // ドラッグ開始位置の記録
        public void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(null);
        }

        // ドラッグ開始の判定
        public void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;

            Point pos = e.GetPosition(null);
            Vector diff = _startPoint - pos;

            // 一定以上の距離を動かした場合にドラッグとみなす
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                var item = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
                if (item == null) return;

                _dragSourceItem = item;
                DragDrop.DoDragDrop(item, item, DragDropEffects.Move);
            }
        }

        // ドラッグ中のアイコン表示設定
        public static void OnDragOver(DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        // ドロップ時のメイン処理
        public async Task OnDrop(object sender, DragEventArgs e)
        {
            if (_dragSourceItem?.Tag is not TreeTag sourceTag) return;

            bool isFolderMove = sourceTag.Type == TreeNodeType.Folder;

            // ドロップ先のTreeViewItemを取得
            var targetItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
            if (targetItem == _dragSourceItem) return;

            string newFolderPath = "/";
            bool isUpperHalf = false;

            // 挿入位置の計算（ターゲットの上下どちらに落としたか）
            if (targetItem != null && targetItem.Tag is TreeTag targetTag)
            {
                var dropPos = e.GetPosition(targetItem);
                double itemHeight = targetItem.ActualHeight > 0 ? targetItem.ActualHeight : 32.0;
                isUpperHalf = dropPos.Y < itemHeight / 2;

                if (targetTag.Type == TreeNodeType.Folder)
                {
                    if (!isFolderMove)
                    {
                        newFolderPath = targetTag.FolderPath;
                    }
                }
                else
                {
                    newFolderPath = targetTag.FolderPath;
                }
            }

            var feeds = await _repository.GetAllFeedsAsync();

            if (isFolderMove)
            {
                // フォルダの移動（常にルート階層に配置し、並び順を更新）
                await ProcessFolderMove(feeds, sourceTag, targetItem, isUpperHalf);
            }
            else
            {
                // フィードの移動
                await ProcessFeedMove(feeds, sourceTag, newFolderPath, targetItem, isUpperHalf);
            }

            await _reloadTree();
        }

        // フォルダの並び順変更処理
        private async Task ProcessFolderMove(List<FeedInfo> allFeeds, TreeTag sourceTag, TreeViewItem? targetItem, bool isUpperHalf)
        {
            // フォルダ名で対象のダミーフィード（folder://）を特定
            var movingFolder = allFeeds.FirstOrDefault(f => f.Url == $"folder://{sourceTag.Name}");
            if (movingFolder == null) return;

            // ルートにあるフォルダとフィードを抽出
            var rootItems = allFeeds
                .Where(f => f.FolderPath == "/" && f.Id != movingFolder.Id)
                .OrderBy(f => f.SortOrder)
                .ToList();

            int insertIndex = rootItems.Count;
            if (targetItem != null && targetItem.Tag is TreeTag targetTag)
            {
                var targetFeed = allFeeds.FirstOrDefault(f => f.Id == targetTag.Id);
                if (targetFeed != null && targetFeed.FolderPath == "/")
                {
                    int baseIndex = rootItems.FindIndex(f => f.Id == targetFeed.Id);
                    insertIndex = isUpperHalf ? baseIndex : baseIndex + 1;
                }
            }

            // 新しい位置に挿入して一括更新
            rootItems.Insert(Math.Max(0, Math.Min(insertIndex, rootItems.Count)), movingFolder);
            for (int i = 0; i < rootItems.Count; i++)
            {
                await _repository.UpdateFeedSortOrderAsync(rootItems[i].Id, i);
            }
        }

        // フィードの階層移動と並び順変更処理
        private async Task ProcessFeedMove(List<FeedInfo> allFeeds, TreeTag sourceTag, string newPath, TreeViewItem? targetItem, bool isUpperHalf)
        {
            var movingFeed = allFeeds.FirstOrDefault(f => f.Id == sourceTag.Id);
            if (movingFeed == null) return;

            // フォルダパスが変更される場合は更新
            if (movingFeed.FolderPath != newPath)
            {
                await _repository.UpdateFeedPathAsync(movingFeed.Id, newPath);
            }

            // 移動先の階層にいるアイテムリストを取得（自分以外）
            var siblings = allFeeds
                .Where(f => f.FolderPath == newPath && f.Id != movingFeed.Id)
                .OrderBy(f => f.SortOrder)
                .ToList();

            int insertIndex = siblings.Count;
            if (targetItem != null && targetItem.Tag is TreeTag targetTag)
            {
                int baseIndex = siblings.FindIndex(f => f.Id == targetTag.Id);
                if (baseIndex >= 0)
                {
                    insertIndex = isUpperHalf ? baseIndex : baseIndex + 1;
                }
            }

            // 新しい並び順を適用
            siblings.Insert(Math.Max(0, Math.Min(insertIndex, siblings.Count)), movingFeed);
            for (int i = 0; i < siblings.Count; i++)
            {
                await _repository.UpdateFeedSortOrderAsync(siblings[i].Id, i);
            }
        }

        // ビジュアルツリーを遡って特定の型の親を探す
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