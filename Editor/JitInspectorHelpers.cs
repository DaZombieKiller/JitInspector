using Iced.Intel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static JitInspector.MonoInterop;
using StringBuilder = System.Text.StringBuilder;

namespace JitInspector
{
    internal static unsafe class JitInspectorHelpers
    {
        public const BindingFlags DeclaredMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        public static bool IsSupportedForJitInspection(MethodBase method)
        {
            if (method.IsGenericMethod)
                return false;

            if (method.MethodImplementationFlags.HasFlag(MethodImplAttributes.InternalCall))
                return false;

            return method.GetMethodBody() != null;
        }

        public static string GetMethodSignature(MethodBase method, bool includeParamNames, bool includeRichText)
        {
            var builder = new StringBuilder();
            AppendMethodSignature(method, builder, includeParamNames, includeRichText);
            return builder.ToString();
        }

        public static void AppendMethodSignature(MethodBase method, StringBuilder builder, bool includeParamNames, bool includeRichText)
        {
            if (method.DeclaringType is Type declaringType)
            {
                builder.AppendTypeName(declaringType, includeRichText);
                builder.Append(':');
            }

            if (includeRichText)
            {
                builder.AppendColored(method.Name, "#dcdcaa");
                builder.AppendColored("(", "#efb839");
            }
            else
            {
                builder.Append(method.Name);
                builder.Append('(');
            }

            var parameters = method.GetParameters();

            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0)
                    builder.Append(", ");

                builder.AppendTypeName(parameters[i].ParameterType, includeRichText);

                if (includeParamNames && !string.IsNullOrEmpty(parameters[i].Name))
                {
                    builder.Append(" ");
                    builder.Append(parameters[i].Name);
                }
            }

            if (includeRichText)
                builder.AppendColored(")", "#efb839");
            else
                builder.Append(')');

            if (method is MethodInfo info)
            {
                builder.Append(':');
                builder.AppendTypeName(info.ReturnType, includeRichText);
            }

            if (method.CallingConvention.HasFlag(CallingConventions.HasThis)
                && !method.CallingConvention.HasFlag(CallingConventions.ExplicitThis))
            {
                builder.Append(':');

                if (includeRichText)
                    builder.AppendColored("this", "#569cd6");
                else
                    builder.Append("this");
            }
        }

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

        // Implementation from .NET Runtime sources
        // https://github.com/dotnet/runtime/blob/dee8a8be71d755a1d27537ea8b3c59ee2a3d49c5/src/libraries/System.Private.CoreLib/src/System/Numerics/BitOperations.cs#L452-L463
        private static int PopCount(uint value)
        {
            const uint c1 = 0x_55555555u;
            const uint c2 = 0x_33333333u;
            const uint c3 = 0x_0F0F0F0Fu;
            const uint c4 = 0x_01010101u;

            value -= (value >> 1) & c1;
            value = (value & c2) + ((value >> 2) & c2);
            value = (((value + (value >> 4)) & c3) * c4) >> 24;

            return (int)value;
        }

        public static unsafe void WriteDisassembly(MethodBase method, byte* code, int size, Formatter formatter, TextWriter writer)
        {
            bool rich = true;
            var reader = new UnmanagedCodeReader(code, size);
            var decoder = Decoder.Create(8 * IntPtr.Size, reader);
            var output = rich ? (FormatterOutput)new RichStringOutput() : new StringOutput();
            decoder.IP = (ulong)code;
            ulong tail = (ulong)(code + size);

            writer.WriteComment($"Assembly listing for method {GetMethodSignature(method, includeParamNames: false, includeRichText: false)}", rich);
            var opts = mono_get_optimizations_for_method((void*)method.MethodHandle.Value, default_opt);

            if (opts != DEFAULT_CPU_OPTIMIZATIONS)
            {
                writer.BeginComment(rich);
                writer.Write("--optimize=");

                if (opts == ALL_CPU_OPTIMIZATIONS)
                    writer.Write("all");
                else if (opts == 0)
                    writer.Write("-all");
                else
                {
                    bool all = false;
                    uint mask = ~0u;

                    if ((opts & ALL_CPU_OPTIMIZATIONS) == ALL_CPU_OPTIMIZATIONS)
                    {
                        all = true;
                        mask = EXCLUDED_FROM_ALL;
                        writer.Write("all");
                    }
                    else if (PopCount(~opts) > PopCount(opts))
                    {
                        all = true;
                        mask = ~0u;
                        writer.Write("-all");
                    }

                    for (int i = 0, n = 0; i < optflag_get_count(); i++)
                    {
                        if (all)
                        {
                            if (((opts & mask) & (1 << i)) != 0)
                            {
                                writer.Write(',');
                                writer.Write(optflag_get_name(i));
                                n++;
                            }
                        }
                        else if ((DEFAULT_CPU_OPTIMIZATIONS & (1 << i)) != 0)
                        {
                            if ((opts & (1 << i)) == 0)
                            {
                                if (n > 0)
                                    writer.Write(',');

                                writer.Write('-');
                                writer.Write(optflag_get_name(i));
                                n++;
                            }
                        }
                        else if ((opts & (1 << i)) != 0)
                        {
                            if (n > 0)
                                writer.Write(',');

                            writer.Write(optflag_get_name(i));
                            n++;
                        }
                    }
                }

                writer.EndComment(rich);
            }

            if (mono_debug_enabled())
            {
                var options = GetDebugOptions();

                if (options.Length > 0)
                {
                    writer.WriteComment($"--debug={string.Join(",", options)}", rich);
                }
            }

            if (mono_use_fast_math)
                writer.WriteComment("--ffast-math", rich);

            writer.WriteLine();

            while (decoder.IP < tail)
            {
                var instr = decoder.Decode();
                formatter.Format(instr, output);

                writer.Write((instr.IP - (ulong)code).ToString("X8"));
                writer.Write(" ");

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
            writer.WriteComment($"Total bytes of code {size}", rich);
        }

        public static string[] GetOptimizations(RuntimeMethodHandle handle)
        {
            try
            {
                return GetOptimizations(mono_get_optimizations_for_method((void*)handle.Value, default_opt));
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public static string[] GetOptimizations(uint opts)
        {
            var names = new string[optflag_get_count()];

            for (int i = 0; i < names.Length; i++)
            {
                names[i] = optflag_get_name(i);

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

        public static string GetTypeName(Type type)
        {
            if (SpecialTypeNames.TryGetValue(type, out string name))
            {
                return name;
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
                return "#86c691";
            }
            else
            {
                return "#4ec9b0";
            }
        }

        private static readonly Dictionary<Type, string> SpecialTypeNames = new Dictionary<Type, string>()
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
