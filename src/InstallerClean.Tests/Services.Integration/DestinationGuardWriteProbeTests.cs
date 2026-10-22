using System.Security.AccessControl;
using System.Security.Principal;
using InstallerClean.Helpers;
using InstallerClean.Services;

namespace InstallerClean.Tests.Services.Integration;

/// <summary>
/// The write probe against folders whose access list decides whether a file the probe
/// creates can also be deleted. The probe asks for the right to delete the file as it
/// creates it and marks it to go when its handle closes, so a folder that lets the
/// account create a file and does not let it delete one refuses the create, and the
/// folder is left with nothing in it.
///
/// Each folder carries a protected list naming this account alone, so no entry inherited
/// from the temporary folder's parent, and no group the account's token holds, can supply
/// the right to delete. The list gives the folder what the probe's open of it asks for
/// and the right to add a file, and gives files the rights to read and write them. The
/// folder's list never grants FILE_DELETE_CHILD, which would also satisfy a request to
/// delete a file inside it.
/// </summary>
public sealed class DestinationGuardWriteProbeTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ic-probe-acl-").FullName;
    private readonly IDestinationGuard _guard = new HandleDestinationGuard();

    private static readonly string ThisAccountSid = WindowsIdentity.GetCurrent().User!.Value;

    // ERROR_ACCESS_DENIED, which RtlNtStatusToDosError gives for STATUS_ACCESS_DENIED.
    private const int AccessDenied = 5;

    // On the folder: FILE_LIST_DIRECTORY, FILE_ADD_FILE, FILE_READ_EA, FILE_TRAVERSE,
    // FILE_READ_ATTRIBUTES, READ_CONTROL and SYNCHRONIZE. This covers GENERIC_READ, which
    // the probe's open of the folder asks for, and the right to add a file.
    private const string FolderRights = "0x1200AB";

    // Inherited by files: FILE_READ_DATA, FILE_WRITE_DATA, FILE_APPEND_DATA, FILE_READ_EA,
    // FILE_WRITE_EA, FILE_READ_ATTRIBUTES, FILE_WRITE_ATTRIBUTES, READ_CONTROL and
    // SYNCHRONIZE. No DELETE.
    private const string FileRightsWithoutDelete = "0x12019F";

    // The same with DELETE (0x10000) added.
    private const string FileRightsWithDelete = "0x13019F";

    private static string ListGranting(string fileRights) =>
        $"D:P(A;;{FolderRights};;;{ThisAccountSid})(A;OIIO;{fileRights};;;{ThisAccountSid})";

    private static readonly string FullControlOnFolders =
        $"D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FA;;;{ThisAccountSid})";

    private static readonly string FullControlOnFiles =
        $"D:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FA;;;{ThisAccountSid})";

    public void Dispose()
    {
        // The account created each folder and so may rewrite its list, which is how it is
        // let back in to delete a file the probe's folder let it create. Full control on
        // the folder includes FILE_DELETE_CHILD, and each file's own list is reset too.
        try
        {
            foreach (var folder in Directory.EnumerateDirectories(_root))
            {
                ResetAccess(folder, isFolder: true);
                foreach (var file in Directory.EnumerateFiles(folder))
                    ResetAccess(file, isFolder: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        try { Directory.Delete(_root, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void ResetAccess(string path, bool isFolder)
    {
        try
        {
            if (isFolder)
            {
                var security = new DirectorySecurity();
                security.SetSecurityDescriptorSddlForm(FullControlOnFolders, AccessControlSections.Access);
                new DirectoryInfo(path).SetAccessControl(security);
            }
            else
            {
                var security = new FileSecurity();
                security.SetSecurityDescriptorSddlForm(FullControlOnFiles, AccessControlSections.Access);
                new FileInfo(path).SetAccessControl(security);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private string MakeFolder(string accessList)
    {
        var path = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName;
        var security = new DirectorySecurity();
        security.SetSecurityDescriptorSddlForm(accessList, AccessControlSections.Access);
        new DirectoryInfo(path).SetAccessControl(security);
        return path;
    }

    private static void AssertTheProbeOpensTheFolder(string folder)
    {
        // Without this a refusal at the open of the folder would answer false with the same
        // code as a refusal of the create, and the assertions below could not tell them apart.
        using var opened = DirectoryGuard.OpenDirectoryFollow(folder, out var failureCode);
        Assert.True(opened is not null, $"the probe could not open the folder (Win32 error {failureCode})");
    }

    private static FileStream CreateMarkedToDeleteOnClose(string folder) =>
        new(Path.Combine(folder, "marked-to-delete"), FileMode.CreateNew, FileAccess.Write,
            FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose);

    [Fact]
    public void A_folder_that_grants_a_create_but_not_a_delete_fails_the_probe_and_is_left_empty()
    {
        var folder = MakeFolder(ListGranting(FileRightsWithoutDelete));
        AssertTheProbeOpensTheFolder(folder);

        // The folder has to refuse a create that asks to delete the file, or this is a
        // test of a folder that does not.
        var refusal = Record.Exception(() => CreateMarkedToDeleteOnClose(folder).Dispose());
        Assert.True(refusal is UnauthorizedAccessException,
            "the folder let this account create a file marked to delete on close, so it is not a folder "
            + $"that grants a create and refuses a delete (got {refusal?.GetType().Name ?? "no exception"})");
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));

        Assert.False(_guard.TryProbeWritable(folder, out var failureCode));
        Assert.Equal(AccessDenied, failureCode);
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));

        // And the folder does let this account create a file when the create asks for the
        // right to write it and nothing more, which is what the probe asks for less the right
        // to delete. Last, because it leaves one.
        using (new FileInfo(Path.Combine(folder, "plain")).Create(
            FileMode.CreateNew, FileSystemRights.WriteData, FileShare.None, 1, FileOptions.None, fileSecurity: null)) { }
        Assert.Single(Directory.EnumerateFileSystemEntries(folder));
    }

    [Fact]
    public void A_folder_that_grants_a_create_and_a_delete_passes_the_probe_and_is_left_empty()
    {
        var folder = MakeFolder(ListGranting(FileRightsWithDelete));
        AssertTheProbeOpensTheFolder(folder);

        // The control for the test above: the same list with DELETE added on files, so
        // the create marked to delete on close is allowed and removes its file as it closes.
        CreateMarkedToDeleteOnClose(folder).Dispose();
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));

        Assert.True(_guard.TryProbeWritable(folder, out var failureCode));
        Assert.Equal(0, failureCode);
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
    }
}
