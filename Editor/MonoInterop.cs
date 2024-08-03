using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using static JitInspector.DbgHelp;

namespace JitInspector
{
#if UNITY_EDITOR_WIN
    [UnityEditor.InitializeOnLoad]
#endif
    internal static unsafe class MonoInterop
    {
        private static readonly uint* s_default_opt;

        private static readonly delegate* unmanaged[Cdecl]<void*, uint, uint> s_mono_get_optimizations_for_method;

        private static readonly int* s_mono_use_fast_math;

        private static readonly int* s_mono_debug_format;

        [DllImport("__Internal", ExactSpelling = true)]
        public static extern void* mono_domain_get();

        [DllImport("__Internal", ExactSpelling = true)]
        public static extern void* mono_jit_info_table_find(void* domain, void* addr);

        [DllImport("__Internal", ExactSpelling = true)]
        public static extern void* mono_jit_info_get_code_start(void* ji);

        [DllImport("__Internal", ExactSpelling = true)]
        public static extern int mono_jit_info_get_code_size(void* ji);

        [DllImport("__Internal", ExactSpelling = true)]
        public static extern int mono_parse_default_optimizations(byte* p);

        [DllImport("__Internal", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool mono_debug_enabled();

        public static uint mono_get_optimizations_for_method(void* method, uint default_opt)
        {
            ThrowIfMonoSymbolNotFound(s_mono_get_optimizations_for_method, nameof(mono_get_optimizations_for_method));
            return s_mono_get_optimizations_for_method(method, default_opt);
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

        private static void ThrowIfMonoSymbolNotFound(void* address, string symbol)
        {
            if (address == null)
            {
                throw new EntryPointNotFoundException($"Unable to find a symbol named '{symbol}' in debug information.");
            }
        }

    #if UNITY_EDITOR_WIN
        static MonoInterop()
        {
            using (var process = Process.GetCurrentProcess())
            using (var module = process.MainModule)
            {
                SymSetOptions(SYMOPT_EXACT_SYMBOLS);
                SymInitialize(process.Handle, Path.GetDirectoryName(module.FileName), fInvadeProcess: true);

                SYMBOL_INFOW info;
                info.SizeOfStruct = (uint)sizeof(SYMBOL_INFOW);

                if (SymFromName(process.Handle, nameof(mono_get_optimizations_for_method), &info))
                    s_mono_get_optimizations_for_method = (delegate* unmanaged[Cdecl]<void*, uint, uint>)info.Address;

                if (SymFromName(process.Handle, nameof(default_opt), &info))
                    s_default_opt = (uint*)info.Address;

                if (SymFromName(process.Handle, nameof(mono_debug_format), &info))
                    s_mono_debug_format = (int*)info.Address;

                // This is exported, so it isn't strictly necessary to use DbgHelp to locate it.
                if (SymFromName(process.Handle, nameof(mono_use_fast_math), &info))
                    s_mono_use_fast_math = (int*)info.Address;

                SymCleanup(process.Handle);
            }
        }
    #endif
    }
}
