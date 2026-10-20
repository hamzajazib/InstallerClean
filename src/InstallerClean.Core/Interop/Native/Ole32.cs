using System.Runtime.InteropServices;

namespace InstallerClean.Interop.Native;

/// <summary>
/// P/Invoke surface for ole32.dll's task memory and GUIDs, through the same
/// source-generated <see cref="LibraryImportAttribute"/> stubs as <see cref="Kernel32"/>.
/// </summary>
/// <remarks>
/// Memory a Windows call hands back for the caller to free is freed here rather than
/// with <see cref="Marshal.FreeCoTaskMem"/>, and a new GUID is made here rather than
/// with <see cref="Guid.NewGuid"/>. Both of those call ole32 through .NET's own imports,
/// and those imports search for ole32 without going through this assembly's resolver.
/// </remarks>
internal static partial class Ole32
{
    private const string Library = "ole32.dll";

    /// <summary>Frees memory allocated with CoTaskMemAlloc. A null pointer is ignored.</summary>
    [LibraryImport(Library, EntryPoint = "CoTaskMemFree")]
    public static partial void CoTaskMemFree(IntPtr pv);

    /// <summary>Makes a new GUID. Returns an HRESULT, zero (S_OK) where it made one.</summary>
    [LibraryImport(Library, EntryPoint = "CoCreateGuid")]
    public static partial int CoCreateGuid(out Guid guid);
}
