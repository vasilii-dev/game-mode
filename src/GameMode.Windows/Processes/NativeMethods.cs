using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GameMode.Windows.Processes;

internal static class NativeMethods
{
    internal const uint QueryLimited = 0x1000, QueryInformation = 0x0400, VmRead = 0x0010,
        Synchronize = 0x00100000, Terminate = 0x0001, TokenQuery = 0x0008;
    internal const uint WaitObject0 = 0, WaitTimeout = 258;
    internal const int ErrorMoreData = 234, ErrorInsufficientBuffer = 122;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int id);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, System.Text.StringBuilder path, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ProcessIdToSessionId(int id, out uint session);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsProcessCritical(SafeProcessHandle process, [MarshalAs(UnmanagedType.Bool)] out bool critical);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadProcessMemory(SafeProcessHandle process, nint address, byte[] buffer, nuint length, out nuint bytesRead);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWow64Process2(SafeProcessHandle process, out ushort processMachine, out ushort nativeMachine);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint GetFinalPathNameByHandle(SafeFileHandle file, System.Text.StringBuilder path, uint length, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint GetDriveType(string rootPath);
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessMemoryInfo(SafeProcessHandle process, out MemoryCounters counters, uint size);
    [DllImport("ntdll.dll")]
    internal static extern int NtQueryInformationProcess(SafeProcessHandle process, int informationClass, byte[] information, uint length, out uint returnLength);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetTokenInformation(SafeTokenHandle token, int informationClass, nint information, uint length, out uint returnLength);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeSnapshotHandle CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Process32First(SafeSnapshotHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Process32Next(SafeSnapshotHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint handle);

    internal delegate bool EnumWindowCallback(nint window, nint parameter);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowCallback callback, nint parameter);
    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint window, out int processId);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint window, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(nint window, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindowAsync(nint window, int command);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CommandLineToArgvW(string commandLine, out int count);
    [DllImport("kernel32.dll")]
    internal static extern nint LocalFree(nint memory);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeServiceHandle OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumServicesStatusEx(SafeServiceHandle manager, int level, uint type, uint state,
        nint buffer, uint size, out uint needed, out uint returned, ref uint resume, string? group);
    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseServiceHandle(nint handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateProcess(string application, System.Text.StringBuilder commandLine,
        nint processAttributes, nint threadAttributes, bool inheritHandles, uint flags, nint environment,
        string currentDirectory, ref StartupInfo startup, out ProcessInformation information);
    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    internal static extern int WinVerifyTrust(nint window, ref Guid action, ref WinTrustData data);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryCounters
    {
        internal uint Size, PageFaultCount;
        internal nuint PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
            QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ProcessEntry
    {
        internal uint Size, Usage, ProcessId;
        internal nuint DefaultHeapId;
        internal uint ModuleId, Threads, ParentProcessId;
        internal int Priority;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string ExeFile;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct ServiceStatusProcess
    {
        internal uint Type, State, ControlsAccepted, Win32ExitCode, ServiceExitCode,
            CheckPoint, WaitHint, ProcessId, Flags;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct ServiceEntry
    {
        internal nint ServiceName, DisplayName;
        internal ServiceStatusProcess Status;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StartupInfo
    {
        internal uint Size;
        internal string? Reserved, Desktop, Title;
        internal uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        internal ushort ShowWindow, Reserved2Size;
        internal nint Reserved2, StdInput, StdOutput, StdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation { internal nint Process, Thread; internal uint ProcessId, ThreadId; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WinTrustFileInfo { internal uint Size; internal string Path; internal nint File, KnownSubject; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct WinTrustData
    {
        internal uint Size;
        internal nint PolicyCallbackData, SipClientData;
        internal uint UiChoice, RevocationChecks, UnionChoice;
        internal nint FileInfo;
        internal uint StateAction;
        internal nint StateData, UrlReference;
        internal uint ProviderFlags, UiContext;
        internal nint SignatureSettings;
    }
}

internal sealed class SafeSnapshotHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeSnapshotHandle() : base(true) { }
    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}
internal sealed class SafeTokenHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeTokenHandle() : base(true) { }
    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}
internal sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeServiceHandle() : base(true) { }
    protected override bool ReleaseHandle() => NativeMethods.CloseServiceHandle(handle);
}
