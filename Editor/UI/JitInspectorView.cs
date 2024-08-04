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
using UnityEngine.UIElements;
using Label = UnityEngine.UIElements.Label;

namespace JitInspector.UI
{
    internal sealed class JitInspectorView : EditorWindow
    {
        private static readonly StringBuilder s_syntaxBuilder = new StringBuilder();
        private static SearchableMethodIndex s_methodIndex = new SearchableMethodIndex();
        private static HashSet<string> s_hashSet = new HashSet<string>();
        private static List<Assembly> s_assemblies;

        private readonly float _delay = 0.45f;

        [SerializeField]
        private VisualTreeAsset _jitInspectorTemplateAsset;
        private VisualElement _viewBase;
        private VirtualizedTreeView _tree;
        private ToolbarSearchField _searchField;
        private Label _statusLabel;
        private ListView _jitAsmListView;
        private Label _selectedItemName;
        private Button _refreshButton;
        private CancellationTokenSource _initCtes;
        private CancellationTokenSource _searchCTS;
        private Dictionary<Assembly, Type[]> _assemblyTypes = new Dictionary<Assembly, Type[]>();
        private List<string> _loadedSourceLines = new List<string>();

        [MenuItem("Window/JIT Inspector View", isValidateFunction: false, priority: 8)]
        public static void ShowExample()
        {
            var wnd = GetWindow<JitInspectorView>();
            wnd.titleContent = new GUIContent("JIT Inspector");
        }

        public void CreateGUI()
        {
            TemplateContainer templateContainer = _jitInspectorTemplateAsset.Instantiate();
            _viewBase = templateContainer.Children().First();
            for (int i = 0; i < templateContainer.styleSheets.count; i++)
                _viewBase.styleSheets.Add(templateContainer.styleSheets[i]);

            rootVisualElement.Add(_viewBase);

            s_assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .OrderBy(asm => asm.FullName)
                .ToList();

            SetupUI();
            _ = InitializeAsync();
        }

        private void SetupUI()
        {
            _tree = new VirtualizedTreeView();
            _tree.style.flexGrow = 1;
            _viewBase.Q<VisualElement>("tree-container").Add(_tree);

            _searchField = _viewBase.Q<ToolbarSearchField>("target-filter");
            _searchField.RegisterValueChangedCallback(OnSearchChanged);
            _statusLabel = _viewBase.Q<Label>("build-status-label");

            _jitAsmListView = _viewBase.Q<ListView>("jit-asm");
            _jitAsmListView.itemsSource = _loadedSourceLines;
            _jitAsmListView.makeItem = () =>
            {
                Label label = new Label() { enableRichText = true };
                label.AddToClassList("monospace");
                return label;
            };
            _jitAsmListView.bindItem = (ve, i) => ((Label)ve).text = _loadedSourceLines[i];
            _jitAsmListView.Rebuild();
            _jitAsmListView.ScrollToItem(0);

            _selectedItemName = _viewBase.Q<Label>("selected-item-name");

            _tree.OnItemSelected += OnTreeItemSelected;
            _tree.OnItemExpanded += OnTreeItemExpanded;

            Refresh();
        }

        private async Task InitializeAsync()
        {
            _initCtes = new CancellationTokenSource();
            _statusLabel.text = "Building index...";
            await s_methodIndex.BuildIndexAsync(_initCtes.Token);
            _statusLabel.text = "Index built successfully.";
            await Task.Delay(TimeSpan.FromSeconds(5));
            _statusLabel.text = string.Empty;
            Refresh();
        }

        private void OnTreeItemSelected(TreeViewItem item)
        {
            if (item.Data is not MethodInfo method)
                return;

            s_syntaxBuilder.Clear();
            s_syntaxBuilder.AppendColored(method.DeclaringType.Name, JitInspectorHelpers.GetHighlightColor(method.DeclaringType));
            var typeString = s_syntaxBuilder.ToString();
            s_syntaxBuilder.Clear();
            _selectedItemName.text = $"{typeString} {{ {JitInspectorHelpers.GetMethodSignature(method, s_syntaxBuilder)} }}";
            _loadedSourceLines.Clear();
            var text = GetDisassembly(method);
            var lines = text.Split(Environment.NewLine);
            _loadedSourceLines.AddRange(lines);
            _jitAsmListView.Rebuild();
            _jitAsmListView.ScrollToItem(0);
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
                _tree.RefreshItem(item);
            }
        }

        public void OnSearchChanged(ChangeEvent<string> evt)
        {
            _ = RunSearchAsync(evt.newValue);
        }

        private async Task RunSearchAsync(string value)
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

                    _tree.SetItems(searchResults);
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
            _tree.SetItems(rootItems);
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
                    s_syntaxBuilder.Clear();
                    s_syntaxBuilder.AppendColored(color: "#569cd6",
                               text: (t.IsAbstract ? "abstract " : string.Empty)
                                   + (t.IsValueType ? "struct " : "class "));
                    s_syntaxBuilder.AppendColored(t.Name, JitInspectorHelpers.GetHighlightColor(t));
                    var name = s_syntaxBuilder.ToString();
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
                .Where(m => JitInspectorHelpers.IsSupportedForJitInspection(m))
                .OrderBy(m => m.Name)
                .ToList();

            return methods.Select(m =>
            {
                s_syntaxBuilder.Clear();
                return new TreeViewItem(JitInspectorHelpers.GetMethodSignature(m, s_syntaxBuilder), m);
            }).ToList();
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
                .Select(m => new TreeViewItem(JitInspectorHelpers.GetMethodSignature(m.Method, s_syntaxBuilder), m.Method))
                .ToList();
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
}