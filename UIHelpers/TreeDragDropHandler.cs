using FeedGem.Data;
using System;
using System.Linq;
using System.Threading.Tasks;
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

        // ドラッグ開始位置記録
        public void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(null);
        }

        // ドラッグ開始判定
        public void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;

            Point pos = e.GetPosition(null);
            Vector diff = _startPoint - pos;

            if (System.Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                System.Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                var item = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
                if (item == null) return;

                _dragSourceItem = item;
                DragDrop.DoDragDrop(item, item, DragDropEffects.Move);
            }
        }

        // ドラッグ中
        public static void OnDragOver(DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        // ドロップ処理
        public async Task OnDrop(object sender, DragEventArgs e)
        {
            if (_dragSourceItem == null || _dragSourceItem.Tag is not long sourceId)
                return;

            var targetItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
            if (targetItem == _dragSourceItem) return;

            string newFolderPath = "/";
            int targetIndex = -1;
            ItemsControl targetParent = (ItemsControl)sender;

            if (targetItem != null)
            {
                if (targetItem.Tag is string folderPath)
                {
                    newFolderPath = folderPath;
                    targetParent = targetItem;
                    targetIndex = targetItem.Items.Count;
                }
                else if (targetItem.Tag is long)
                {
                    var parentItem = FindAncestor<TreeViewItem>(VisualTreeHelper.GetParent(targetItem));
                    newFolderPath = parentItem?.Tag as string ?? "/";
                    targetParent = parentItem ?? (ItemsControl)sender;
                    targetIndex = targetParent.Items.IndexOf(targetItem);
                }
            }

            var feeds = await _repository.GetAllFeedsAsync();
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