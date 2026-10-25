using System.ComponentModel;
using System.Runtime.InteropServices;
using InstallerClean.Helpers;
using InstallerClean.Interop.Native;

namespace InstallerClean.Services;

/// <summary>
/// The folder an update's download is written to before it runs: a new folder
/// straight under %ProgramData%, which only Administrators and SYSTEM can change.
/// </summary>
/// <remarks>
/// The app runs elevated, and it runs the file it downloads, so the file must sit
/// where a process without administrator rights cannot swap it between the write and
/// the start. %ProgramData% lets any user create a folder in it and keeps them out of
/// folders others made, so the folder is given its own descriptor as it is created,
/// in the same call, rather than a moment later. That descriptor is protected, so
/// nothing from %ProgramData% is inherited, and everything written inside inherits it.
/// A name that already exists fails the create, so a folder somebody else made can
/// never be taken for this one. Keep it straight under %ProgramData%: a folder deeper
/// down is only as safe as every folder above it, and any user can make those first.
/// </remarks>
public sealed class UpdateStagingFolder
{
    /// <summary>What every staging folder's name starts with, before 32 hex digits.</summary>
    internal const string NamePrefix = "InstallerClean-update-";

    /// <summary>
    /// Administrators as the owner, full control for SYSTEM and for Administrators on
    /// the folder and inherited by every file and folder inside it, and nothing for
    /// anybody else. "P" protects the list from the parent's inheritable entries. The
    /// owner is fixed to Administrators, rather than left to the creating account's
    /// default, so that a folder the app made is told from one another account made by
    /// an owner no account without administrator rights can set.
    /// </summary>
    internal const string AdministratorsOnly = "O:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)";

    private UpdateStagingFolder(string path) => FullPath = path;

    /// <summary>The folder's full path.</summary>
    public string FullPath { get; }

    /// <summary>
    /// The folder under the PC's %ProgramData%, the one the app uses.
    /// </summary>
    public static string DefaultRoot =>
        KnownFolders.GetPath(Environment.SpecialFolder.CommonApplicationData);

    /// <summary>
    /// Creates a new staging folder under <paramref name="root"/>, named
    /// <see cref="NamePrefix"/> and a new GUID. Throws <see cref="Win32Exception"/>
    /// where Windows refuses the descriptor or the folder, the name already being
    /// taken among them, and <see cref="IOException"/> where the root cannot be found.
    /// </summary>
    public static UpdateStagingFolder Create(string root) => Create(root, AdministratorsOnly);

    /// <summary>
    /// Test seam: the same, with the descriptor given as SDDL. The suite runs without
    /// administrator rights, so a test that writes into the folder adds its own
    /// account to <see cref="AdministratorsOnly"/>.
    /// </summary>
    internal static UpdateStagingFolder Create(string root, string descriptorSddl)
    {
        if (!Directory.Exists(root))
            throw new IOException("The folder to create the update's folder in does not exist.");

        var path = Path.Combine(root, NamePrefix + Guids.NewGuid().ToString("N"));

        if (!Advapi32.ConvertStringSecurityDescriptorToSecurityDescriptor(
                descriptorSddl, Advapi32.SDDL_REVISION_1, out var descriptor, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        try
        {
            var attributes = Kernel32.SECURITY_ATTRIBUTES.With(descriptor);
            if (!Kernel32.CreateDirectory(path, in attributes))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        finally
        {
            Kernel32.LocalFree(descriptor);
        }
        return new UpdateStagingFolder(path);
    }

    /// <summary>
    /// Removes this folder where it is empty, as it is once the download has been
    /// moved out of it. Leaves it where anything is still inside, and never throws:
    /// <see cref="RemoveLeftovers(string)"/> takes what is left at a later start.
    /// </summary>
    public void TryRemoveIfEmpty()
    {
        try
        {
            Directory.Delete(FullPath, recursive: false);
        }
        catch
        {
            // Not empty, or already gone; either way there is nothing to do now.
        }
    }

    /// <summary>
    /// Removes the staging folders earlier updates left under <paramref name="root"/>,
    /// as the setup's leaves one: the setup is still running from it when the app
    /// closes, and still holds its own file open as it opens the app again. Answers
    /// whether none of this app's folders is left, so the caller can try again while
    /// that setup finishes. Never throws.
    /// </summary>
    /// <remarks>
    /// Any user can create a folder with this name in %ProgramData%, so nothing found
    /// is trusted and nothing is reached by its path across a check. The root is opened
    /// as a handle, and each candidate folder is opened without following a link in its
    /// name; a folder is cleaned only where its owner and list are the app's own, as
    /// <see cref="Create(string)"/> sets them, so a folder another account made is left whole.
    /// Inside a folder that is the app's own, a file is removed through a handle opened
    /// relative to the folder's, and only where its name has the downloads' shape,
    /// "InstallerClean-" to ".exe", and it is a plain file; the folder itself goes,
    /// relative to the root's handle, only once it is empty.
    /// </remarks>
    public static bool RemoveLeftovers(string root) =>
        RemoveLeftovers(root, DirectoryGuard.FolderExpectation.AppOwned);

    /// <summary>
    /// Test seam: the same, against the owner and list a test's folder carries. The
    /// suite runs without administrator rights, so it cannot create a folder owned by
    /// Administrators or SYSTEM and names its own account in <paramref name="expected"/>.
    /// </summary>
    internal static bool RemoveLeftovers(string root, DirectoryGuard.FolderExpectation expected)
    {
        try
        {
            if (!Directory.Exists(root)) return true;

            using var rootHandle = DirectoryGuard.OpenDirectoryNoFollow(root);
            if (rootHandle is null) return false;

            var allGone = true;
            foreach (var folder in Directory.EnumerateDirectories(root, NamePrefix + "*"))
            {
                var leaf = Path.GetFileName(folder);
                try
                {
                    if (!IsOurName(leaf)) continue;

                    using var folderHandle = DirectoryGuard.OpenDirectoryNoFollow(folder);
                    if (folderHandle is null || !DirectoryGuard.IsExpectedFolder(folderHandle, expected))
                        continue;

                    var cleared = true;
                    foreach (var file in Directory.EnumerateFiles(folder, "InstallerClean-*.exe"))
                    {
                        if (!DirectoryGuard.TryRemoveChildFile(folderHandle, Path.GetFileName(file)))
                            cleared = false;
                    }

                    if (!cleared || !DirectoryGuard.TryRemoveEmptyChildFolder(rootHandle, leaf))
                        allGone = false;
                }
                catch
                {
                    // One folder that will not go does not stop the rest.
                    allGone = false;
                }
            }
            return allGone;
        }
        catch
        {
            // Nothing here is worth a failed start.
            return false;
        }
    }

    private static bool IsOurName(string name) =>
        name.Length == NamePrefix.Length + 32
        && name.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase)
        && name.AsSpan(NamePrefix.Length).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;
}
