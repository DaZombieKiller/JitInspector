using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.UIElements.Cursor;

namespace JitInspector.UI
{
    public class SplitContainer : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<SplitContainer, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            UxmlEnumAttributeDescription<FlexDirection> m_Orientation = new UxmlEnumAttributeDescription<FlexDirection> { name = "orientation", defaultValue = FlexDirection.Row };
            UxmlFloatAttributeDescription m_ThumbWidth = new UxmlFloatAttributeDescription { name = "thumb-width", defaultValue = 10f };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var splitContainer = ve as SplitContainer;
                splitContainer.Orientation = m_Orientation.GetValueFromBag(bag, cc);
                splitContainer._thumbWidth = m_ThumbWidth.GetValueFromBag(bag, cc);
            }
        }

        public FlexDirection Orientation
        {
            get => style.flexDirection.value;
            set
            {
                style.flexDirection = value;
                UpdateLayout();
            }
        }

        private float _thumbWidth = 10f;
        private VisualElement _firstElement;
        private VisualElement _secondElement;
        private VisualElement _thumb;
        private Vector2 _StartPointerPosition;
        private Vector2 _StartThumbPosition;

        public SplitContainer()
        {
            Init();
        }

        private void Init()
        {
            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Row;

            _firstElement = new VisualElement { name = "first-element" };
            _secondElement = new VisualElement { name = "second-element" };
            _thumb = new VisualElement { name = "thumb" };

            _firstElement.style.flexGrow = 1;
            _secondElement.style.flexGrow = 1;
            _firstElement.style.overflow = Overflow.Hidden;
            _secondElement.style.overflow = Overflow.Hidden;

            _thumb.style.width = _thumbWidth;
            _thumb.style.backgroundColor = Color.gray;

            _thumb.RegisterCallback<PointerDownEvent>(OnThumbPointerDown);
            _thumb.RegisterCallback<PointerMoveEvent>(OnThumbPointerMove);
            _thumb.RegisterCallback<PointerUpEvent>(OnThumbPointerUp);

            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
        }

        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            ProcessExistingChildren();
        }

        private void ProcessExistingChildren()
        {
            List<VisualElement> childrenToProcess = new List<VisualElement>();
            foreach (var child in Children())
            {
                if (_firstElement == child) continue;
                if (_secondElement == child) continue;
                childrenToProcess.Add(child);
            }

            Clear();

            if (childrenToProcess.Count >= 2)
            {
                _firstElement.Add(childrenToProcess[0]);
                _secondElement.Add(childrenToProcess[1]);
            }
            else if (childrenToProcess.Count == 1)
            {
                _firstElement.Add(childrenToProcess[0]);
            }

            // Re-add the elements in the correct order
            hierarchy.Add(_firstElement);
            hierarchy.Add(_thumb);
            hierarchy.Add(_secondElement);

            UpdateLayout();
        }

        private void UpdateLayout()
        {
            bool isHorizontal = Orientation == FlexDirection.Row;

            _thumb.style.width = isHorizontal ? _thumbWidth : Length.Percent(100);
            _thumb.style.height = isHorizontal ? Length.Percent(100) : _thumbWidth;
            SetCursor(_thumb, isHorizontal ? MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical);

            _firstElement.style.width = isHorizontal ? Length.Percent(50) : Length.Percent(100);
            _firstElement.style.height = isHorizontal ? Length.Percent(100) : Length.Percent(50);

            _secondElement.style.width = isHorizontal ? Length.Percent(50) : Length.Percent(100);
            _secondElement.style.height = isHorizontal ? Length.Percent(100) : Length.Percent(50);
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            UpdateLayout();
        }

        private void OnThumbPointerDown(PointerDownEvent evt)
        {
            _StartPointerPosition = evt.position;
            _StartThumbPosition = _thumb.layout.position;
            _thumb.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnThumbPointerMove(PointerMoveEvent evt)
        {
            if (!_thumb.HasPointerCapture(evt.pointerId)) return;

            Vector2 pointerPosition = evt.position;
            Vector2 localPointerPosition = _thumb.WorldToLocal(pointerPosition);
            Vector2 delta = localPointerPosition - _thumb.WorldToLocal(_StartPointerPosition);

            bool isHorizontal = Orientation == FlexDirection.Row;

            float containerSize = isHorizontal ? layout.width : layout.height;
            float newPosition = isHorizontal
                ? Mathf.Clamp(_StartThumbPosition.x + delta.x, 0, containerSize - _thumbWidth)
                : Mathf.Clamp(_StartThumbPosition.y + delta.y, 0, containerSize - _thumbWidth);

            float firstElementSize = newPosition;
            float secondElementSize = containerSize - firstElementSize - _thumbWidth;

            float firstPercentage = firstElementSize / containerSize * 100;
            float secondPercentage = secondElementSize / containerSize * 100;

            if (isHorizontal)
            {
                _firstElement.style.width = Length.Percent(firstPercentage);
                _secondElement.style.width = Length.Percent(secondPercentage);
            }
            else
            {
                _firstElement.style.height = Length.Percent(firstPercentage);
                _secondElement.style.height = Length.Percent(secondPercentage);
            }

            evt.StopPropagation();
        }

        private void OnThumbPointerUp(PointerUpEvent evt)
        {
            _thumb.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        }

        public void SetFirstElement(VisualElement element)
        {
            _firstElement.Clear();
            _firstElement.Add(element);
        }

        public void SetSecondElement(VisualElement element)
        {
            _secondElement.Clear();
            _secondElement.Add(element);
        }

        public static void SetCursor(VisualElement element, MouseCursor cursor)
        {
            object objCursor = new Cursor();
            PropertyInfo fields = typeof(Cursor).GetProperty("defaultCursorId", BindingFlags.NonPublic | BindingFlags.Instance);
            fields.SetValue(objCursor, (int)cursor);
            element.style.cursor = new StyleCursor((Cursor)objCursor);
        }
    }
}