using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using InstallerClean.Interop.Native;

namespace InstallerClean.Helpers;

/// <summary>
/// Opens a folder and reaches the children inside it through handles rather than
/// paths, for a folder that sits where another account can add entries. The app runs
/// elevated, so a delete it makes by a path lands wherever the path leads; another
/// account that can write the folder can, between the moment the path is checked and
/// the moment it is used, put a link in the folder's place whose last component carries
/// a child's name and points anywhere. Reached through a handle instead, a child is
/// resolved against the directory the handle already names, so a folder swapped in
/// under the same name afterwards is not on the route the kernel walks.
/// </summary>
/// <remarks>
/// Three steps make that hold. A folder is opened with
/// <see cref="Kernel32.FILE_FLAG_OPEN_REPARSE_POINT"/>, so a link put in its place
/// opens as the link and is refused, never followed. Its owner and list are read off
/// that handle and checked against what the caller expects, so a folder another account
/// made is told apart from the app's own. Each child is opened relative to the folder
/// handle through <see cref="Ntdll.NtCreateFile"/> and removed through its own handle,
/// so no name is resolved against a path a second time.
/// </remarks>
internal static class DirectoryGuard
{
    /// <summary>The SID of SYSTEM, one of the two the app's own folders grant.</summary>
    internal const string SystemSid = "S-1-5-18";

    /// <summary>The SID of the Administrators group, the other.</summary>
    internal const string AdministratorsSid = "S-1-5-32-544";

    /// <summary>
    /// What a folder's owner and list must be for it to count as the app's own: an
    /// owner drawn from <see cref="OwnerSids"/>, a list naming only SIDs from
    /// <see cref="DaclSids"/>, and, where <see cref="RequireProtected"/>, a list sealed
    /// against inheritance. The two sets are SYSTEM and Administrators for a real folder;
    /// a test that cannot create a folder owned by either adds its own account.
    /// </summary>
    internal readonly struct FolderExpectation
    {
        internal FolderExpectation(IReadOnlySet<string> ownerSids, IReadOnlySet<string> daclSids, bool requireProtected)
        {
            OwnerSids = ownerSids;
            DaclSids = daclSids;
            RequireProtected = requireProtected;
        }

        internal IReadOnlySet<string> OwnerSids { get; }
        internal IReadOnlySet<string> DaclSids { get; }
        internal bool RequireProtected { get; }

        /// <summary>A real folder: owned by SYSTEM or Administrators, granting only those two, sealed.</summary>
        internal static FolderExpectation AppOwned { get; } = new(
            new HashSet<string> { SystemSid, AdministratorsSid },
            new HashSet<string> { SystemSid, AdministratorsSid },
            requireProtected: true);
    }

    /// <summary>
    /// Opens <paramref name="path"/> as a directory handle without following a link in
    /// its final component, and returns it only where what opened is a real directory.
    /// Null where the open fails, the attributes cannot be read, or what opened is a
    /// reparse point or not a directory.
    /// </summary>
    internal static SafeFileHandle? OpenDirectoryNoFollow(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        var handle = Kernel32.CreateFile(
            path, Kernel32.GENERIC_READ, Kernel32.FILE_SHARE_ALL, IntPtr.Zero,
            Kernel32.OPEN_EXISTING,
            Kernel32.FILE_FLAG_OPEN_REPARSE_POINT | Kernel32.FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);
        if (handle.IsInvalid) return null;

        if (!Kernel32.GetFileInformationByHandle(handle, out var info)
            || (info.dwFileAttributes & Kernel32.FILE_ATTRIBUTE_REPARSE_POINT) != 0
            || (info.dwFileAttributes & Kernel32.FILE_ATTRIBUTE_DIRECTORY) == 0)
        {
            handle.Dispose();
            return null;
        }
        return handle;
    }

    /// <summary>
    /// Whether the folder <paramref name="handle"/> names has the owner and list
    /// <paramref name="expected"/> describes. False on any read failure or any
    /// mismatch, so a folder that cannot be vouched for is never acted on.
    /// </summary>
    internal static bool IsExpectedFolder(SafeFileHandle handle, FolderExpectation expected)
    {
        var rc = Advapi32.GetSecurityInfo(
            handle, Advapi32.SE_FILE_OBJECT,
            Advapi32.OWNER_SECURITY_INFORMATION | Advapi32.DACL_SECURITY_INFORMATION,
            out var owner, out _, out var dacl, out _, out var descriptor);
        if (rc != 0) return false;

        try
        {
            var ownerSid = SidToString(owner);
            if (ownerSid is null || !expected.OwnerSids.Contains(ownerSid)) return false;

            if (!Advapi32.GetSecurityDescriptorControl(descriptor, out var control, out _)) return false;
            if ((control & Advapi32.SE_DACL_PRESENT) == 0) return false;
            if (expected.RequireProtected && (control & Advapi32.SE_DACL_PROTECTED) == 0) return false;

            // A null list pointer grants everyone, so it is never the app's own.
            if (dacl == IntPtr.Zero) return false;
            return EveryAceSidIsExpected(dacl, expected.DaclSids);
        }
        finally
        {
            if (descriptor != IntPtr.Zero) Kernel32.LocalFree(descriptor);
        }
    }

    private static unsafe bool EveryAceSidIsExpected(IntPtr dacl, IReadOnlySet<string> allowed)
    {
        // The ACL header carries its entry count in its third field; each entry begins
        // with a four-byte header, then a four-byte mask, then the SID. An entry whose
        // type is neither "allow" nor "deny" has a different shape, so a folder carrying
        // one is not read further and not taken for the app's own. A list with no entries
        // grants nobody and is not how the app's own folders are made, so it is refused.
        ushort aceCount = *(ushort*)((byte*)dacl + 4);
        if (aceCount == 0) return false;
        for (uint i = 0; i < aceCount; i++)
        {
            if (!Advapi32.GetAce(dacl, i, out var ace)) return false;
            var aceType = *(byte*)ace;
            if (aceType > 1) return false;
            var sid = SidToString(ace + 8);
            if (sid is null || !allowed.Contains(sid)) return false;
        }
        return true;
    }

    private static string? SidToString(IntPtr sid)
    {
        if (sid == IntPtr.Zero) return null;
        if (!Advapi32.ConvertSidToStringSid(sid, out var text)) return null;
        try
        {
            return Marshal.PtrToStringUni(text);
        }
        finally
        {
            Kernel32.LocalFree(text);
        }
    }

    /// <summary>
    /// Opens the child <paramref name="leaf"/> inside the directory
    /// <paramref name="directory"/> names, resolved against that handle and never a
    /// path, without following a link in <paramref name="leaf"/>. Null on any failure.
    /// </summary>
    internal static unsafe SafeFileHandle? OpenChildNoFollow(
        SafeFileHandle directory, string leaf, uint access, uint disposition, uint options)
    {
        if (string.IsNullOrEmpty(leaf)) return null;

        var refTaken = false;
        try
        {
            directory.DangerousAddRef(ref refTaken);
            var root = directory.DangerousGetHandle();

            fixed (char* buffer = leaf)
            {
                var name = new Ntdll.UNICODE_STRING
                {
                    Length = (ushort)(leaf.Length * sizeof(char)),
                    MaximumLength = (ushort)(leaf.Length * sizeof(char)),
                    Buffer = (IntPtr)buffer,
                };
                var attributes = new Ntdll.OBJECT_ATTRIBUTES
                {
                    Length = (uint)sizeof(Ntdll.OBJECT_ATTRIBUTES),
                    RootDirectory = root,
                    ObjectName = (IntPtr)(&name),
                    Attributes = Ntdll.OBJ_CASE_INSENSITIVE,
                    SecurityDescriptor = IntPtr.Zero,
                    SecurityQualityOfService = IntPtr.Zero,
                };

                var status = Ntdll.NtCreateFile(
                    out var child, access | Ntdll.SYNCHRONIZE, in attributes, out _, IntPtr.Zero,
                    0, Kernel32.FILE_SHARE_ALL, disposition,
                    options | Ntdll.FILE_SYNCHRONOUS_IO_NONALERT, IntPtr.Zero, 0);
                return status < 0 ? null : new SafeFileHandle(child, ownsHandle: true);
            }
        }
        finally
        {
            if (refTaken) directory.DangerousRelease();
        }
    }

    /// <summary>
    /// Removes the plain file <paramref name="leaf"/> inside <paramref name="directory"/>
    /// through a handle. Leaves, and answers false, where the child cannot be opened,
    /// its attributes cannot be read, or it is a reparse point; removes it and answers
    /// true otherwise.
    /// </summary>
    internal static bool TryRemoveChildFile(SafeFileHandle directory, string leaf)
    {
        using var child = OpenChildNoFollow(
            directory, leaf,
            Ntdll.DELETE | Kernel32.FILE_READ_ATTRIBUTES, Ntdll.FILE_OPEN,
            Ntdll.FILE_OPEN_REPARSE_POINT | Ntdll.FILE_NON_DIRECTORY_FILE);
        if (child is null) return false;

        if (!Kernel32.GetFileInformationByHandle(child, out var info)
            || (info.dwFileAttributes & Kernel32.FILE_ATTRIBUTE_REPARSE_POINT) != 0)
            return false;

        return DeleteByHandle(child);
    }

    /// <summary>
    /// Removes the child folder <paramref name="leaf"/> inside <paramref name="directory"/>
    /// through a handle, where it is empty. Answers false where it cannot be opened or
    /// anything is still inside it.
    /// </summary>
    internal static bool TryRemoveEmptyChildFolder(SafeFileHandle directory, string leaf)
    {
        using var child = OpenChildNoFollow(
            directory, leaf,
            Ntdll.DELETE | Kernel32.FILE_READ_ATTRIBUTES, Ntdll.FILE_OPEN,
            Ntdll.FILE_OPEN_REPARSE_POINT | Ntdll.FILE_DIRECTORY_FILE);
        if (child is null) return false;

        if (!Kernel32.GetFileInformationByHandle(child, out var info)
            || (info.dwFileAttributes & Kernel32.FILE_ATTRIBUTE_REPARSE_POINT) != 0)
            return false;

        return DeleteByHandle(child);
    }

    private static unsafe bool DeleteByHandle(SafeFileHandle handle)
    {
        var posix = new Kernel32.FILE_DISPOSITION_INFO_EX
        {
            Flags = Kernel32.FILE_DISPOSITION_FLAG_DELETE | Kernel32.FILE_DISPOSITION_FLAG_POSIX_SEMANTICS,
        };
        if (Kernel32.SetFileDispositionInfoEx(
                handle, Kernel32.FileDispositionInfoEx, in posix, (uint)sizeof(Kernel32.FILE_DISPOSITION_INFO_EX)))
            return true;

        // A Windows build that does not take the POSIX form answers
        // ERROR_INVALID_PARAMETER; the older form marks the name to go as the last
        // handle closes, which here is at once.
        if (Marshal.GetLastPInvokeError() != Kernel32.ERROR_INVALID_PARAMETER) return false;

        var plain = new Kernel32.FILE_DISPOSITION_INFO { DeleteFile = 1 };
        return Kernel32.SetFileDispositionInfo(
            handle, Kernel32.FileDispositionInfo, in plain, (uint)sizeof(Kernel32.FILE_DISPOSITION_INFO));
    }
}
