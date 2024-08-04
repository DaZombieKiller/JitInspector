using System;
using System.Collections.Generic;
using System.Text;

namespace JitInspector.UI
{
    public static class StringBuildingExtensions
    {
        public static readonly IReadOnlyDictionary<Type, string> SpecialTypeNames = new Dictionary<Type, string>()
        {
            { typeof(void)    , "void"   },
            { typeof(Single)  , "float"  },
            { typeof(Double)  , "double" },
            { typeof(Int16)   , "short"  },
            { typeof(Int32)   , "int"    },
            { typeof(Int64)   , "long"   },
            { typeof(UInt16)  , "ushort" },
            { typeof(UInt32)  , "uint"   },
            { typeof(UInt64)  , "ulong"  },
            { typeof(Boolean) , "bool"   },
            { typeof(String)  , "string" },
        };

        public static StringBuilder AppendColored(this StringBuilder builder, string text, string color)
        {
            return builder.Append("<color=").Append(color).Append(">").Append(text).Append("</color>");
        }
        public static string HighlightTypeName(Type type) =>
                             type.IsValueType ? $"<color=#86c691>{type.Name}</color>"
                                              : $"<color=#4ec9b0>{type.Name}</color>";
        public static StringBuilder AppendTypeName(this StringBuilder stringBuilder, Type type) =>
           SpecialTypeNames.ContainsKey(type) ? stringBuilder.AppendColored(SpecialTypeNames[type], "#569cd6")
                           : type.IsValueType ? stringBuilder.AppendColored(type.Name, "#86c691")
                                              : stringBuilder.AppendColored(type.Name, "#4ec9b0");
    }
}
