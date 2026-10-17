using System.Runtime.InteropServices;

namespace InstallerClean.Interop.Native;

/// <summary>
/// P/Invoke surface for rstrtmgr.dll, the Restart Manager, which names the processes that
/// have a file open. Source-generated <see cref="LibraryImportAttribute"/> stubs over
/// blittable arguments, as in <see cref="Kernel32"/>, so a call made after the running exe
/// has been renamed aside reaches no assembly that is not loaded already. Each is loaded
/// from System32 only, so a file of the same name beside the exe is never loaded into the
/// elevated process.
/// </summary>
internal static partial class Rstrtmgr
{
    private const string Library = "rstrtmgr.dll";

    internal const int ERROR_SUCCESS = 0;

    /// <summary>CCH_RM_SESSION_KEY: the characters in a session key, its terminator not counted.</summary>
    internal const int SessionKeyLength = 32;

    /// <summary>CCH_RM_MAX_APP_NAME, its terminator not counted.</summary>
    internal const int MaxAppNameLength = 255;

    /// <summary>CCH_RM_MAX_SVC_NAME, its terminator not counted.</summary>
    internal const int MaxServiceNameLength = 63;

    /// <summary>
    /// Starts a Restart Manager session. <paramref name="sessionKey"/> takes
    /// <see cref="SessionKeyLength"/> characters and a terminator.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "RmStartSession")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static unsafe partial int RmStartSession(out uint sessionHandle, uint sessionFlags, char* sessionKey);

    /// <summary>Registers the files whose holders <see cref="RmGetList"/> is to name.</summary>
    [LibraryImport(Library, EntryPoint = "RmRegisterResources")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static unsafe partial int RmRegisterResources(
        uint sessionHandle, uint fileCount, char** fileNames,
        uint applicationCount, void* applications, uint serviceCount, char** serviceNames);

    /// <summary>
    /// Lists the processes holding the registered files. Answers
    /// <see cref="Kernel32.ERROR_MORE_DATA"/> where <paramref name="count"/> is fewer than
    /// <paramref name="needed"/>, with nothing written.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "RmGetList")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static unsafe partial int RmGetList(
        uint sessionHandle, out uint needed, ref uint count, RM_PROCESS_INFO* affectedApps, out uint rebootReasons);

    [LibraryImport(Library, EntryPoint = "RmEndSession")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int RmEndSession(uint sessionHandle);

    /// <summary>
    /// RM_PROCESS_INFO. The process's start time, a FILETIME, is two 32-bit halves, which
    /// keeps the struct on the 4-byte alignment the native one has.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct RM_PROCESS_INFO
    {
        public uint ProcessId;
        public uint StartTimeLow;
        public uint StartTimeHigh;
        public fixed char AppName[MaxAppNameLength + 1];
        public fixed char ServiceShortName[MaxServiceNameLength + 1];
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        public int Restartable;
    }
}
