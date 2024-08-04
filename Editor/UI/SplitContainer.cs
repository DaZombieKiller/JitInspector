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
                splitContainer.ThumbWidth = m_ThumbWidth.GetValueFromBag(bag, cc);
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

        private float ThumbWidth = 10f;
        private VisualElement m_FirstElement;
        private VisualElement m_SecondElement;
        private VisualElement m_Thumb;

        public SplitContainer()
        {
            Init();
        }

        private void Init()
        {
            style.flexGrow = 1;
            style.flexDirection = FlexDirection.Row;

            m_FirstElement = new VisualElement { name = "first-element" };
            m_SecondElement = new VisualElement { name = "second-element" };
            m_Thumb = new VisualElement { name = "thumb" };

            m_FirstElement.style.flexGrow = 1;
            m_SecondElement.style.flexGrow = 1;
            m_FirstElement.style.overflow = Overflow.Hidden;
            m_SecondElement.style.overflow = Overflow.Hidden;

            m_Thumb.style.width = ThumbWidth;
            m_Thumb.style.backgroundColor = Color.gray;

            m_Thumb.RegisterCallback<PointerDownEvent>(OnThumbPointerDown);
            m_Thumb.RegisterCallback<PointerMoveEvent>(OnThumbPointerMove);
            m_Thumb.RegisterCallback<PointerUpEvent>(OnThumbPointerUp);

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
                if (m_FirstElement == child) continue;
                if (m_SecondElement == child) continue;
                childrenToProcess.Add(child);
            }

            Clear();

            if (childrenToProcess.Count >= 2)
            {
                m_FirstElement.Add(childrenToProcess[0]);
                m_SecondElement.Add(childrenToProcess[1]);
            }
            else if (childrenToProcess.Count == 1)
            {
                m_FirstElement.Add(childrenToProcess[0]);
            }

            // Re-add the elements in the correct order
            hierarchy.Add(m_FirstElement);
            hierarchy.Add(m_Thumb);
            hierarchy.Add(m_SecondElement);

            UpdateLayout();
        }

        private void UpdateLayout()
        {
            bool isHorizontal = Orientation == FlexDirection.Row;

            m_Thumb.style.width = isHorizontal ? ThumbWidth : Length.Percent(100);
            m_Thumb.style.height = isHorizontal ? Length.Percent(100) : ThumbWidth;
            SetCursor(m_Thumb, isHorizontal ? MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical);

            m_FirstElement.style.width = isHorizontal ? Length.Percent(50) : Length.Percent(100);
            m_FirstElement.style.height = isHorizontal ? Length.Percent(100) : Length.Percent(50);

            m_SecondElement.style.width = isHorizontal ? Length.Percent(50) : Length.Percent(100);
            m_SecondElement.style.height = isHorizontal ? Length.Percent(100) : Length.Percent(50);
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            UpdateLayout();
        }

        private Vector2 m_StartPointerPosition;
        private Vector2 m_StartThumbPosition;

        private void OnThumbPointerDown(PointerDownEvent evt)
        {
            m_StartPointerPosition = evt.position;
            m_StartThumbPosition = m_Thumb.layout.position;
            m_Thumb.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnThumbPointerMove(PointerMoveEvent evt)
        {
            if (!m_Thumb.HasPointerCapture(evt.pointerId)) return;

            Vector2 pointerPosition = evt.position;
            Vector2 localPointerPosition = m_Thumb.WorldToLocal(pointerPosition);
            Vector2 delta = localPointerPosition - m_Thumb.WorldToLocal(m_StartPointerPosition);

            bool isHorizontal = Orientation == FlexDirection.Row;

            float containerSize = isHorizontal ? layout.width : layout.height;
            float newPosition = isHorizontal
                ? Mathf.Clamp(m_StartThumbPosition.x + delta.x, 0, containerSize - ThumbWidth)
                : Mathf.Clamp(m_StartThumbPosition.y + delta.y, 0, containerSize - ThumbWidth);

            float firstElementSize = newPosition;
            float secondElementSize = containerSize - firstElementSize - ThumbWidth;

            float firstPercentage = firstElementSize / containerSize * 100;
            float secondPercentage = secondElementSize / containerSize * 100;

            if (isHorizontal)
            {
                m_FirstElement.style.width = Length.Percent(firstPercentage);
                m_SecondElement.style.width = Length.Percent(secondPercentage);
            }
            else
            {
                m_FirstElement.style.height = Length.Percent(firstPercentage);
                m_SecondElement.style.height = Length.Percent(secondPercentage);
            }

            evt.StopPropagation();
        }

        private void OnThumbPointerUp(PointerUpEvent evt)
        {
            m_Thumb.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        }

        public void SetFirstElement(VisualElement element)
        {
            m_FirstElement.Clear();
            m_FirstElement.Add(element);
        }

        public void SetSecondElement(VisualElement element)
        {
            m_SecondElement.Clear();
            m_SecondElement.Add(element);
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