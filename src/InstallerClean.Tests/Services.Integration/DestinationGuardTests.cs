using InstallerClean.Interop.Native;
using InstallerClean.Services;

namespace InstallerClean.Tests.Services.Integration;

/// <summary>
/// The handle-based destination guard against a real folder, since its write test,
/// discard and folder removal all reach the disk through handles and a MockFileSystem
/// cannot stand in for that. These witness the ordinary outcomes; the guarantee the
/// handles carry, that a folder swapped in under the destination's name mid-operation
/// cannot steer an act to another file, is the same one the staging-folder clean-up
/// rests on and is argued at the code.
/// </summary>
public sealed class DestinationGuardTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ic-dest-").FullName;
    private readonly IDestinationGuard _guard = new HandleDestinationGuard();

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void A_writable_folder_passes_the_probe_and_nothing_is_left_behind()
    {
        Assert.True(_guard.TryProbeWritable(_root, out _, out var refusal));
        Assert.Equal(WriteProbeRefusal.None, refusal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public void A_folder_that_is_not_there_fails_the_probe_with_a_cause()
    {
        Assert.False(_guard.TryProbeWritable(Path.Combine(_root, "missing"), out var code, out var refusal));
        Assert.NotEqual(0, code);
        Assert.Equal(WriteProbeRefusal.FolderOpen, refusal);
    }

    [Fact]
    public void A_path_that_names_a_file_fails_the_probe_at_the_folder_with_ERROR_DIRECTORY()
    {
        var file = Path.Combine(_root, "not-a-folder");
        File.WriteAllBytes(file, []);

        Assert.False(_guard.TryProbeWritable(file, out var code, out var refusal));
        Assert.Equal(Kernel32.ERROR_DIRECTORY, code);
        Assert.Equal(WriteProbeRefusal.FolderOpen, refusal);
    }

    [Fact]
    public void A_copy_is_discarded_through_the_handle_even_when_read_only()
    {
        var copy = Path.Combine(_root, "payload.msi");
        File.WriteAllText(copy, "content");
        File.SetAttributes(copy, FileAttributes.ReadOnly);

        Assert.True(_guard.TryDiscardCopy(copy, out _));
        Assert.False(File.Exists(copy));
    }

    [Fact]
    public void Discarding_a_copy_that_was_never_made_answers_that_it_is_gone()
    {
        Assert.True(_guard.TryDiscardCopy(Path.Combine(_root, "never-written.msi"), out _));
    }

    [Fact]
    public void A_destination_that_is_a_directory_link_passes_the_probe()
    {
        // A Move destination the user chose can be a volume mount point or a directory
        // junction, which the move's own path resolution and the file move both follow.
        // The probe follows the folder's own link to the directory it stands for, so
        // such a destination is accepted rather than refused. Best-effort: a link needs
        // a privilege not every host grants.
        var target = Directory.CreateDirectory(Path.Combine(_root, "real")).FullName;
        var linkedDestination = Path.Combine(_root, "mounted");
        try
        {
            Directory.CreateSymbolicLink(linkedDestination, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        Assert.True(_guard.TryProbeWritable(linkedDestination, out _, out var refusal));
        Assert.Equal(WriteProbeRefusal.None, refusal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
    }

    [Fact]
    public void An_empty_created_folder_is_removed_and_a_folder_with_a_file_is_left()
    {
        var empty = Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName;
        Assert.True(_guard.TryRemoveEmptyFolder(empty));
        Assert.False(Directory.Exists(empty));

        var full = Directory.CreateDirectory(Path.Combine(_root, "full")).FullName;
        File.WriteAllText(Path.Combine(full, "keep.txt"), "x");
        Assert.False(_guard.TryRemoveEmptyFolder(full));
        Assert.True(Directory.Exists(full));
    }

    [Fact]
    public void A_copy_name_swapped_for_a_link_is_not_followed_to_its_target()
    {
        // The real destination holds a child named like the copy, but that child is a
        // link to a file elsewhere. The discard opens the child relative to a handle on
        // the destination folder without following the child's link, finds a reparse
        // point and leaves it, so the file the link points at is untouched. Best-effort:
        // a link needs a privilege not every host grants.
        var outside = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
        var decoy = Path.Combine(outside, "decoy.txt");
        File.WriteAllText(decoy, "untouched");

        var destination = Directory.CreateDirectory(Path.Combine(_root, "destination")).FullName;
        var copyName = Path.Combine(destination, "payload.msi");
        try
        {
            File.CreateSymbolicLink(copyName, decoy);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        _guard.TryDiscardCopy(copyName, out _);

        Assert.True(File.Exists(decoy));
    }
}
