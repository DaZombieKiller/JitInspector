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

        [SerializeField]
        private string _searchString;

        [SerializeField]
        private MethodReference _methodReference;

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
        private List<TreeViewItem> _searchResults;

        [Serializable]
        private struct MethodReference
        {
            public string DeclaringType;

            public string Name;

            public string[] Parameters;

            public MethodReference(MethodBase method)
            {
                Name = method.Name;
                DeclaringType = method.DeclaringType.AssemblyQualifiedName;
                var param = method.GetParameters();
                Parameters = new string[param.Length];

                for (int i = 0; i < Parameters.Length; i++)
                {
                    Parameters[i] = param[i].ParameterType.AssemblyQualifiedName;
                }
            }

            public readonly bool TryGetMethod(out MethodBase method)
            {
                var declaringType = Type.GetType(DeclaringType);
                var paramTypes = new Type[Parameters.Length];

                for (int i = 0; i < paramTypes.Length; i++)
                {
                    paramTypes[i] = Type.GetType(Parameters[i]);
                }

                if (declaringType == null || Name == null)
                {
                    method = null;
                    return false;
                }

                if (Name == ".ctor" || Name == ".cctor")
                    method = declaringType.GetConstructor(JitInspectorHelpers.DeclaredMembers, null, paramTypes, null);
                else
                    method = declaringType.GetMethod(Name, JitInspectorHelpers.DeclaredMembers, null, paramTypes, null);

                return method != null;
            }
        }

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

            _loadedSourceLines.Clear();
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
            _selectedItemName.text = string.Empty;

            _tree.OnItemSelected += OnTreeItemSelected;
            _tree.OnItemExpanded += OnTreeItemExpanded;

            if (!string.IsNullOrEmpty(_searchString))
            {
                _searchField.SetValueWithoutNotify(_searchString);
            }
        }

        private async Task InitializeAsync()
        {
            Refresh();
            _initCtes = new CancellationTokenSource();
            _statusLabel.text = "Building index...";
            try
            {
                await s_methodIndex.BuildIndexAsync(_initCtes.Token);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            _statusLabel.text = "Index built successfully.";
            await RefreshAsync();
            await Task.Delay(TimeSpan.FromSeconds(5));
            _statusLabel.text = string.Empty;
        }

        private bool ExpandMethod(MethodBase method, out TreeViewItem asmItem, out TreeViewItem nsItem, out TreeViewItem typeItem, out TreeViewItem methodItem)
        {
            asmItem = _tree.Items?.FirstOrDefault(item => (Assembly)item.Data == method.DeclaringType.Assembly);

            if (asmItem == null)
                goto NotFound;

            _tree.ExpandItem(asmItem);
            nsItem = asmItem.Children?.FirstOrDefault(item => (string)item.Data == GetNamespace(method.DeclaringType.Namespace));

            if (nsItem == null)
                goto NotFound;

            _tree.ExpandItem(nsItem);
            typeItem = nsItem.Children?.FirstOrDefault(item => (Type)item.Data == method.DeclaringType);

            if (typeItem == null)
                goto NotFound;

            _tree.ExpandItem(typeItem);
            methodItem = typeItem.Children?.FirstOrDefault(item => (MethodBase)item.Data == method);

            if (methodItem == null)
                goto NotFound;

            _tree.RefreshItem(asmItem);
            return true;

        NotFound:
            asmItem = null;
            nsItem = null;
            typeItem = null;
            methodItem = null;
            return false;
        }

        private void SelectReferencedMethod()
        {
            if (!_methodReference.TryGetMethod(out var method))
                return;

            if (!ExpandMethod(method, out _, out _, out _, out var methodItem))
                return;

            _tree.SelectItem(methodItem);
        }

        private void OnTreeItemSelected(TreeViewItem item)
        {
            if (item.Data is not MethodBase method)
                return;

            _methodReference = new MethodReference(method);
            s_syntaxBuilder.Clear();
            s_syntaxBuilder.AppendColored(method.DeclaringType.Name, JitInspectorHelpers.GetHighlightColor(method.DeclaringType));
            s_syntaxBuilder.Clear();
            JitInspectorHelpers.AppendMethodSignature(method, s_syntaxBuilder, includeParamNames: true, includeRichText: true);
            _selectedItemName.text = s_syntaxBuilder.ToString();
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
            _searchString = evt.newValue;
            _ = RunSearchAsync(evt.newValue);
        }

        private async Task RunSearchAsync(string value)
        {
            _searchCTS?.Cancel();
            _searchCTS = new CancellationTokenSource();

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_delay), _searchCTS.Token);
                _statusLabel.text = "Filtering methods...";

                if (!string.IsNullOrWhiteSpace(value))
                {
                    _searchResults = await Task.Run(() => s_methodIndex.Search(value)
                        .GroupBy(m => m.Assembly)
                        .Select(g => new TreeViewItem(g.Key.GetName().Name, g.Key, GetNamespaceItems(g), true, g.Key.GetName().Version.ToString()))
                        .ToList());
                }

                _statusLabel.text = string.Empty;
                Refresh();
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception)
            {
                throw;
            }
        }

        private async Task RefreshAsync()
        {
            if (!string.IsNullOrEmpty(_searchString))
                await RunSearchAsync(_searchString);
            else
                Refresh();
        }

        private void Refresh()
        {
            List<TreeViewItem> items;

            if (string.IsNullOrEmpty(_searchString) || _searchResults == null)
            {
                items = s_assemblies.Select(a => new TreeViewItem(a.GetName().Name, a, GetFullNameItems(a), CacheLoadTypes(a).Any(), a.GetName().Version.ToString()))
                    .Where(tvi => tvi.Children.Any())
                    .ToList();
            }
            else
            {
                items = _searchResults;
            }

            _tree.SetItems(items);
            SelectReferencedMethod();
        }

        private List<TreeViewItem> GetFullNameItems(Assembly assembly)
        {
            var clt = CacheLoadTypes(assembly);
            var data = new List<TreeViewItem>();
            s_hashSet.Clear();
            for (int i = 0; i < clt.Length; i++)
            {
                var type = clt[i];
                var ns = GetNamespace(type.Namespace);

                if (s_hashSet.Contains(ns))
                    continue;

                s_hashSet.Add(ns);
                data.Add(new TreeViewItem(ns, ns, null, true));
            }

            return data;
        }

        private static string GetNamespace(string @namespace)
        {
            if (string.IsNullOrEmpty(@namespace))
                return "-";

            return @namespace;
        }

        private List<TreeViewItem> GetTypeItems(string @namespace)
        {
            var allTypes = s_assemblies.SelectMany(CacheLoadTypes);
            var types = allTypes
                .Where(t => GetNamespace(t.Namespace) == @namespace)
                .OrderBy(t => t.Name)
                .ToList();

            return types.Select(t =>
                {
                    var mtvi = GetMethodItems(t);
                    s_syntaxBuilder.Clear();
                    s_syntaxBuilder.AppendColored(color: "#569cd6",
                               text: (t.IsAbstract ? (t.IsSealed ? "static " : "abstract ") : string.Empty)
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
            var targets = new List<TreeViewItem>();

            foreach (var m in type.GetConstructors(JitInspectorHelpers.DeclaredMembers))
            {
                s_syntaxBuilder.Clear();
                JitInspectorHelpers.AppendMethodSignature(m, s_syntaxBuilder, includeParamNames: true, includeRichText: true);
                targets.Add(new TreeViewItem(s_syntaxBuilder.ToString(), m));
            }

            foreach (var m in type.GetMethods(JitInspectorHelpers.DeclaredMembers))
            {
                if (m is MethodInfo { IsGenericMethod: true } info)
                {
                    foreach (var attr in m.GetCustomAttributes<JitGenericAttribute>())
                    {
                        var inst = info.MakeGenericMethod(attr.TypeArguments);
                        s_syntaxBuilder.Clear();
                        JitInspectorHelpers.AppendMethodSignature(inst, s_syntaxBuilder, includeParamNames: true, includeRichText: true);
                        targets.Add(new TreeViewItem(s_syntaxBuilder.ToString(), inst));
                    }
                }
                else
                {
                    s_syntaxBuilder.Clear();
                    JitInspectorHelpers.AppendMethodSignature(m, s_syntaxBuilder, includeParamNames: true, includeRichText: true);
                    targets.Add(new TreeViewItem(s_syntaxBuilder.ToString(), m));
                }
            }

            return targets;
        }

        public List<TreeViewItem> GetNamespaceItems(IGrouping<Assembly, MethodIndex> assemblyGroup)
        {
            return assemblyGroup
                .GroupBy(m => GetNamespace(m.Namespace))
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
                .Select(m => new TreeViewItem(JitInspectorHelpers.GetMethodSignature(m.Method, includeParamNames: true, includeRichText: true), m.Method))
                .ToList();
        }

        private static unsafe string GetDisassembly(MethodBase method)
        {
            if (!JitInspectorHelpers.TryGetJitCode(method, out var code, out var size))
                return "Failed to get disassembly";

            return GetDisassembly(method, (byte*)code, size, new IntelFormatter(new MonoSymbolResolver())
            {
                Options =
                {
                    RipRelativeAddresses = true,
                    FirstOperandCharIndex = 10,
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
