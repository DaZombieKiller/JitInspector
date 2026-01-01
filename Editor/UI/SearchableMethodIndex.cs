using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace JitInspector.Search
{
    internal sealed class MethodIndex
    {
        public Assembly Assembly { get; set; }
        public string Namespace { get; set; }
        public Type DeclaringType { get; set; }
        public MethodBase Method { get; set; }
    }

    internal sealed class SearchableMethodIndex
    {
        private List<MethodIndex> _methodIndices = new List<MethodIndex>();

        public async Task BuildIndexAsync(CancellationToken cancellationToken = default)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            _methodIndices.Clear();

            await Task.Run(() =>
            {
                foreach (var assembly in assemblies)
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    try
                    {
                        var types = assembly.GetTypes();
                        foreach (var type in types)
                        {
                            if (cancellationToken.IsCancellationRequested) return;

                            var methods = type.GetMethods(JitInspectorHelpers.DeclaredMembers)
                                .Where(JitInspectorHelpers.IsSupportedForJitInspection);

                            var constructors = type.GetConstructors(JitInspectorHelpers.DeclaredMembers)
                                .Where(JitInspectorHelpers.IsSupportedForJitInspection);

                            foreach (var method in Enumerable.Concat<MethodBase>(methods, constructors))
                            {
                                if (cancellationToken.IsCancellationRequested)
                                    return;

                                if (method.IsGenericMethod)
                                {
                                    foreach (var attr in method.GetCustomAttributes<JitGenericAttribute>())
                                    {
                                        _methodIndices.Add(new MethodIndex
                                        {
                                            Assembly = assembly,
                                            Namespace = type.Namespace,
                                            DeclaringType = type,
                                            Method = ((MethodInfo)method).MakeGenericMethod(attr.TypeArguments)
                                        });
                                    }
                                }
                                else
                                {
                                    _methodIndices.Add(new MethodIndex
                                    {
                                        Assembly = assembly,
                                        Namespace = type.Namespace,
                                        DeclaringType = type,
                                        Method = method
                                    });
                                }
                            }
                        }
                    }
                    catch (ReflectionTypeLoadException)
                    {
                        // Skip assemblies that can't be loaded
                    }
                }
            }, cancellationToken);
        }

        public IEnumerable<MethodIndex> Search(string query)
        {
            return _methodIndices.Where(m => IsMatch(m, query));
        }

        private static bool IsMatch(MethodIndex m, string query)
        {
            if (m.Assembly.GetName().Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                return true;

            if (m.Namespace != null && m.Namespace.Contains(query, StringComparison.OrdinalIgnoreCase))
                return true;

            var name = JitInspectorHelpers.GetMethodSignature(m.Method, includeParamNames: false, includeRichText: false);
            return name.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        public IEnumerable<Assembly> GetAssemblies() => _methodIndices.Select(m => m.Assembly).Distinct();
        public IEnumerable<string> GetNamespaces(Assembly assembly) => _methodIndices.Where(m => m.Assembly == assembly).Select(m => m.Namespace).Distinct();
        public IEnumerable<Type> GetTypes(string @namespace) => _methodIndices.Where(m => m.Namespace == @namespace).Select(m => m.DeclaringType).Distinct();
        public IEnumerable<MethodBase> GetMethods(Type type) => _methodIndices.Where(m => m.DeclaringType == type).Select(m => m.Method);

    }
}
