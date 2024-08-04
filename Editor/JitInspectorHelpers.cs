using Iced.Intel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static JitInspector.MonoInterop;

namespace JitInspector
{
    internal static unsafe class JitInspectorHelpers
    {
        public static unsafe bool TryGetJitCode(RuntimeMethodHandle handle, out void* code, out int size)
        {
            var fp = (void*)handle.GetFunctionPointer();
            var ji = mono_jit_info_table_find(mono_domain_get(), fp);

            if (ji == null)
            {
                code = null;
                size = 0;
                return false;
            }

            code = mono_jit_info_get_code_start(ji);
            size = mono_jit_info_get_code_size(ji);
            return true;
        }

        public static unsafe bool TryGetJitCode(MethodBase method, out void* code, out int size)
        {
            if (method.GetMethodBody() == null || method.IsGenericMethod)
            {
                code = null;
                size = 0;
                return false;
            }

            return TryGetJitCode(method.MethodHandle, out code, out size);
        }

        public static unsafe bool TryGetJitCode(MethodBase method, out ReadOnlySpan<byte> code)
        {
            if (!TryGetJitCode(method, out var pointer, out var size))
            {
                code = default;
                return false;
            }

            code = new ReadOnlySpan<byte>(pointer, size);
            return true;
        }

        public static unsafe bool TryGetJitCode(RuntimeMethodHandle handle, out ReadOnlySpan<byte> code)
        {
            if (!TryGetJitCode(handle, out var pointer, out var size))
            {
                code = default;
                return false;
            }

            code = new ReadOnlySpan<byte>(pointer, size);
            return true;
        }

        private static void WriteComment(TextWriter writer, string comment, bool rich)
        {
            if (rich)
            {
                writer.Write("<color=#6A9955>");
            }

            writer.Write(";\u00A0");
            writer.Write(comment);

            if (rich)
            {
                writer.WriteLine("</color>");
            }
        }

        public static unsafe void WriteDisassembly(MethodBase method, byte* code, int size, Formatter formatter, TextWriter writer)
        {
            bool rich = true;
            var reader = new UnmanagedCodeReader(code, size);
            var decoder = Decoder.Create(8 * IntPtr.Size, reader);
            var output = rich ? (FormatterOutput)new RichStringOutput() : new StringOutput();
            decoder.IP = (ulong)code;
            ulong tail = (ulong)(code + size);

            WriteComment(writer, $"Assembly listing for method {method.DeclaringType.FullName}:{method.Name}", rich);
            var opts = GetOptimizations(method.MethodHandle);

            if (opts.Length > 0)
                WriteComment(writer, $"--optimize={string.Join(",", opts)}", rich);

            if (mono_debug_enabled())
            {
                var options = GetDebugOptions();

                if (options.Length > 0)
                {
                    WriteComment(writer, $"--debug={string.Join(",", options)}", rich);
                }
                else
                {
                    WriteComment(writer, "--debug", rich);
                }
            }

            if (mono_use_fast_math)
                WriteComment(writer, "--ffast-math", rich);

            writer.WriteLine();

            while (decoder.IP < tail)
            {
                var instr = decoder.Decode();
                formatter.Format(instr, output);

                writer.Write("<color=#888888>");
                for (int i = 0; i < instr.Length; i++)
                    writer.Write($"{code[(int)(instr.IP - (ulong)code) + i]:X2}");
                writer.Write("</color>");

                if (instr.Length < 10)
                    writer.Write(new string(' ', (10 - instr.Length) * 2));

                writer.Write(" ");

                if (rich)
                    writer.WriteLine(((RichStringOutput)output).ToStringAndReset());
                else
                    writer.WriteLine(((StringOutput)output).ToStringAndReset());
            }

            writer.WriteLine();
            WriteComment(writer, $"Total bytes of code {size}", rich);
        }

        public static string[] GetOptimizations(RuntimeMethodHandle handle)
        {
            uint opts;

            try
            {
                opts = mono_get_optimizations_for_method((void*)handle.Value, default_opt);
            }
            catch
            {
                return Array.Empty<string>();
            }

            var names = new string[s_Optimizations.Length];

            for (int i = 0; i < s_Optimizations.Length; i++)
            {
                names[i] = s_Optimizations[i];

                if ((opts & (1 << i)) == 0)
                {
                    names[i] = '-' + names[i];
                }
            }

            return names;
        }

        public static string[] GetDebugOptions()
        {
            var debug = mini_get_debug_options();
            var count = 0;

            if (debug->better_cast_details != 0)
                count++;

            if (debug->mdb_optimizations != 0)
                count++;

            if (debug->gdb != 0)
                count++;

            var names = new string[count];
            var index = 0;

            if (debug->better_cast_details != 0)
                names[index++] = "casts";

            if (debug->mdb_optimizations != 0)
                names[index++] = "mdb-optimizations";

            if (debug->gdb != 0)
                names[index++] = "gdb";

            return names;
        }

        private static readonly string[] s_Optimizations =
        {
            "peephole",
            "branch",
            "inline",
            "cfold",
            "consprop",
            "copyprop",
            "deadce",
            "linears",
            "cmov",
            "shared",
            "sched",
            "intrins",
            "tailc",
            "loop",
            "fcmov",
            "leaf",
            "aot",
            "precomp",
            "abcrem",
            "ssapre",
            "exception",
            "ssa",
            "float32",
            "sse2",
            "gsharedvt",
            "gshared",
            "simd",
            "unsafe",
            "alias-analysis",
            "aggressive-inlining"
        };

        public static string GetTypeName(Type type)
        {
            if (SpecialTypeNames.ContainsKey(type))
            {
                return SpecialTypeNames[type];
            }
            else
            {
                return type.Name;
            }
        }

        public static string GetHighlightColor(Type type)
        {
            if (SpecialTypeNames.ContainsKey(type))
            {
                return "#569cd6";
            }
            else if (type.IsValueType)
            {
                return ("#86c691");
            }
            else
            {
                return ("#4ec9b0");
            }
        }

        private static readonly IReadOnlyDictionary<Type, string> SpecialTypeNames = new Dictionary<Type, string>()
        {
            { typeof(void)    , "void"   },
            { typeof(Single)  , "float"  },
            { typeof(Double)  , "double" },
            { typeof(Int16)   , "short"  },
            { typeof(Int32)   , "int"    },
            { typeof(Int64)   , "long"   },
            { typeof(UInt16)  , "ushort" },
            { typeof(UInt32)  , "uint"   },
            { typeof(UInt64)  , "ulong"  },
            { typeof(Boolean) , "bool"   },
            { typeof(String)  , "string" },
        };
    }
}
