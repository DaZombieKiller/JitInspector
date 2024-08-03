﻿using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace JitInspector
{
    internal static unsafe class DbgHelp
    {
        public const uint SYMOPT_IGNORE_CVREC = 0x00000080;

        public const uint SYMOPT_EXACT_SYMBOLS = 0x00000400;

        [DllImport("DbgHelp", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SymInitialize(IntPtr hProcess, string UserSearchPath, [MarshalAs(UnmanagedType.Bool)] bool fInvadeProcess);

        [DllImport("DbgHelp", ExactSpelling = true)]
        public static extern uint SymSetOptions(uint SymOptions);

        [DllImport("DbgHelp", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SymCleanup(IntPtr hProcess);

        [DllImport("DbgHelp", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SymFromName(IntPtr hProcess, string Name, SYMBOL_INFOW* Symbol);

        [DllImport("DbgHelp", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ulong SymLoadModuleEx(IntPtr hProcess, IntPtr hFile, string ImageName, string ModuleName, ulong BaseOfDll, uint DllSize, void* Data, uint Flags);

        public static ulong SymLoadModule(IntPtr hProcess, IntPtr hFile, ProcessModule Module, void* Data, uint Flags)
        {
            return SymLoadModuleEx(hProcess, hFile, Module.FileName, Module.ModuleName, (ulong)Module.BaseAddress, (uint)Module.ModuleMemorySize, Data, Flags);
        }

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct SYMBOL_INFOW
        {
            public uint SizeOfStruct;
            public uint TypeIndex;
            public fixed ulong Reserved[2];
            public uint Index;
            public uint Size;
            public ulong ModBase;
            public uint Flags;
            public ulong Value;
            public ulong Address;
            public uint Register;
            public uint Scope;
            public uint Tag;
            public uint NameLen;
            public uint MaxNameLen;
            public fixed ushort Name[1];
        }
    }
}
