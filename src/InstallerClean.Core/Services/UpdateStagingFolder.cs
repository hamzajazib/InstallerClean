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
    /// Full control for SYSTEM and for Administrators, on the folder and inherited by
    /// every file and folder inside it, and nothing for anybody else. "P" protects the
    /// list from the parent's inheritable entries.
    /// </summary>
    internal const string AdministratorsOnly = "D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)";

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
            var attributes = new Kernel32.SECURITY_ATTRIBUTES
            {
                nLength = (uint)Marshal.SizeOf<Kernel32.SECURITY_ATTRIBUTES>(),
                lpSecurityDescriptor = descriptor,
                bInheritHandle = 0,
            };
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
    /// <see cref="RemoveLeftovers"/> takes what is left at a later start.
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
    /// is trusted. A folder whose name is not <see cref="NamePrefix"/> and 32 hex
    /// digits is left alone, and so is one that is a reparse point. Inside, a file is
    /// deleted only where its name has the downloads' shape, "InstallerClean-" to
    /// ".exe", and it is a plain file; the folder itself goes only once it is empty,
    /// never with what is inside it.
    /// </remarks>
    public static bool RemoveLeftovers(string root)
    {
        var allGone = true;
        try
        {
            if (!Directory.Exists(root)) return true;
            foreach (var folder in Directory.EnumerateDirectories(root, NamePrefix + "*"))
            {
                try
                {
                    if (!IsOurName(Path.GetFileName(folder)) || IsReparsePoint(folder)) continue;
                    foreach (var file in Directory.EnumerateFiles(folder, "InstallerClean-*.exe"))
                    {
                        if (!IsReparsePoint(file))
                            File.Delete(file);
                    }
                    Directory.Delete(folder, recursive: false);
                }
                catch
                {
                    // One folder that will not go does not stop the rest.
                    allGone = false;
                }
            }
        }
        catch
        {
            // Nothing here is worth a failed start.
            allGone = false;
        }
        return allGone;
    }

    private static bool IsOurName(string name) =>
        name.Length == NamePrefix.Length + 32
        && name.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase)
        && name.AsSpan(NamePrefix.Length).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
