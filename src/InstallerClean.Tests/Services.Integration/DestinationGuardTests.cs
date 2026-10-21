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
        Assert.True(_guard.TryProbeWritable(_root));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public void A_folder_that_is_not_there_fails_the_probe()
    {
        Assert.False(_guard.TryProbeWritable(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void A_copy_is_discarded_through_the_handle_even_when_read_only()
    {
        var copy = Path.Combine(_root, "payload.msi");
        File.WriteAllText(copy, "content");
        File.SetAttributes(copy, FileAttributes.ReadOnly);

        Assert.True(_guard.TryDiscardCopy(copy));
        Assert.False(File.Exists(copy));
    }

    [Fact]
    public void Discarding_a_copy_that_was_never_made_answers_that_it_is_gone()
    {
        Assert.True(_guard.TryDiscardCopy(Path.Combine(_root, "never-written.msi")));
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
    public void A_link_in_the_destinations_place_does_not_let_the_discard_reach_its_target()
    {
        // The destination is a link to another folder holding a file named like the
        // copy. The discard opens the leaf relative to a handle on the link's own
        // directory without following the link, so the file behind the link is left.
        // Best-effort: creating a link needs a privilege not every host grants.
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        var behindLink = Path.Combine(target, "payload.msi");
        File.WriteAllText(behindLink, "untouched");

        var linkedDestination = Path.Combine(_root, "destination");
        try
        {
            Directory.CreateSymbolicLink(linkedDestination, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // this host cannot create links; the guarantee holds by construction
        }

        _guard.TryDiscardCopy(Path.Combine(linkedDestination, "payload.msi"));

        Assert.True(File.Exists(behindLink));
    }
}
