using System;
using System.Reflection;
using Iced.Intel;

namespace JitInspector
{
    internal sealed class UnmanagedCodeReader : CodeReader
    {
        public int Length { get; }

        public int Offset { get; private set; }

        public unsafe byte* Pointer { get; }

        public unsafe UnmanagedCodeReader(RuntimeMethodHandle handle)
        {
            if (!JitInspectorHelpers.TryGetJitCode(handle, out var code, out var size))
                throw new ArgumentException(nameof(handle));

            Pointer = (byte*)code;
            Length = size;
        }

        public unsafe UnmanagedCodeReader(MethodBase method)
        {
            if (!JitInspectorHelpers.TryGetJitCode(method, out var code, out var size))
                throw new ArgumentException(nameof(method));

            Pointer = (byte*)code;
            Length = size;
        }

        public unsafe UnmanagedCodeReader(byte* pointer, int length)
        {
            Pointer = pointer;
            Length = length;
        }

        public override unsafe int ReadByte()
        {
            if (Offset >= Length)
                return -1;

            return Pointer[Offset++];
        }
    }
}
