using System;

namespace JitInspector
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class JitGenericAttribute : Attribute
    {
        public Type[] TypeArguments { get; }

        public JitGenericAttribute(params Type[] typeArguments)
        {
            TypeArguments = typeArguments;
        }
    }
}
