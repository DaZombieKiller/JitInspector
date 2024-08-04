using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace JitInspector.Search
{
    public class MethodIndex
    {
        public Assembly Assembly { get; set; }
        public string Namespace { get; set; }
        public Type DeclaringType { get; set; }
        public MethodInfo Method { get; set; }
    }

    public class SearchableMethodIndex
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

                            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                                .Where(m => !m.IsGenericMethod && m.GetMethodBody() != null && !m.MethodImplementationFlags.HasFlag(MethodImplAttributes.InternalCall));

                            foreach (var method in methods)
                            {
                                if (cancellationToken.IsCancellationRequested) return;

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
                    catch (ReflectionTypeLoadException)
                    {
                        // Skip assemblies that can't be loaded
                    }
                }
            }, cancellationToken);
        }

        public IEnumerable<MethodIndex> Search(string query)
        {
            query = query.ToLowerInvariant();
            return _methodIndices.Where(m =>
                m.Assembly.GetName().Name.ToLowerInvariant().Contains(query) ||
                (m.Namespace?.ToLowerInvariant().Contains(query) ?? false) ||
                m.DeclaringType.Name.ToLowerInvariant().Contains(query) ||
                m.Method.Name.ToLowerInvariant().Contains(query));
        }

        public IEnumerable<Assembly> GetAssemblies() => _methodIndices.Select(m => m.Assembly).Distinct();
        public IEnumerable<string> GetNamespaces(Assembly assembly) => _methodIndices.Where(m => m.Assembly == assembly).Select(m => m.Namespace).Distinct();
        public IEnumerable<Type> GetTypes(string @namespace) => _methodIndices.Where(m => m.Namespace == @namespace).Select(m => m.DeclaringType).Distinct();
        public IEnumerable<MethodInfo> GetMethods(Type type) => _methodIndices.Where(m => m.DeclaringType == type).Select(m => m.Method);

    }
}
