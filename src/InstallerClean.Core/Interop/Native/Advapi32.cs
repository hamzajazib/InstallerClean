using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace InstallerClean.Interop.Native;

/// <summary>
/// P/Invoke surface for advapi32.dll, through the same source-generated
/// <see cref="LibraryImportAttribute"/> stubs as <see cref="Kernel32"/>.
/// </summary>
internal static partial class Advapi32
{
    private const string Library = "advapi32.dll";

    /// <summary>
    /// Builds a self-relative security descriptor from its SDDL text. The descriptor
    /// is allocated with LocalAlloc and the caller frees it with
    /// <see cref="Kernel32.LocalFree"/>.
    /// </summary>
    [LibraryImport(Library, EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW",
                   SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string stringSecurityDescriptor,
        uint stringSdRevision,
        out IntPtr securityDescriptor,
        IntPtr securityDescriptorSize);

    /// <summary>The only revision the SDDL conversion takes.</summary>
    public const uint SDDL_REVISION_1 = 1;
}
