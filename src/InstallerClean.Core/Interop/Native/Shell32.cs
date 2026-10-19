using System.Runtime.InteropServices;

namespace InstallerClean.Interop.Native;

/// <summary>
/// P/Invoke surface for shell32.dll, through the same source-generated
/// <see cref="LibraryImportAttribute"/> stubs as <see cref="Kernel32"/>.
/// Consumed by <see cref="Helpers.KnownFolders"/>.
/// </summary>
internal static partial class Shell32
{
    private const string Library = "shell32.dll";

    /// <summary>
    /// The path of the known folder <paramref name="rfid"/> names. Returns S_OK
    /// or an error HRESULT. <paramref name="ppszPath"/> is allocated with
    /// CoTaskMemAlloc and the caller frees it with <see cref="Ole32.CoTaskMemFree"/>
    /// whether the call succeeds or not.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "SHGetKnownFolderPath")]
    public static partial int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
