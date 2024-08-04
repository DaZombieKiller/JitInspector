using System.Reflection;
using UnityEditor;
using UnityEngine.UIElements;
using Cursor = UnityEngine.UIElements.Cursor;

namespace JitInspector.UI
{
    internal static class VisualElementExtensions
    {
        public static void SetCursor(this VisualElement element, MouseCursor cursor)
        {
            object objCursor = new Cursor();
            PropertyInfo fields = typeof(Cursor).GetProperty("defaultCursorId", BindingFlags.NonPublic | BindingFlags.Instance);
            fields.SetValue(objCursor, (int)cursor);
            element.style.cursor = new StyleCursor((Cursor)objCursor);
        }
    }
}
