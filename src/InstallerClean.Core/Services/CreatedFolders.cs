using System.IO.Abstractions;

namespace InstallerClean.Services;

/// <summary>
/// The folders a Move makes on the way to the destination it was given. Creating the
/// destination also creates every missing folder above it, so a Move that is then
/// abandoned has more than one folder to take away. Only the folders listed before the
/// create are ever removed, and each only while it is empty.
/// </summary>
internal static class CreatedFolders
{
    /// <summary>
    /// The folders that creating <paramref name="destination"/> would make, the
    /// destination first and the top-most last, each spelled as its full path so a
    /// <c>..</c> in the destination names the folders the create makes. Empty where the
    /// destination is there already. A drive root is never listed.
    /// </summary>
    internal static IReadOnlyList<string> MissingOnPath(IFileSystem fileSystem, string destination)
    {
        var missing = new List<string>();
        var folder = fileSystem.Path.TrimEndingDirectorySeparator(fileSystem.Path.GetFullPath(destination));
        while (!string.IsNullOrEmpty(folder) && !fileSystem.Directory.Exists(folder))
        {
            var parent = fileSystem.Path.GetDirectoryName(folder);
            if (string.IsNullOrEmpty(parent)) break;
            missing.Add(folder);
            folder = parent;
        }
        return missing;
    }

    /// <summary>
    /// Removes <paramref name="folders"/> in the order given, each through
    /// <paramref name="guard"/> and so only where it is empty. A folder that is no longer
    /// there is passed over; the first that is there and will not go ends the removal, so
    /// a folder holding anything keeps every folder above it.
    /// </summary>
    internal static void RemoveWhileEmpty(
        IFileSystem fileSystem, IDestinationGuard guard, IReadOnlyList<string> folders)
    {
        foreach (var folder in folders)
        {
            if (guard.TryRemoveEmptyFolder(folder)) continue;
            if (fileSystem.Directory.Exists(folder)) return;
        }
    }
}
