using Iced.Intel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Label = UnityEngine.UIElements.Label;

namespace JitInspector.UI
{
    using static StringBuilderExtensions;
    public class JitInspectorView : EditorWindow
    {

        private static HashSet<string> hashSet = new HashSet<string>();
        private static List<Assembly> _assemblies;

        [SerializeField]
        private VisualTreeAsset m_VisualTreeAsset = default;

        private VisualElement viewBase;
        private VirtualizedTreeView tree;
        private ToolbarSearchField searchField;
        private ListView jitAsmListView;
        private Label selectedItemName;
        private Button refreshButton;

        private Dictionary<Assembly, Type[]> _assemblyTypes = new Dictionary<Assembly, Type[]>();

        private List<string> loadedSourceLines = new List<string>();

        public static IEnumerable<Type> SafeTypeLoad(Assembly asm)
        {
            try
            {
                return asm.GetTypes();
            }
            catch (ReflectionTypeLoadException rtle)
            {
                return rtle.Types;
            }
        }
        [MenuItem("Window/JIT Inspector View", isValidateFunction: false, priority: 8)]
        public static void ShowExample()
        {
            var wnd = GetWindow<JitInspectorView>();
            wnd.titleContent = new GUIContent("JITInspector");
        }

        public void CreateGUI()
        {
            TemplateContainer templateContainer = m_VisualTreeAsset.Instantiate();
            viewBase = templateContainer.Children().First();
            for (int i = 0; i < templateContainer.styleSheets.count; i++)
                viewBase.styleSheets.Add(templateContainer.styleSheets[i]);

            rootVisualElement.Add(viewBase);

            _assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .OrderBy(asm => asm.FullName)
                .ToList();

            SetupUI();
        }
        private void SetupUI()
        {
            tree = new VirtualizedTreeView();
            tree.style.flexGrow = 1;
            viewBase.Q<VisualElement>("tree-container").Add(tree);

            searchField = viewBase.Q<ToolbarSearchField>("target-filter");
            jitAsmListView = viewBase.Q<ListView>("jit-asm");
            jitAsmListView.itemsSource = loadedSourceLines;
            jitAsmListView.makeItem = () =>
            {
                Label label = new Label() { enableRichText = true };
                label.AddToClassList("monospace");
                return label;
            };
            jitAsmListView.bindItem = (ve, i) => ((Label)ve).text = loadedSourceLines[i];
            jitAsmListView.Rebuild();
            jitAsmListView.ScrollToItem(0);

            selectedItemName = viewBase.Q<Label>("selected-item-name");

            tree.OnItemSelected += OnTreeItemSelected;
            tree.OnItemExpanded += OnTreeItemExpanded;

            Refresh();
        }
        private void OnTreeItemSelected(TreeViewItem item)
        {
            if (item.Data is not MethodInfo method)
                return;

            selectedItemName.text = $"{HighlightTypeName(method.DeclaringType)} {{ {GetMethodSignature(method)} }}";
            loadedSourceLines.Clear();
            var text = GetDisassembly(method);
            var lines = text.Split(Environment.NewLine);
            loadedSourceLines.AddRange(lines);
            jitAsmListView.Rebuild();
            jitAsmListView.ScrollToItem(0);
        }
        private void OnTreeItemExpanded(TreeViewItem item)
        {
            if (item.Children == null)
            {
                if (item.Data is Assembly assembly)
                {
                    item.Children = GetFullNameItems(assembly);
                }
                else if (item.Data is string @namespace)
                {
                    item.Children = GetTypeItems(@namespace);
                }
                else if (item.Data is Type type)
                {
                    item.Children = GetMethodItems(type);
                }
                tree.RefreshItem(item);
            }
        }
        private void Refresh()
        {
            var rootItems = _assemblies.Select(a => new TreeViewItem(a.GetName().Name, a, GetFullNameItems(a), CacheLoadTypes(a).Any(), a.GetName().Version.ToString()))
                .Where(tvi => tvi.Children.Any())
                .ToList();
            tree.SetItems(rootItems);
        }
        private List<TreeViewItem> GetFullNameItems(Assembly assembly)
        {
            var clt = CacheLoadTypes(assembly);
            var data = new List<TreeViewItem>();
            hashSet.Clear();
            for (int i = 0; i < clt.Length; i++)
            {
                var type = clt[i];
                if (string.IsNullOrEmpty(type.Namespace)) continue;
                if (hashSet.Contains(type.Namespace))
                    continue;
                hashSet.Add(type.Namespace);
                var name = type.Name;
                data.Add(new TreeViewItem(type.Namespace, type.Namespace, null, true));
            }

            return data;
        }
        private List<TreeViewItem> GetTypeItems(string @namespace)
        {
            var allTypes = _assemblies.SelectMany(CacheLoadTypes);
            var types = allTypes
                .Where(t => t.Namespace == @namespace)
                .OrderBy(t => t.Name)
                .ToList();

            return types.Select(t =>
                {
                    var mtvi = GetMethodItems(t);

                    var qualifiers = "<color=#569cd6>" 
                                   + (t.IsAbstract ? "abstract " : string.Empty)
                                   + (t.IsValueType ? "struct " : "class ")
                                   + "</color>";
                    var name = qualifiers + HighlightTypeName(t);
                    TreeViewItem treeViewItem = new TreeViewItem(name, t, mtvi, mtvi.Count > 0);
                    return treeViewItem;
                })
                .Where(tvi => tvi.Children.Count > 0)
                .ToList();
        }
        private Type[] CacheLoadTypes(Assembly assembly)
        {
            if (!_assemblyTypes.ContainsKey(assembly))
                _assemblyTypes[assembly] = assembly.GetTypes();

            return _assemblyTypes[assembly];
        }
        private List<TreeViewItem> GetMethodItems(Type type)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsGenericMethod && m.GetMethodBody() != null && !m.MethodImplementationFlags.HasFlag(MethodImplAttributes.InternalCall))
                .OrderBy(m => m.Name)
                .ToList();

            return methods.Select(m => new TreeViewItem(GetMethodSignature(m), m)).ToList();
        }
        private string GetMethodSignature(MethodInfo method)
        {
            var sb = new StringBuilder();
            sb.AppendTypeName(method.ReturnType);
            sb.Append(" ");
            sb.AppendColored(method.Name, "#dcdcaa");
            sb.AppendColored("(", "#efb839");
            var parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.AppendTypeName(parameters[i].ParameterType);
                sb.Append(" ");
                sb.Append(parameters[i].Name);
            }
            sb.AppendColored(")", "#efb839");
            return sb.ToString();
        }
        private static unsafe string GetDisassembly(MethodBase method)
        {
            if (!JitInspectorHelpers.TryGetJitCode(method, out var code, out var size))
                return string.Empty;

            return GetDisassembly(method, (byte*)code, size, new NasmFormatter
            {
                Options =
                {
                    FirstOperandCharIndex = 10
                }
            });
        }
        private static unsafe string GetDisassembly(MethodBase method, byte* code, int size, Formatter formatter)
        {
            using var text = new StringWriter();
            JitInspectorHelpers.WriteDisassembly(method, code, size, formatter, text);
            return text.ToString();
        }

    }
    public static class StringBuilderExtensions
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