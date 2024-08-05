using Iced.Intel;
using System.Reflection;
using static JitInspector.MonoInterop;

namespace JitInspector
{
    internal sealed unsafe class MonoSymbolResolver : ISymbolResolver
    {
        public bool TryGetSymbol(in Instruction instruction, int operand, int instructionOperand, ulong address, int addressSize, out SymbolResult symbol)
        {
            if (!TryResolveMethodHandle(address, out var method, ref address, out bool trampoline))
            {
                symbol = default;
                return false;
            }

            var name = JitInspectorHelpers.GetMethodSignature(method, includeParamNames: false, includeRichText: true);

            if (trampoline)
                name = "<color=#9CDCFE>tramp</color> " + name;

            symbol = new SymbolResult(address, name);
            return true;
        }

        private static bool TryResolveMethodHandle(ulong ip, out MethodInfo method, ref ulong address, out bool trampoline)
        {
            var domain = mono_domain_get();

            if (domain == null)
                domain = mono_get_root_domain();

            var ji = mono_jit_info_table_find_internal(domain, (void*)ip, try_aot: 1, allow_trampolines: 1);

            if (ji != null)
            {
                if (mono_jit_info_is_trampoline(ji))
                {
                    method = GetMethodFromHandleUnsafe(mono_jit_info_get_trampoline_method(ji));
                    trampoline = true;
                    return true;
                }
                else
                {
                    address = (ulong)mono_jit_info_get_code_start(ji);
                    method = GetMethodFromHandleUnsafe(mono_jit_info_get_method(ji));
                    trampoline = false;
                    return true;
                }
            }

            FindTrampUserData user_data;
            user_data.ip = (void*)ip;
            user_data.method = null;
            mono_domain_lock(domain);
            monoeg_g_hash_table_foreach(mono_domain_jit_trampoline_hash(domain), find_tramp, &user_data);
            mono_domain_unlock(domain);
            
            if (user_data.method == null)
            {
                method = null;
                trampoline = false;
                return false;
            }

            method = GetMethodFromHandleUnsafe(user_data.method);
            trampoline = true;
            return true;
        }
    }
}
