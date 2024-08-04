using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace JitInspector
{
    internal static unsafe class DbgHelp
    {
        public const uint SYMOPT_IGNORE_CVREC = 0x00000080;

        public const uint SYMOPT_EXACT_SYMBOLS = 0x00000400;

        // Not part of DbgHelp but defined here for now.
        [DllImport("Kernel32", ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr LocalFree(IntPtr hMem);

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
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SymGetTypeFromName(IntPtr hProcess, ulong BaseOfDll, string Name, SYMBOL_INFOW* Symbol);

        [DllImport("DbgHelp")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SymGetTypeInfo(IntPtr hProcess, ulong ModBase, uint TypeId, SYMBOL_TYPE_INFO GetType, void* pInfo);

        [DllImport("DbgHelp", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ulong SymLoadModuleEx(IntPtr hProcess, IntPtr hFile, string ImageName, string ModuleName, ulong BaseOfDll, uint DllSize, void* Data, uint Flags);

        public static ulong SymLoadModule(IntPtr hProcess, IntPtr hFile, ProcessModule Module, void* Data, uint Flags)
        {
            return SymLoadModuleEx(hProcess, hFile, Module.FileName, Module.ModuleName, (ulong)Module.BaseAddress, (uint)Module.ModuleMemorySize, Data, Flags);
        }

        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct TI_FINDCHILDREN_PARAMS
        {
            public uint Count;
            public uint Start;
            public fixed uint ChildId[1];
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

        public enum SYMBOL_TYPE_INFO
        {
            TI_GET_SYMTAG,
            TI_GET_SYMNAME,
            TI_GET_LENGTH,
            TI_GET_TYPE,
            TI_GET_TYPEID,
            TI_GET_BASETYPE,
            TI_GET_ARRAYINDEXTYPEID,
            TI_FINDCHILDREN,
            TI_GET_DATAKIND,
            TI_GET_ADDRESSOFFSET,
            TI_GET_OFFSET,
            TI_GET_VALUE,
            TI_GET_COUNT,
            TI_GET_CHILDRENCOUNT,
            TI_GET_BITPOSITION,
            TI_GET_VIRTUALBASECLASS,
            TI_GET_VIRTUALTABLESHAPEID,
            TI_GET_VIRTUALBASEPOINTEROFFSET,
            TI_GET_CLASSPARENTID,
            TI_GET_NESTED,
            TI_GET_SYMINDEX,
            TI_GET_LEXICALPARENT,
            TI_GET_ADDRESS,
            TI_GET_THISADJUST,
            TI_GET_UDTKIND,
            TI_IS_EQUIV_TO,
            TI_GET_CALLING_CONVENTION,
            TI_IS_CLOSE_EQUIV_TO,
            TI_GTIEX_REQS_VALID,
            TI_GET_VIRTUALBASEOFFSET,
            TI_GET_VIRTUALBASEDISPINDEX,
            TI_GET_IS_REFERENCE,
            TI_GET_INDIRECTVIRTUALBASECLASS,
            TI_GET_VIRTUALBASETABLETYPE,
            TI_GET_OBJECTPOINTERTYPE,
            TI_GET_DISCRIMINATEDUNION_TAG_TYPEID,
            TI_GET_DISCRIMINATEDUNION_TAG_OFFSET,
            TI_GET_DISCRIMINATEDUNION_TAG_RANGESCOUNT,
            TI_GET_DISCRIMINATEDUNION_TAG_RANGES,
        }
    }
}
