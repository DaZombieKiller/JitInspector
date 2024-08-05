using System;
using System.Text;

namespace JitInspector
{
    internal static class StringBuilderExtensions
    {
        public static StringBuilder AppendColored(this StringBuilder builder, string text, string color)
        {
            return builder.Append("<color=").Append(color).Append(">").Append(text).Append("</color>");
        }

        public static StringBuilder AppendTypeName(this StringBuilder stringBuilder, Type type, bool includeRichText)
        {
            if (includeRichText)
                stringBuilder.AppendColored(JitInspectorHelpers.GetTypeName(type), JitInspectorHelpers.GetHighlightColor(type));
            else
                stringBuilder.Append(JitInspectorHelpers.GetTypeName(type));

            return stringBuilder;
        }
    }
}
