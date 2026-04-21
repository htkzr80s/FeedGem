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

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
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
            if (_dragSourceItem?.Tag is not TreeTag sourceTag) return;

            // フォルダの移動かどうかを判定
            bool isFolderMove = sourceTag.Type == TreeNodeType.Folder;
            if (!isFolderMove && sourceTag.FeedId == null) return;

            var targetItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
            if (targetItem == _dragSourceItem) return;

            string newFolderPath = "/";
            int targetIndex = -1;
            ItemsControl targetParent = (ItemsControl)sender;
            bool isUpperHalf = false;

            if (targetItem != null && targetItem.Tag is TreeTag targetTag)
            {
                // ドロップ位置を対象アイテム相対で取得（上下半分判定用）
                var dropPos = e.GetPosition(targetItem);
                double itemHeight = targetItem.ActualHeight > 0 ? targetItem.ActualHeight : 32.0;
                isUpperHalf = dropPos.Y < itemHeight / 2;

                if (targetTag.Type == TreeNodeType.Folder && targetTag.FolderPath != null)
                {
                    if (!isFolderMove)
                    {
                        // フィードをフォルダにドロップした場合は常にフォルダ内へ移動
                        newFolderPath = targetTag.FolderPath;
                        targetParent = targetItem;
                        targetIndex = targetItem.Items.Count;
                    }
                    else
                    {
                        // フォルダをフォルダにドロップ：ルート階層で前後並べ替え（位置計算は後続のrootListで実施）
                        newFolderPath = "/";
                    }
                }
                else if (targetTag.Type == TreeNodeType.Feed)
                {
                    // フィードを対象にした場合は同レベルで前後挿入
                    var parentItem = FindAncestor<TreeViewItem>(VisualTreeHelper.GetParent(targetItem));
                    ItemsControl parentControl = parentItem ?? (ItemsControl)sender;
                    TreeTag? parentTag = parentItem?.Tag as TreeTag;
                    newFolderPath = parentTag?.FolderPath ?? "/";
                    targetParent = parentControl;
                    int baseIndex = parentControl.Items.IndexOf(targetItem);
                    targetIndex = isUpperHalf ? baseIndex : baseIndex + 1;
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

                // ルート階層の全アイテム（フィード＋フォルダダミー）から移動元を除いたリストを作成
                // これでフィードの間や最後尾への挿入が可能になる
                var rootList = feeds
                    .Where(f => f.FolderPath == "/" && f.Id != dummyFolder.Id)
                    .OrderBy(f => f.SortOrder)
                    .ToList();

                // 挿入位置を視覚的なTreeView位置から計算
                // rootControl.Items.Containsでルート直下のアイテムのみ対象（サブフォルダ内のドロップは末尾扱い）
                int insertIndex = rootList.Count;
                ItemsControl rootControl = (ItemsControl)sender;
                if (targetItem != null && rootControl.Items.Contains(targetItem))
                {
                    int baseIndex = rootControl.Items.IndexOf(targetItem);
                    insertIndex = isUpperHalf ? baseIndex : baseIndex + 1;
                }

                // 計算した位置にフォルダを挿入
                rootList.Insert(insertIndex, dummyFolder);

                // ルート階層の全アイテムの新しい並び順を保存
                for (int i = 0; i < rootList.Count; i++)
                {
                    await _repository.UpdateFeedOrderAsync(rootList[i].Id, i);
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