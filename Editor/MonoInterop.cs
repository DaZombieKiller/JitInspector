using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Debug = UnityEngine.Debug;
using static JitInspector.DbgHelp;

namespace JitInspector
{
#if UNITY_EDITOR_WIN
    [InitializeOnLoad]
#endif
    internal static unsafe class MonoInterop
    {
        private static readonly Type s_MethodInfo = GetMethodInfoType();

        private static uint* s_default_opt;

        private static int* s_default_opt_set;

        private static delegate* unmanaged[Cdecl]<void*, uint, uint> s_mono_get_optimizations_for_method;

        private static delegate* unmanaged[Cdecl]<uint, void> s_mono_set_optimizations;

        private static delegate* unmanaged[Cdecl]<void*, void*, int, int, void*> s_mono_jit_info_table_find_internal;

        private static delegate* unmanaged[Cdecl]<void*, void> s_mono_domain_lock;

        private static delegate* unmanaged[Cdecl]<void*, void> s_mono_domain_unlock;

        private static delegate* unmanaged[Cdecl]<void*, delegate* unmanaged[Cdecl]<void*, void*, void*, void>, void*, void> s_monoeg_g_hash_table_foreach;

        // TODO: Not really necessary to import this, can be implemented in managed code. Only exposed directly for passing to monoeg_g_hash_table_foreach.
        public static delegate* unmanaged[Cdecl]<void*, void*, void*, void> find_tramp;

        private static int* s_mono_use_fast_math;

        private static int* s_mono_debug_format;

        private static Dictionary<string, TypeInfoEntry> ti_MonoDomain;

        private static Dictionary<string, TypeInfoEntry> ti_MonoJitDomainInfo;

        private static Dictionary<string, TypeInfoEntry> ti_MonoJitInfo;

        private static Dictionary<string, TypeInfoEntry> ti_MonoTrampInfo;

        [DllImport("__Internal", ExactSpelling = true)]
        public static extern void* mono_domain_get();

        [DllImport("__Internal", ExactSpelling = true)]
        public static extern void* mono_get_root_domain();

        [DllImport("__Internal", ExactSpelling = true)]
        public static extern void* mono_jit_info_table_find(void* domain, void* addr);

        [DllImport("__Internal", ExactSpelling = true)]
        public static extern void* mono_jit_info_get_code_start(void* ji);

        [DllImport("__Internal", ExactSpelling = true)]
        public static extern int mono_jit_info_get_code_size(void* ji);

        [DllImport("__Internal", ExactSpelling = true)]
        public static extern void* mono_jit_info_get_method(void* ji);

        [DllImport("__Internal", ExactSpelling = true)]
        public static extern int mono_parse_default_optimizations(byte* p);

        [DllImport("__Internal", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool mono_debug_enabled();

        [DllImport("__Internal", ExactSpelling = true)]
        public static extern MonoDebugOptions* mini_get_debug_options();

        [DllImport("__Internal", ExactSpelling = true)]
        public static extern byte* mono_method_full_name(void* method, [MarshalAs(UnmanagedType.Bool)] bool signature);

        public static uint mono_get_optimizations_for_method(void* method, uint default_opt)
        {
            ThrowIfMonoSymbolNotFound(s_mono_get_optimizations_for_method, nameof(mono_get_optimizations_for_method));
            return s_mono_get_optimizations_for_method(method, default_opt);
        }

        private static void mono_set_optimizations(uint opts)
        {
            ThrowIfMonoSymbolNotFound(s_mono_set_optimizations, nameof(mono_set_optimizations));
            s_mono_set_optimizations(opts);
        }

        public static void* mono_jit_info_table_find_internal(void* domain, void* addr, int try_aot, int allow_trampolines)
        {
            ThrowIfMonoSymbolNotFound(s_mono_jit_info_table_find_internal, nameof(mono_jit_info_table_find_internal));
            return s_mono_jit_info_table_find_internal(domain, addr, try_aot, allow_trampolines);
        }

        public static void mono_domain_lock(void* domain)
        {
            ThrowIfMonoSymbolNotFound(s_mono_domain_lock, nameof(mono_domain_lock));
            s_mono_domain_lock(domain);
        }

        public static void mono_domain_unlock(void* domain)
        {
            ThrowIfMonoSymbolNotFound(s_mono_domain_unlock, nameof(mono_domain_unlock));
            s_mono_domain_unlock(domain);
        }

        public static void monoeg_g_hash_table_foreach(void* hash, delegate* unmanaged[Cdecl]<void*, void*, void*, void> func, void* user_data)
        {
            ThrowIfMonoSymbolNotFound(s_monoeg_g_hash_table_foreach, nameof(monoeg_g_hash_table_foreach));
            s_monoeg_g_hash_table_foreach(hash, func, user_data);
        }

        public static void* mono_domain_jit_trampoline_hash(void* domain)
        {
            var info = *(void**)((byte*)domain + ti_MonoDomain["runtime_info"].offset);
            var hash = *(void**)((byte*)info + ti_MonoJitDomainInfo["jit_trampoline_hash"].offset);
            return hash;
        }

        public static bool mono_jit_info_is_trampoline(void* ji)
        {
            var info = ti_MonoJitInfo["is_trampoline"];
            var data = (*(int*)((byte*)ji + info.offset) >> info.bitpos) & 1;
            return data != 0;
        }

        public static void* mono_jit_info_get_trampoline_method(void* ji)
        {
            var info = *(void**)((byte*)ji + ti_MonoJitInfo["d"].offset);
            return *(void**)((byte*)info + ti_MonoTrampInfo["method"].offset);
        }

        public static bool mono_use_fast_math
        {
            get
            {
                ThrowIfMonoSymbolNotFound(s_mono_use_fast_math, nameof(mono_use_fast_math));
                return *s_mono_use_fast_math != 0;
            }

            set
            {
                ThrowIfMonoSymbolNotFound(s_mono_use_fast_math, nameof(mono_use_fast_math));
                *s_mono_use_fast_math = value ? 1 : 0;
            }
        }

        private static int mono_debug_format
        {
            get
            {
                ThrowIfMonoSymbolNotFound(s_mono_debug_format, nameof(mono_debug_format));
                return *s_mono_debug_format;
            }

            set
            {
                ThrowIfMonoSymbolNotFound(s_mono_debug_format, nameof(mono_debug_format));
                *s_mono_debug_format = value;
            }
        }

        public static uint default_opt
        {
            get
            {
                ThrowIfMonoSymbolNotFound(s_default_opt, nameof(default_opt));
                return *s_default_opt;
            }
        }

        private static bool default_opt_set
        {
            get
            {
                ThrowIfMonoSymbolNotFound(s_default_opt_set, nameof(default_opt_set));
                return *s_default_opt_set != 0;
            }
        }

        private static void ThrowIfMonoSymbolNotFound(void* address, string symbol)
        {
            if (address == null)
            {
                throw new EntryPointNotFoundException($"Unable to find a symbol named '{symbol}' in debug information.");
            }
        }

        private static bool IsMonoModuleName(string name)
        {
            return name.Equals("mono.dll", StringComparison.OrdinalIgnoreCase)
                || name.Equals("mono-2.0-bdwgc.dll", StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, TypeInfoEntry> GetTypeInfo(IntPtr hProcess, ulong ModBase, string typeName)
        {
            SYMBOL_INFOW symbol;
            symbol.SizeOfStruct = (uint)sizeof(SYMBOL_INFOW);

            if (!SymGetTypeFromName(hProcess, ModBase, typeName, &symbol))
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());

            uint childrenCount;

            if (!SymGetTypeInfo(hProcess, ModBase, symbol.TypeIndex, SYMBOL_TYPE_INFO.TI_GET_CHILDRENCOUNT, &childrenCount))
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());

            var children = (TI_FINDCHILDREN_PARAMS*)Marshal.AllocHGlobal(sizeof(TI_FINDCHILDREN_PARAMS) + sizeof(uint) * ((int)childrenCount - 1));

            try
            {
                children->Count = childrenCount;
                children->Start = 0;

                if (!SymGetTypeInfo(hProcess, ModBase, symbol.TypeIndex, SYMBOL_TYPE_INFO.TI_FINDCHILDREN, children))
                    Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());

                var fields = new Dictionary<string, TypeInfoEntry>();

                for (uint i = 0; i < children->Count; i++)
                {
                    IntPtr pName;
                    TypeInfoEntry info;

                    if (!SymGetTypeInfo(hProcess, ModBase, children->ChildId[i], SYMBOL_TYPE_INFO.TI_GET_SYMNAME, &pName))
                        Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());

                    var name = Marshal.PtrToStringUni(pName);
                    LocalFree(pName);

                    if (!SymGetTypeInfo(hProcess, ModBase, children->ChildId[i], SYMBOL_TYPE_INFO.TI_GET_OFFSET, &info.offset))
                        Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());

                    if (!SymGetTypeInfo(hProcess, ModBase, children->ChildId[i], SYMBOL_TYPE_INFO.TI_GET_BITPOSITION, &info.bitpos))
                        info.bitpos = 0;

                    fields[name] = info;
                }

                return fields;
            }
            finally
            {
                Marshal.FreeHGlobal((IntPtr)children);
            }
        }

        private static Type GetMethodInfoType()
        {
            return Type.GetType("System.Reflection.MonoMethod")
                ?? Type.GetType("System.Reflection.RuntimeMethodInfo");
        }

        public static MethodInfo GetMethodFromHandleUnsafe(RuntimeMethodHandle handle)
        {
            return (MethodInfo)Activator.CreateInstance(s_MethodInfo, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { handle }, null);
        }

        public static MethodInfo GetMethodFromHandleUnsafe(void* handle)
        {
            return GetMethodFromHandleUnsafe(*(RuntimeMethodHandle*)&handle);
        }

        private static void InitializeWithSymbols(Process process)
        {
            ulong mono = 0;

            foreach (ProcessModule module in process.Modules)
            {
                if (!string.IsNullOrEmpty(module.ModuleName) && IsMonoModuleName(module.ModuleName))
                {
                    mono = SymLoadModule(process.Handle, hFile: IntPtr.Zero, module, Data: null, Flags: 0);
                    break;
                }
            }

            if (mono == 0)
                throw new DllNotFoundException("Unable to find Mono module in process.");

            SYMBOL_INFOW info;
            info.SizeOfStruct = (uint)sizeof(SYMBOL_INFOW);

            if (SymFromName(process.Handle, nameof(mono_get_optimizations_for_method), &info))
                s_mono_get_optimizations_for_method = (delegate* unmanaged[Cdecl]<void*, uint, uint>)info.Address;

            if (SymFromName(process.Handle, nameof(mono_set_optimizations), &info))
                s_mono_set_optimizations = (delegate* unmanaged[Cdecl]<uint, void>)info.Address;

            if (SymFromName(process.Handle, nameof(mono_jit_info_table_find_internal), &info))
                s_mono_jit_info_table_find_internal = (delegate* unmanaged[Cdecl]<void*, void*, int, int, void*>)info.Address;

            if (SymFromName(process.Handle, nameof(mono_domain_lock), &info))
                s_mono_domain_lock = (delegate* unmanaged[Cdecl]<void*, void>)info.Address;

            if (SymFromName(process.Handle, nameof(mono_domain_unlock), &info))
                s_mono_domain_unlock = (delegate* unmanaged[Cdecl]<void*, void>)info.Address;

            if (SymFromName(process.Handle, nameof(monoeg_g_hash_table_foreach), &info))
                s_monoeg_g_hash_table_foreach = (delegate* unmanaged[Cdecl]<void*, delegate* unmanaged[Cdecl]<void*, void*, void*, void>, void*, void>)info.Address;

            if (SymFromName(process.Handle, nameof(find_tramp), &info))
                find_tramp = (delegate* unmanaged[Cdecl]<void*, void*, void*, void>)info.Address;

            if (SymFromName(process.Handle, nameof(default_opt), &info))
                s_default_opt = (uint*)info.Address;

            if (SymFromName(process.Handle, nameof(default_opt_set), &info))
                s_default_opt_set = (int*)info.Address;

            if (SymFromName(process.Handle, nameof(mono_debug_format), &info))
                s_mono_debug_format = (int*)info.Address;

            // This is exported, so it isn't strictly necessary to use DbgHelp to locate it.
            if (SymFromName(process.Handle, nameof(mono_use_fast_math), &info))
                s_mono_use_fast_math = (int*)info.Address;

            // Get type info
            ti_MonoDomain = GetTypeInfo(process.Handle, mono, "_MonoDomain");
            ti_MonoJitDomainInfo = GetTypeInfo(process.Handle, mono, "MonoJitDomainInfo");
            ti_MonoJitInfo = GetTypeInfo(process.Handle, mono, "_MonoJitInfo");
            ti_MonoTrampInfo = GetTypeInfo(process.Handle, mono, "MonoTrampInfo");
        }

    #if UNITY_EDITOR_WIN
        static MonoInterop()
        {
            using var process = Process.GetCurrentProcess();

            try
            {
                var symbolPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "Symbols");
                var searchPath = string.Join(";", new string[]
                {
                    Path.GetDirectoryName(EditorApplication.applicationPath),
                    $"srv*{symbolPath}*http://symbolserver.unity3d.com/",
                });

                SymSetOptions(SYMOPT_EXACT_SYMBOLS);

                if (!SymInitialize(process.Handle, searchPath, fInvadeProcess: false))
                    Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());

                InitializeWithSymbols(process);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                SymCleanup(process.Handle);
            }
        }
    #endif

        [StructLayout(LayoutKind.Explicit)]
        public struct MonoDebugOptions
        {
            [FieldOffset(0x14)]
            public int better_cast_details;

            [FieldOffset(0x18)]
            public int mdb_optimizations;

            [FieldOffset(0x30)]
            public int gdb;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FindTrampUserData
        {
            public void* ip;
            public void* method;
        }

        private struct TypeInfoEntry
        {
            public int offset;
            public int bitpos;
        }
    }
}
