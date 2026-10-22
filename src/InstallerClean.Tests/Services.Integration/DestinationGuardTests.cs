using System.Runtime.InteropServices;
using InstallerClean.Helpers;
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
    private const int ErrorAccessDenied = 5;

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
    public void A_copy_whose_delete_is_refused_is_left_and_the_cause_the_delete_gave_is_recorded()
    {
        var destination = Directory.CreateDirectory(Path.Combine(_root, "loaded")).FullName;
        var copy = Path.Combine(destination, "payload.dll");

        // A library loaded from the file makes Windows refuse its delete. The discard's open
        // asking for DELETE is granted, so the refusal comes from the delete itself.
        using var image = new LoadedImage(copy);

        var refusal = Record.Exception(() => File.Delete(copy));
        Assert.True(refusal is UnauthorizedAccessException or IOException,
            $"the file with a library loaded from it was not refused a delete (got {refusal?.GetType().Name ?? "no exception"})");
        Assert.True(File.Exists(copy));

        AssertTheDiscardsOpenIsGranted(destination, "payload.dll");

        Assert.False(_guard.TryDiscardCopy(copy, out var code));
        Assert.Equal(ErrorAccessDenied, code);
        Assert.Equal(refusal!.HResult & 0xFFFF, code);
        Assert.True(File.Exists(copy));
    }

    [Fact]
    public void A_read_only_copy_whose_delete_is_refused_keeps_the_attributes_it_had()
    {
        var destination = Directory.CreateDirectory(Path.Combine(_root, "loaded-read-only")).FullName;
        var copy = Path.Combine(destination, "payload.dll");
        using var image = new LoadedImage(copy);
        File.SetAttributes(copy, File.GetAttributes(copy) | FileAttributes.ReadOnly | FileAttributes.Hidden);
        var before = File.GetAttributes(copy);
        AssertTheDiscardsOpenIsGranted(destination, "payload.dll");

        Assert.False(_guard.TryDiscardCopy(copy, out _));

        Assert.Equal(before, File.GetAttributes(copy));
        Assert.True(File.Exists(copy));
    }

    [Fact]
    public void A_read_only_copy_another_program_holds_open_is_released_from_its_name_at_once()
    {
        // The POSIX form lets the name go while a handle that shares delete is still open,
        // so a new file can take the name straight away; the older form would leave the
        // name until that handle closed.
        var destination = Directory.CreateDirectory(Path.Combine(_root, "held-open")).FullName;
        var copy = Path.Combine(destination, "payload.msi");
        File.WriteAllBytes(copy, new byte[16]);
        File.SetAttributes(copy, File.GetAttributes(copy) | FileAttributes.ReadOnly);
        using var holder = new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        Assert.True(_guard.TryDiscardCopy(copy, out _));

        File.WriteAllText(copy, "a new file at the same name");
        Assert.Equal("a new file at the same name", File.ReadAllText(copy));
    }

    [Fact]
    public void A_read_only_copy_is_deleted_by_the_older_form_with_its_attribute_cleared()
    {
        var destination = Directory.CreateDirectory(Path.Combine(_root, "older-form")).FullName;
        var copy = Path.Combine(destination, "payload.msi");
        File.WriteAllBytes(copy, new byte[16]);
        File.SetAttributes(copy, File.GetAttributes(copy) | FileAttributes.ReadOnly);

        using (var folder = DirectoryGuard.OpenDirectoryFollow(destination, out _))
        using (var child = OpenTheCopyAsTheDiscardDoes(folder!, "payload.msi", out var attributes))
        {
            Assert.True(DirectoryGuard.DeleteClearingReadOnly(child, attributes));
        }

        Assert.False(File.Exists(copy));
    }

    [Fact]
    public void A_read_only_copy_the_older_form_cannot_delete_has_its_attribute_put_back_and_the_deletes_cause_kept()
    {
        var destination = Directory.CreateDirectory(Path.Combine(_root, "older-form-refused")).FullName;
        var copy = Path.Combine(destination, "payload.dll");
        using var image = new LoadedImage(copy);
        File.SetAttributes(copy, File.GetAttributes(copy) | FileAttributes.ReadOnly | FileAttributes.Hidden);
        var before = File.GetAttributes(copy);
        // A read-only file is refused its delete whatever else holds it, so the refusal that
        // counts here is the one a file with a library loaded from it gives once it is not
        // read-only; the cause kept is that one.
        File.SetAttributes(copy, before & ~FileAttributes.ReadOnly);
        var refusal = Record.Exception(() => File.Delete(copy));
        Assert.True(refusal is UnauthorizedAccessException or IOException,
            $"the file with a library loaded from it was not refused a delete (got {refusal?.GetType().Name ?? "no exception"})");
        File.SetAttributes(copy, before);

        bool deleted;
        int cause;
        using (var folder = DirectoryGuard.OpenDirectoryFollow(destination, out _))
        using (var child = OpenTheCopyAsTheDiscardDoes(folder!, "payload.dll", out var attributes))
        {
            deleted = DirectoryGuard.DeleteClearingReadOnly(child, attributes);
            cause = Marshal.GetLastPInvokeError();
        }

        Assert.False(deleted);
        Assert.Equal(ErrorAccessDenied, cause);
        Assert.Equal(refusal!.HResult & 0xFFFF, cause);
        Assert.Equal(before, File.GetAttributes(copy));
        Assert.True(File.Exists(copy));
    }

    /// <summary>
    /// A copy of a system library at the path given, loaded into this process until it is
    /// disposed. Windows refuses to delete a file that a library is loaded from.
    /// </summary>
    private sealed class LoadedImage : IDisposable
    {
        private readonly nint _module;

        public LoadedImage(string path)
        {
            File.Copy(Path.Combine(Environment.SystemDirectory, "version.dll"), path);
            _module = NativeLibrary.Load(path);
        }

        public void Dispose() => NativeLibrary.Free(_module);
    }

    private static Microsoft.Win32.SafeHandles.SafeFileHandle OpenTheCopyAsTheDiscardDoes(
        Microsoft.Win32.SafeHandles.SafeFileHandle folder, string leaf, out uint attributes)
    {
        var child = DirectoryGuard.OpenChildNoFollow(
            folder, leaf, DirectoryGuard.DiscardAccess, Ntdll.FILE_OPEN, DirectoryGuard.DiscardOptions);
        Assert.NotNull(child);
        Assert.True(Kernel32.GetFileInformationByHandle(child, out var info));
        attributes = info.dwFileAttributes;
        return child;
    }

    /// <summary>
    /// The discard's own open of the copy, with its access and options, is granted: so the
    /// refusal the discard records comes from the delete and not from the open.
    /// </summary>
    private static void AssertTheDiscardsOpenIsGranted(string destination, string leaf)
    {
        using var folder = DirectoryGuard.OpenDirectoryFollow(destination, out var folderCode);
        Assert.True(folder is not null, $"the folder did not open (Win32 error {folderCode})");
        using var opened = DirectoryGuard.OpenChildNoFollow(
            folder, leaf, DirectoryGuard.DiscardAccess, Ntdll.FILE_OPEN,
            DirectoryGuard.DiscardOptions, out var status);
        Assert.True(opened is not null,
            $"the discard's open was refused (Win32 error {Ntdll.RtlNtStatusToDosError(status)})");
    }

    [Fact]
    public void A_folder_at_the_copy_s_name_is_left_and_recorded_as_a_folder()
    {
        var destination = Directory.CreateDirectory(Path.Combine(_root, "named-folder")).FullName;
        var copyName = Directory.CreateDirectory(Path.Combine(destination, "payload.msi")).FullName;

        Assert.False(_guard.TryDiscardCopy(copyName, out var code));

        Assert.Equal(Kernel32.ERROR_DIRECTORY_NOT_SUPPORTED, code);
        Assert.True(Directory.Exists(copyName));
    }

    [Fact]
    public void A_copy_name_swapped_for_a_link_is_not_followed_to_its_target()
    {
        // The real destination holds a child named like the copy, but that child is a
        // link to a file elsewhere. The discard opens the child relative to a handle on
        // the destination folder without following the child's link, finds a reparse
        // point and leaves it, so the file the link points at is untouched, and records
        // the reparse point as the cause. Best-effort: a link needs a privilege not every
        // host grants.
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

        Assert.False(_guard.TryDiscardCopy(copyName, out var code));

        Assert.Equal(Kernel32.ERROR_REPARSE_POINT_ENCOUNTERED, code);
        Assert.True(File.Exists(decoy));
    }
}
