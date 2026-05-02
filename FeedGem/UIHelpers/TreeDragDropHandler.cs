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

            long? newParentId = null;
            bool isUpperHalf = false;

            // ターゲットが存在し、かつタグ情報を持っている場合
            if (targetItem != null && targetItem.Tag is TreeTag targetTag)
            {
                if (targetTag.Type == TreeNodeType.Folder)
                {
                    // フォルダの上に落としたなら、そのフォルダのIDを新しい親にする
                    newParentId = targetTag.Id;
                }
                else
                {
                    // ターゲットがフィードなら、そのフィードの親と同じ階層に設定
                    newParentId = targetTag.ParentId == 0 ? null : targetTag.ParentId;

                    // マウス位置がアイテムの上半分か下半分かを判定して挿入位置を決める
                    var dropPos = e.GetPosition(targetItem);
                    double itemHeight = targetItem.ActualHeight > 0 ? targetItem.ActualHeight : 32.0;
                    isUpperHalf = dropPos.Y < itemHeight / 2;
                }
            }

            var feeds = await _repository.GetAllFeedsAsync();

            // 統合された移動処理を呼び出す
            await ProcessMove(feeds, sourceTag, newParentId, targetItem, isUpperHalf);
            await _reloadTree();
        }

        // 階層移動と並び順変更の統合処理
        private async Task ProcessMove(List<FeedInfo> allFeeds, TreeTag sourceTag, long? newParentId, TreeViewItem? targetItem, bool isUpperHalf)
        {
            var movingItem = allFeeds.FirstOrDefault(f => f.Id == sourceTag.Id);
            if (movingItem == null) return;

            // 移動先の階層にいるアイテムリストを取得（移動するアイテム自身は除外）
            var siblings = allFeeds
                .Where(f => f.ParentId == newParentId && f.Id != movingItem.Id)
                .OrderBy(f => f.SortOrder)
                .ToList();

            int insertIndex = siblings.Count; // デフォルトは末尾追加

            // ドロップ先が同じ階層のアイテムだった場合、挿入インデックスを算出
            if (targetItem != null && targetItem.Tag is TreeTag targetTag && targetTag.ParentId == newParentId)
            {
                int baseIndex = siblings.FindIndex(f => f.Id == targetTag.Id);
                if (baseIndex >= 0)
                {
                    insertIndex = isUpperHalf ? baseIndex : baseIndex + 1;
                }
            }

            // 新しい位置に挿入する
            siblings.Insert(Math.Max(0, Math.Min(insertIndex, siblings.Count)), movingItem);

            // 所属する親IDが変わる場合はデータベースのレイアウト情報を更新
            if (movingItem.ParentId != newParentId)
            {
                int newOrder = siblings.IndexOf(movingItem);
                await _repository.UpdateFeedLayoutAsync(movingItem.Id, newParentId, newOrder);
            }

            // 新しい並び順を抽出して一括適用する
            var updates = siblings.Select((item, index) => (item.Id, index));
            await _repository.ReorderFolderItemsAsync(updates);
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