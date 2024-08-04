using Iced.Intel;
using JitInspector.Search;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;
using Label = UnityEngine.UIElements.Label;

namespace JitInspector.UI
{
    using static StringBuildingExtensions;

    public class JitInspectorView : EditorWindow
    {
        private static SearchableMethodIndex s_methodIndex = new SearchableMethodIndex();
        private static HashSet<string> s_hashSet = new HashSet<string>();
        private static List<Assembly> s_assemblies;

        private readonly float _delay = 0.45f;

        [SerializeField]
        private VisualTreeAsset jitInspectorTemplateAsset = default;

        private VisualElement viewBase;
        private VirtualizedTreeView tree;
        private ToolbarSearchField searchField;
        private Label statusLabel;
        private ListView jitAsmListView;
        private Label selectedItemName;
        private Button refreshButton;
        private CancellationTokenSource _initCtes;
        private CancellationTokenSource _searchCTS;

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
            TemplateContainer templateContainer = jitInspectorTemplateAsset.Instantiate();
            viewBase = templateContainer.Children().First();
            for (int i = 0; i < templateContainer.styleSheets.count; i++)
                viewBase.styleSheets.Add(templateContainer.styleSheets[i]);

            rootVisualElement.Add(viewBase);

            s_assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .OrderBy(asm => asm.FullName)
                .ToList();

            SetupUI();

            EditorApplication.delayCall += InitializeAsync;
        }
        private void SetupUI()
        {
            tree = new VirtualizedTreeView();
            tree.style.flexGrow = 1;
            viewBase.Q<VisualElement>("tree-container").Add(tree);

            searchField = viewBase.Q<ToolbarSearchField>("target-filter");
            searchField.RegisterValueChangedCallback(OnSearchChanged);
            statusLabel = new Label("Building index...");

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
        private async void InitializeAsync()
        {
            _initCtes = new CancellationTokenSource();
            statusLabel.text = "Building index...";
            await s_methodIndex.BuildIndexAsync(_initCtes.Token);
            statusLabel.text = "Index built successfully.";
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
        public void OnSearchChanged(ChangeEvent<string> evt)
        {
            RunSearchAsync(evt.newValue);
        }
        async void RunSearchAsync(string value)
        {
            _searchCTS?.Cancel();
            _searchCTS = new CancellationTokenSource();
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_delay), _searchCTS.Token);

                if (string.IsNullOrWhiteSpace(value))
                {
                    Refresh();
                }
                else
                {
                    var searchResults = s_methodIndex.Search(value)
                        .GroupBy(m => m.Assembly)
                        .Select(g => new TreeViewItem(g.Key.GetName().Name, g.Key, GetNamespaceItems(g), true, g.Key.GetName().Version.ToString()))
                        .ToList();

                    tree.SetItems(searchResults);
                }
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception)
            {
                throw;
            }
        }
        private void Refresh()
        {
            var rootItems = s_assemblies.Select(a => new TreeViewItem(a.GetName().Name, a, GetFullNameItems(a), CacheLoadTypes(a).Any(), a.GetName().Version.ToString()))
                .Where(tvi => tvi.Children.Any())
                .ToList();
            tree.SetItems(rootItems);
        }
        private List<TreeViewItem> GetFullNameItems(Assembly assembly)
        {
            var clt = CacheLoadTypes(assembly);
            var data = new List<TreeViewItem>();
            s_hashSet.Clear();
            for (int i = 0; i < clt.Length; i++)
            {
                var type = clt[i];
                if (string.IsNullOrEmpty(type.Namespace)) continue;
                if (s_hashSet.Contains(type.Namespace))
                    continue;
                s_hashSet.Add(type.Namespace);
                var name = type.Name;
                data.Add(new TreeViewItem(type.Namespace, type.Namespace, null, true));
            }

            return data;
        }
        private List<TreeViewItem> GetTypeItems(string @namespace)
        {
            var allTypes = s_assemblies.SelectMany(CacheLoadTypes);
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

        public List<TreeViewItem> GetNamespaceItems(IGrouping<Assembly, MethodIndex> assemblyGroup)
        {
            return assemblyGroup
                .GroupBy(m => m.Namespace)
                .Select(g => new TreeViewItem(g.Key, g.Key, GetTypeItems(g), true))
                .ToList();
        }
        public List<TreeViewItem> GetTypeItems(IGrouping<string, MethodIndex> namespaceGroup)
        {
            return namespaceGroup
                .GroupBy(m => m.DeclaringType)
                .Select(g => new TreeViewItem(g.Key.Name, g.Key, GetMethodItems(g), true))
                .ToList();
        }
        public List<TreeViewItem> GetMethodItems(IGrouping<Type, MethodIndex> typeGroup)
        {
            return typeGroup
                .Select(m => new TreeViewItem(GetMethodSignature(m.Method), m.Method))
                .ToList();
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
        public async Task ApplyFilterAsync(string filter)
        {
            // Cancel any pending operation
            _initCtes?.Cancel();
            _initCtes = new CancellationTokenSource();

            try
            {
                // Wait for the specified delay
                await Task.Delay(TimeSpan.FromSeconds(_delay), _initCtes.Token);

                // If we haven't been canceled, apply the filter
                //tree.SetFilter(filter);
            }
            catch (TaskCanceledException)
            {
                // Task was canceled, do nothing
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error applying filter: {ex}");
            }
        }

    }
}