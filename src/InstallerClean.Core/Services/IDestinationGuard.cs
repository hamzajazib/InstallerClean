using System.IO.Abstractions;
using InstallerClean.Helpers;
using InstallerClean.Interop.Native;

namespace InstallerClean.Services;

/// <summary>
/// The three acts a Move makes inside the folder the user chose for it: a write test,
/// the discarding of a copy a half-completed move left, and the removal of a folder the
/// Move created. The user may pick a folder another account can write, so each act is
/// reached in a way that a folder swapped in under the same name part way through cannot
/// redirect. The removal reaches the folder through every folder the Move made above it,
/// each opened without following a link, so a link put in place of any of them refuses
/// the removal. <see cref="HandleDestinationGuard"/> is that way in the app;
/// <see cref="ByPathDestinationGuard"/> is the one the test seams use, where the real
/// filesystem is substituted.
/// </summary>
public interface IDestinationGuard
{
    /// <summary>
    /// Whether the folder accepts a new file and lets it be read back, with the Win32
    /// cause of a refusal in <paramref name="failureCode"/> for a caller to record and
    /// the step that refused in <paramref name="refusal"/>, so a caller names a cause
    /// only where the probe established it. A folder whose files can be read and not
    /// deleted passes.
    /// </summary>
    bool TryProbeWritable(string folder, out int failureCode, out WriteProbeRefusal refusal);

    /// <summary>
    /// Removes the file at <paramref name="destinationPath"/>, where it is still there,
    /// so a file reported as failed is one the user has exactly one of. Answers true
    /// where it is gone, false where it is present and would not go, with the Win32
    /// cause in <paramref name="failureCode"/>. A folder at the name is left and
    /// answered false with ERROR_DIRECTORY_NOT_SUPPORTED.
    /// </summary>
    bool TryDiscardCopy(string destinationPath, out int failureCode);

    /// <summary>
    /// Removes <paramref name="folder"/> where it is empty. <paramref name="existingAncestor"/>
    /// is the nearest folder above it that was there before the Move: the folder the user
    /// chose, or the one the chosen path leads to, which may be a volume mount point or a
    /// directory junction and is followed to the directory it stands for. Every folder
    /// between it and <paramref name="folder"/> is one the Move made, and each is reached
    /// from the one above without following a link, so a link in the place of any of them
    /// answers false and removes nothing. Answers false too where the folder is not empty.
    /// </summary>
    bool TryRemoveEmptyFolder(string folder, string existingAncestor);
}

/// <summary>
/// Reaches each act through a handle opened on the destination folder, so the act lands
/// on the folder the handle names and not on a folder put in its place under the same
/// name. This is what the app uses.
/// </summary>
internal sealed class HandleDestinationGuard : IDestinationGuard
{
    public bool TryProbeWritable(string folder, out int failureCode, out WriteProbeRefusal refusal) =>
        DirectoryGuard.TryProbeWritable(folder, out failureCode, out refusal);

    public bool TryDiscardCopy(string destinationPath, out int failureCode)
    {
        failureCode = 0;
        var folder = Path.GetDirectoryName(destinationPath);
        var leaf = Path.GetFileName(destinationPath);
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(leaf)) return false;
        return DirectoryGuard.TryDiscardChildCopy(folder, leaf, out failureCode);
    }

    public bool TryRemoveEmptyFolder(string folder, string existingAncestor)
    {
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(existingAncestor)) return false;

        var relative = Path.GetRelativePath(existingAncestor, folder);
        if (Path.IsPathRooted(relative)) return false;
        var names = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (names.Length == 0 || names.Any(name => name is "." or "..")) return false;

        using var ancestor = DirectoryGuard.OpenDirectoryFollow(existingAncestor, out _);
        return ancestor is not null && DirectoryGuard.TryRemoveEmptyDescendantFolder(ancestor, names);
    }
}

/// <summary>
/// Acts by path through an injected <see cref="IFileSystem"/>, for the test seam where
/// that filesystem is a substitute. The app never uses it: the production path takes
/// <see cref="HandleDestinationGuard"/>, which a wiring test holds.
/// </summary>
internal sealed class ByPathDestinationGuard(IFileSystem fileSystem) : IDestinationGuard
{
    private readonly IFileSystem _fs = fileSystem;

    /// <summary>
    /// Writes an empty file, reads it back and deletes it, giving the answers the app's
    /// guard gives: a refused write or read fails the probe, an
    /// <see cref="UnauthorizedAccessException"/> naming the step as refused access and
    /// any other exception as a failure of it, and a refused delete does not fail the
    /// probe, since a folder whose files can be read and not deleted passes there too.
    /// There is no folder to open, so <see cref="WriteProbeRefusal.FolderOpen"/> is
    /// never given.
    /// </summary>
    public bool TryProbeWritable(string folder, out int failureCode, out WriteProbeRefusal refusal)
    {
        failureCode = 0;
        refusal = WriteProbeRefusal.None;
        var probe = _fs.Path.Combine(folder, _fs.Path.GetRandomFileName());
        try
        {
            _fs.File.WriteAllBytes(probe, Array.Empty<byte>());
        }
        catch (Exception ex)
        {
            failureCode = ex.HResult;
            refusal = ex is UnauthorizedAccessException
                ? WriteProbeRefusal.CreateRefusedAccess : WriteProbeRefusal.CreateFailed;
            return false;
        }

        try
        {
            _fs.File.ReadAllBytes(probe);
            return true;
        }
        catch (Exception ex)
        {
            failureCode = ex.HResult;
            refusal = ex is UnauthorizedAccessException
                ? WriteProbeRefusal.ReadBackRefusedAccess : WriteProbeRefusal.ReadBackFailed;
            return false;
        }
        finally
        {
            try { _fs.File.Delete(probe); } catch { }
        }
    }

    public bool TryDiscardCopy(string destinationPath, out int failureCode)
    {
        failureCode = 0;
        try
        {
            if (!_fs.File.Exists(destinationPath))
            {
                if (!_fs.Directory.Exists(destinationPath)) return true;
                failureCode = Kernel32.ERROR_DIRECTORY_NOT_SUPPORTED;
                return false;
            }
            var attributes = _fs.File.GetAttributes(destinationPath);
            var readOnly = attributes.HasFlag(FileAttributes.ReadOnly);
            if (readOnly)
                _fs.File.SetAttributes(destinationPath, attributes & ~FileAttributes.ReadOnly);
            try
            {
                _fs.File.Delete(destinationPath);
            }
            catch
            {
                // The copy stays as it was, read-only included.
                if (readOnly)
                {
                    try { _fs.File.SetAttributes(destinationPath, attributes); } catch { }
                }
                throw;
            }
            return true;
        }
        catch (Exception ex)
        {
            failureCode = ex.HResult;
            return false;
        }
    }

    public bool TryRemoveEmptyFolder(string folder, string existingAncestor)
    {
        try
        {
            // The one-argument Delete is the non-recursive removal: a folder holding anything is refused.
            _fs.Directory.Delete(folder);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
