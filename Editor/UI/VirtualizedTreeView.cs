using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace JitInspector.UI
{
    public class VirtualizedTreeView : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<VirtualizedTreeView, UxmlTraits> { }

        private List<TreeViewItem> _items = new List<TreeViewItem>();
        private List<TreeViewItem> _flattenedItems = new List<TreeViewItem>();
        private VisualElement _contentContainer;
        private Scroller _verticalScroller;
        private float _itemHeight = 20f;
        private float _totalContentHeight;
        private Dictionary<int, VisualElement> _visibleItems = new Dictionary<int, VisualElement>();
        private Queue<VisualElement> _itemPool = new Queue<VisualElement>();

        public event Action<TreeViewItem> OnItemSelected;
        public event Action<TreeViewItem> OnItemExpanded;

        public VirtualizedTreeView()
        {
            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Row;

            _contentContainer = new VisualElement
            {
                name = "content-container",
                style =
                {
                    flexGrow = 1,
                    overflow = Overflow.Hidden
                }
            };

            _verticalScroller = new Scroller(0, 100, SetContentPosition, SliderDirection.Vertical)
            {
                style =
                {
                    width = 15
                }
            };

            Add(_contentContainer);
            Add(_verticalScroller);

            RegisterCallback<WheelEvent>(OnWheelEvent);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            UpdateScrollerRange();
            RefreshVisibleItems();
        }

        private void SetContentPosition(float position)
        {
            RefreshVisibleItems();
        }

        private void OnWheelEvent(WheelEvent evt)
        {
            float delta = evt.delta.y;
            _verticalScroller.value = Mathf.Clamp(_verticalScroller.value + delta * 20, 0, _verticalScroller.highValue);
            evt.StopPropagation();
        }

        private void FlattenItems()
        {
            _flattenedItems.Clear();
            FlattenItemsRecursive(_items, 0);
        }

        private void FlattenItemsRecursive(List<TreeViewItem> items, int depth)
        {
            foreach (var item in items)
            {
                item.Depth = depth;
                _flattenedItems.Add(item);
                if (item.IsExpanded && item.Children != null)
                {
                    FlattenItemsRecursive(item.Children, depth + 1);
                }
            }
        }

        private void RefreshVisibleItems()
        {
            float viewportHeight = _contentContainer.layout.height;
            int visibleItemCount = Mathf.CeilToInt(viewportHeight / _itemHeight);
            int startIndex = Mathf.FloorToInt(_verticalScroller.value / _itemHeight);
            startIndex = Mathf.Max(0, startIndex);
            int endIndex = Mathf.Min(startIndex + visibleItemCount, _flattenedItems.Count);

            // Remove items that are no longer visible
            foreach (var kvp in _visibleItems.ToList())
            {
                if (kvp.Key < startIndex || kvp.Key >= endIndex)
                {
                    RecycleItem(kvp.Value);
                    _visibleItems.Remove(kvp.Key);
                }
            }

            // Add or update visible items
            for (int i = startIndex; i < endIndex; i++)
            {
                if (!_visibleItems.TryGetValue(i, out var itemElement))
                {
                    itemElement = GetOrCreateItemElement();
                    _visibleItems[i] = itemElement;
                    _contentContainer.Add(itemElement);
                }

                UpdateItemElement(itemElement, _flattenedItems[i], i);

                // Set the position of the item element
                itemElement.style.position = Position.Absolute;
                itemElement.style.top = (i - startIndex) * (_itemHeight - 0.25f);
            }
        }


        private VisualElement GetOrCreateItemElement()
        {
            if (_itemPool.Count > 0)
            {
                return _itemPool.Dequeue();
            }

            var itemElement = new Button();
            var extraDataLabel = new Label { name = "tree-item-extra-data" };
            var indent = new VisualElement { name = "indent-element" };
            var expansionLabel = new Label { name = "expansion-label", enableRichText = false };
            var nameLabel = new Label { name = "name-label", enableRichText = true };

            itemElement.AddToClassList("tree-item");
            itemElement.Add(indent);
            itemElement.Add(expansionLabel);
            itemElement.Add(nameLabel);
            itemElement.Add(extraDataLabel);
            return itemElement;
        }
        const string expanded = "▼ ";
        const string collapsed = "▶ ";
        private void UpdateItemElement(VisualElement itemElement, TreeViewItem item, int index)
        {
            var button = itemElement as Button;
            button.clickable = new Clickable(() => OnItemClicked(item));

            var indentElement = button.Q<VisualElement>("indent-element");
            var expansionLabel = button.Q<Label>("expansion-label");
            var nameLabel = button.Q<Label>("name-label");

            indentElement.style.width = item.Depth * 10;
            expansionLabel.text = item.HasChildren
                                ? (item.IsExpanded ? expanded : collapsed)
                                : "  ";
            nameLabel.text = item.DisplayName;

            var extraDataLabel = itemElement.Q<Label>("tree-item-extra-data");
            extraDataLabel.text = item.ExtraData;
        }
        private void RecycleItem(VisualElement itemElement)
        {
            _contentContainer.Remove(itemElement);
            _itemPool.Enqueue(itemElement);
        }

        private void UpdateScrollerRange()
        {
            _totalContentHeight = _flattenedItems.Count * _itemHeight;
            float viewportHeight = this.layout.height;
            _verticalScroller.highValue = Mathf.Max(0, _totalContentHeight - viewportHeight);
            _verticalScroller.slider.pageSize = viewportHeight;

            _verticalScroller.value = Mathf.Clamp(_verticalScroller.value, 0, _verticalScroller.highValue);
        }

        public void SetItems(List<TreeViewItem> items)
        {
            _items = items;
            FlattenItems();
            UpdateScrollerRange();
            RefreshVisibleItems();
        }

        private void OnItemClicked(TreeViewItem item)
        {
            if (item.HasChildren)
            {
                item.IsExpanded = !item.IsExpanded;
                if (item.IsExpanded && item.Children == null)
                {
                    OnItemExpanded?.Invoke(item);
                }
                FlattenItems();
                UpdateScrollerRange();
                RefreshVisibleItems();
            }
            OnItemSelected?.Invoke(item);
        }

        public void RefreshItem(TreeViewItem item)
        {
            FlattenItems();
            UpdateScrollerRange();
            RefreshVisibleItems();
        }
    }

    public class TreeViewItem
    {
        public string DisplayName { get; set; }
        public string ExtraData { get; set; }
        public object Data { get; set; }
        public List<TreeViewItem> Children { get; set; }
        public bool IsExpanded { get; set; }
        public int Depth { get; set; }
        public bool HasChildren { get; set; }

        public TreeViewItem(string displayName, object data, List<TreeViewItem> children = null, bool hasChildren = false, string extraData = null)
        {
            DisplayName = displayName;
            Data = data;
            Children = children;
            IsExpanded = false;
            HasChildren = hasChildren || (children != null && children.Count > 0);
            ExtraData = extraData;
        }
    }
}