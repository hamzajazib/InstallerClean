using System.IO.Abstractions;
using InstallerClean.Helpers;

namespace InstallerClean.Services;

/// <summary>
/// The three acts a Move makes inside the folder the user chose for it: a write test,
/// the discarding of a copy a half-completed move left, and the removal of a folder the
/// Move created. The user may pick a folder another account can write, so each act is
/// reached in a way that a folder swapped in under the same name part way through cannot
/// redirect. <see cref="HandleDestinationGuard"/> is that way in the app;
/// <see cref="ByPathDestinationGuard"/> is the test-seam default, where the real
/// filesystem is substituted.
/// </summary>
public interface IDestinationGuard
{
    /// <summary>Whether the folder accepts a file and gives it back.</summary>
    bool TryProbeWritable(string folder);

    /// <summary>
    /// Removes the file at <paramref name="destinationPath"/>, where it is still there,
    /// so a file reported as failed is one the user has exactly one of. Answers true
    /// where it is gone, false where it is present and would not go.
    /// </summary>
    bool TryDiscardCopy(string destinationPath);

    /// <summary>Removes <paramref name="folder"/> where it is empty.</summary>
    bool TryRemoveEmptyFolder(string folder);
}

/// <summary>
/// Reaches each act through a handle opened on the destination folder, so the act lands
/// on the folder the handle names and not on a folder put in its place under the same
/// name. This is what the app uses.
/// </summary>
internal sealed class HandleDestinationGuard : IDestinationGuard
{
    public bool TryProbeWritable(string folder) => DirectoryGuard.TryProbeWritable(folder);

    public bool TryDiscardCopy(string destinationPath)
    {
        var folder = Path.GetDirectoryName(destinationPath);
        var leaf = Path.GetFileName(destinationPath);
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(leaf)) return false;
        return DirectoryGuard.TryDiscardChildCopy(folder, leaf);
    }

    public bool TryRemoveEmptyFolder(string folder)
    {
        var parent = Path.GetDirectoryName(folder);
        var leaf = Path.GetFileName(folder);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return false;
        using var parentHandle = DirectoryGuard.OpenDirectoryNoFollow(parent);
        return parentHandle is not null && DirectoryGuard.TryRemoveEmptyChildFolder(parentHandle, leaf);
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

    public bool TryProbeWritable(string folder)
    {
        var probe = _fs.Path.Combine(folder, _fs.Path.GetRandomFileName());
        try
        {
            _fs.File.WriteAllBytes(probe, Array.Empty<byte>());
            _fs.File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryDiscardCopy(string destinationPath)
    {
        try
        {
            if (!_fs.File.Exists(destinationPath)) return true;
            var attributes = _fs.File.GetAttributes(destinationPath);
            if (attributes.HasFlag(FileAttributes.ReadOnly))
                _fs.File.SetAttributes(destinationPath, attributes & ~FileAttributes.ReadOnly);
            _fs.File.Delete(destinationPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryRemoveEmptyFolder(string folder)
    {
        try
        {
            _fs.Directory.Delete(folder, recursive: false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
