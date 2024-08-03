using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor.IMGUI.Controls;

namespace JitInspector
{
    internal sealed partial class JitInspectorGUI
    {
        private sealed class MethodTreeView : TreeView
        {
            private List<MethodInfo> _targets;

            public MethodTreeView(TreeViewState state)
                : base(state)
            {
                showBorder = true;
            }

            protected override TreeViewItem BuildRoot()
            {
                var root = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };
                var allItems = new List<TreeViewItem>();

                if (_targets != null)
                {
                    var builder = new StringBuilder();

                    for (int i = 0; i < _targets.Count; i++)
                    {
                        builder.Clear();
                        AppendDisplayName(_targets[i], builder);

                        allItems.Add(new TreeViewItem
                        {
                            id = i,
                            depth = 0,
                            displayName = builder.ToString(),
                        });
                    }
                }

                SetupParentsAndChildrenFromDepths(root, allItems);
                return root;
            }

            public void Initialize(List<MethodInfo> targets)
            {
                _targets = targets;
                Reload();
            }

            protected override void SingleClickedItem(int id)
            {
                if (id < 0)
                {
                    SetExpanded(id, IsExpanded(id));
                    SetSelection(new List<int>());
                }
            }

            protected override bool CanMultiSelect(TreeViewItem item)
            {
                return false;
            }
        }
    }
}
