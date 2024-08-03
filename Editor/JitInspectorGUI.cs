using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Iced.Intel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StringBuilder = System.Text.StringBuilder;

// Partially based on BurstInspectorGUI.cs
/*
Burst copyright © 2022 Unity Technologies
Source code of the package is licensed under the Unity Companion License (see https://unity3d.com/legal/licenses/unity_companion_license);
otherwise licensed under the Unity Package Distribution License (see https://unity3d.com/legal/licenses/Unity_Package_Distribution_License ).

Unless expressly provided otherwise, the software under this license is made available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY
KIND, EXPRESS OR IMPLIED. Please review the license for details on these and other terms and conditions.
*/

namespace JitInspector
{
    internal sealed partial class JitInspectorGUI : EditorWindow
    {
        private static readonly Type s_SplitterState = Type.GetType("UnityEditor.SplitterState, UnityEditor", throwOnError: true);

        private static readonly Type s_SplitterGUILayout = Type.GetType("UnityEditor.SplitterGUILayout, UnityEditor", throwOnError: true);

        private static readonly MethodInfo s_BeginHorizontalSplit = s_SplitterGUILayout.GetMethod("BeginHorizontalSplit", new Type[] { s_SplitterState, typeof(GUILayoutOption[]) });

        private static readonly MethodInfo s_EndHorizontalSplit = s_SplitterGUILayout.GetMethod("EndHorizontalSplit");

        private static readonly object s_TreeViewSplitterState = Activator.CreateInstance(s_SplitterState, new object[] { new float[] { 30, 70 }, new int[] { 128, 128 }, null });

        private MethodTreeView _treeView;

        private Rect _inspectorView;

        private SearchField _searchFieldMethods;

        private string _searchFilterMethods;

        private const int ScrollbarThickness = 14;

        private const int FontSize = 13;

        private List<MethodInfo> _targets;

        private List<MethodInfo> _allTargets;

        private Font _font;

        private GUIStyle _fixedFontStyle;

        private MethodInfo _target;

        private string _disasm;

        private Vector2 _disasmScroll;

        private CancellationTokenSource _searchToken;

        [MenuItem("Window/JIT Inspector", isValidateFunction: false, priority: 8)]
        private static void BurstInspector()
        {
            GetWindow<JitInspectorGUI>("JIT Inspector").Show();
        }

        public void OnEnable()
        {
            _targets = _allTargets = new List<MethodInfo>();

            // TODO: async
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsGenericType)
                        continue;

                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (method.IsGenericMethod)
                            continue;

                        if (method.MethodImplementationFlags.HasFlag(MethodImplAttributes.InternalCall))
                            continue;

                        if (method.GetMethodBody() == null)
                            continue;

                        _targets.Add(method);
                    }
                }
            }

            if (!string.IsNullOrEmpty(_searchFilterMethods))
                _targets = GetFilteredMethods(CancellationToken.None, _searchFilterMethods);

            _treeView = new MethodTreeView(new TreeViewState());
            _treeView.Initialize(_targets);
            _searchFieldMethods = new SearchField();
        }

    #if !UNITY_2023_1_OR_NEWER
        private void CleanupFont()
        {
            if (_font != null)
            {
                DestroyImmediate(_font, true);
                _font = null;
            }
        }

        public void OnDisable()
        {
            CleanupFont();
        }
    #endif

        private void RenderMethodMenu()
        {
            float width = position.width / 3;
            GUILayout.BeginVertical(GUILayout.Width(width));

            GUILayout.Label("Compile Targets", EditorStyles.boldLabel);
            RenderCompileTargetsFilters(width);

            _inspectorView = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
            _treeView.OnGUI(_inspectorView);
            GUILayout.EndVertical();
        }

        private void RenderCompileTargetsFilters(float width)
        {
            _searchFieldMethods ??= new SearchField();
            var newFilter = _searchFieldMethods.OnGUI(_searchFilterMethods);

            if (newFilter != _searchFilterMethods)
            {
                _searchToken?.Cancel();
                _searchToken = new CancellationTokenSource();
                _searchFilterMethods = newFilter;

                if (string.IsNullOrEmpty(newFilter))
                {
                    _targets = _allTargets;
                    _treeView.Initialize(_targets);
                }
                else
                {
                    Task.Run(() => GetFilteredMethods(_searchToken.Token, newFilter))
                        .ContinueWith(RefreshAfterSearchCompletion, _searchToken.Token);
                }
            }
        }

        private void RefreshAfterSearchCompletion(Task<List<MethodInfo>> search)
        {
            _targets = search.Result;
            _treeView.Initialize(_targets);
        }

        private static void AppendDisplayName(MethodBase target, StringBuilder builder)
        {
            if (target.IsStatic)
                builder.Append("static ");

            builder.Append(target.DeclaringType.FullName);
            builder.Append(":");
            builder.Append(target.Name);
            builder.Append('(');
            var parameters = target.GetParameters();

            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i].ParameterType;

                if (i > 0)
                    builder.Append(", ");

                builder.Append(param.FullName);
            }

            builder.Append(')');
        }

        private List<MethodInfo> GetFilteredMethods(CancellationToken token, string filter)
        {
            var allTargets = _allTargets;

            if (string.IsNullOrEmpty(filter))
                return allTargets;

            var builder = new StringBuilder();
            var targets = new List<MethodInfo>();

            foreach (var target in allTargets)
            {
                token.ThrowIfCancellationRequested();
                builder.Clear();
                AppendDisplayName(target, builder);
                var displayName = builder.ToString();

                if (displayName.IndexOf(filter, 0, displayName.Length, StringComparison.OrdinalIgnoreCase) == -1)
                    continue;

                targets.Add(target);
            }

            return targets;
        }

        public void OnGUI()
        {
            if (_fixedFontStyle == null || _fixedFontStyle.font == null) // also check .font as it's reset somewhere when going out of play mode.
            {
                _fixedFontStyle = new GUIStyle(GUI.skin.label);
                
            #if UNITY_2023_1_OR_NEWER
                _font = EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as Font;
            #else
                string fontName;
                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    fontName = "Consolas";
                }
                else
                {
                    fontName = "Courier";
                }
                CleanupFont();

                _font = Font.CreateDynamicFontFromOSFont(fontName, FontSize);
            #endif

                _fixedFontStyle.font = _font;
                _fixedFontStyle.fontSize = FontSize;
                _fixedFontStyle.wordWrap = true;
                _fixedFontStyle.richText = true;
            }

            GUILayout.BeginHorizontal();
            s_BeginHorizontalSplit.Invoke(null, new object[] { s_TreeViewSplitterState, null });
            RenderMethodMenu();
            GUILayout.BeginVertical();

            var selection = _treeView.GetSelection();

            if (selection.Count == 1 && selection[0] < _targets.Count)
            {
                if (_targets[selection[0]] != _target)
                {
                    _target = _targets[selection[0]];
                    _disasm = GetDisassembly(_target);
                }

                _disasmScroll = GUILayout.BeginScrollView(_disasmScroll);
                GUILayout.TextArea(_disasm, _fixedFontStyle);
                GUILayout.EndScrollView();
            }
            else
            {
                _target = null;
                _disasm = null;
            }

            GUILayout.EndVertical();
            s_EndHorizontalSplit.Invoke(null, null);
            GUILayout.EndHorizontal();
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
