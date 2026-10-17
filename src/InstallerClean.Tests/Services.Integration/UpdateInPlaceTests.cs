using System.Net;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Principal;
using InstallerClean.Services;

namespace InstallerClean.Tests.Services.Integration;

/// <summary>
/// The parts of the update from inside the app that touch the disk: the folder the
/// download is written to, the swap that puts a newer version at the running exe's
/// path, the leftovers a later start removes, and the whole of each route through
/// <see cref="AppUpdater"/>, against a temporary folder standing in for %ProgramData%.
/// </summary>
public sealed class UpdateInPlaceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ic-update-").FullName;

    /// <summary>
    /// The shipping descriptor with the account running the suite added, which runs
    /// without administrator rights and so could not write into the folder otherwise.
    /// </summary>
    private static readonly string AdministratorsAndThisAccount =
        UpdateStagingFolder.AdministratorsOnly + $"(A;OICI;FA;;;{WindowsIdentity.GetCurrent().User!.Value})";

    public void Dispose()
    {
        // A folder made with the shipping descriptor keeps this account out, but its
        // creator owns it and may rewrite its list, which is how it is let back in to
        // be deleted.
        foreach (var folder in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories))
        {
            try
            {
                var security = new DirectorySecurity();
                security.SetSecurityDescriptorSddlForm(AdministratorsAndThisAccount);
                new DirectoryInfo(folder).SetAccessControl(security);
            }
            catch { }
        }
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static byte[] Program(byte fill)
    {
        var bytes = new byte[2048];
        Array.Fill(bytes, fill);
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        return bytes;
    }

    private sealed class OneAnswer(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    [Fact]
    public void The_staging_folder_is_new_each_time_and_only_Administrators_and_SYSTEM_hold_any_right()
    {
        // The shipping descriptor itself. The suite cannot write into this folder, and
        // does not need to: its creator owns it, so it can read the list back.
        var first = UpdateStagingFolder.Create(_root);
        var second = UpdateStagingFolder.Create(_root);

        Assert.NotEqual(first.FullPath, second.FullPath);
        Assert.StartsWith(UpdateStagingFolder.NamePrefix, Path.GetFileName(first.FullPath));

        var security = new DirectoryInfo(first.FullPath).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);
        var holders = security.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(rule => (SecurityIdentifier)rule.IdentityReference)
            .Distinct()
            .ToHashSet();
        var expected = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.LocalSystemSid, null),
            new(WellKnownSidType.BuiltinAdministratorsSid, null),
        };
        Assert.True(holders.SetEquals(expected), string.Join(", ", holders));
    }

    [Fact]
    public void The_staging_folder_is_not_made_where_the_root_is_missing()
    {
        Assert.Throws<IOException>(() => UpdateStagingFolder.Create(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void Leftovers_go_and_nothing_else_does()
    {
        var ours = Path.Combine(_root, UpdateStagingFolder.NamePrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ours);
        File.WriteAllBytes(Path.Combine(ours, "InstallerClean-3.5.0-setup.exe"), Program(1));

        var oursWithOtherFile = Path.Combine(_root, UpdateStagingFolder.NamePrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(oursWithOtherFile);
        File.WriteAllText(Path.Combine(oursWithOtherFile, "notes.txt"), "kept");

        var notOurName = Path.Combine(_root, UpdateStagingFolder.NamePrefix + "something-else");
        Directory.CreateDirectory(notOurName);
        File.WriteAllBytes(Path.Combine(notOurName, "InstallerClean-3.5.0-setup.exe"), Program(2));

        var allGone = UpdateStagingFolder.RemoveLeftovers(_root);

        Assert.False(allGone);
        Assert.False(Directory.Exists(ours));
        Assert.True(File.Exists(Path.Combine(oursWithOtherFile, "notes.txt")));
        Assert.True(File.Exists(Path.Combine(notOurName, "InstallerClean-3.5.0-setup.exe")));
    }

    [Fact]
    public void The_swap_puts_the_newer_file_at_the_exe_path_and_putting_back_restores_it()
    {
        var exe = Path.Combine(_root, "InstallerClean-3.4.0-portable.exe");
        var staged = Path.Combine(_root, "staged.exe");
        File.WriteAllBytes(exe, Program(1));
        File.WriteAllBytes(staged, Program(2));

        var putBack = SelfReplacement.Replace(exe, staged);

        Assert.Equal(Program(2), File.ReadAllBytes(exe));
        Assert.Equal(Program(1), File.ReadAllBytes(exe + SelfReplacement.OldSuffix));
        Assert.False(File.Exists(staged));

        putBack();

        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
    }

    [Fact]
    public void The_swap_clears_a_copy_an_earlier_update_left_aside()
    {
        var exe = Path.Combine(_root, "InstallerClean.exe");
        var staged = Path.Combine(_root, "staged.exe");
        File.WriteAllBytes(exe, Program(1));
        File.WriteAllBytes(exe + SelfReplacement.OldSuffix, Program(9));
        File.WriteAllBytes(staged, Program(2));

        SelfReplacement.Replace(exe, staged);

        Assert.Equal(Program(1), File.ReadAllBytes(exe + SelfReplacement.OldSuffix));
        Assert.True(SelfReplacement.TryRemoveOldCopy(exe));
        Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
    }

    [Fact]
    public void A_swap_whose_second_move_fails_puts_the_running_exe_back()
    {
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));

        var failure = Assert.Throws<UpdateFailedException>(
            () => SelfReplacement.Replace(exe, Path.Combine(_root, "missing.exe")));

        Assert.Equal(UpdateFailureStage.Install, failure.Stage);
        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
    }

    [Fact]
    public async Task The_portable_route_downloads_swaps_and_leaves_no_staging_folder()
    {
        var exeFolder = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        var stagingRoot = Directory.CreateDirectory(Path.Combine(_root, "programdata")).FullName;
        var exe = Path.Combine(exeFolder, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        var updater = new AppUpdater(AppCopyKind.Portable, exe,
            new UpdateDownloadService(new OneAnswer(Program(2)), null), stagingRoot,
            AppUpdater.WindowSetupArguments, AdministratorsAndThisAccount);

        var prepared = await updater.PrepareAsync("3.5.0", null, CancellationToken.None);

        Assert.Equal(exe, prepared.FileToStart);
        Assert.Equal(string.Empty, prepared.Arguments);
        Assert.Equal(Program(2), File.ReadAllBytes(exe));
        Assert.Empty(Directory.EnumerateFileSystemEntries(stagingRoot));

        prepared.Undo();

        Assert.Equal(Program(1), File.ReadAllBytes(exe));
    }

    [Fact]
    public async Task The_setup_route_starts_the_downloaded_setup_with_the_apps_switch()
    {
        var stagingRoot = Directory.CreateDirectory(Path.Combine(_root, "programdata")).FullName;
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        var updater = new AppUpdater(AppCopyKind.Setup, exe,
            new UpdateDownloadService(new OneAnswer(Program(3)), null), stagingRoot,
            AppUpdater.WindowSetupArguments, AdministratorsAndThisAccount);

        var prepared = await updater.PrepareAsync("3.5.0", null, CancellationToken.None);

        Assert.Equal("InstallerClean-3.5.0-setup.exe", Path.GetFileName(prepared.FileToStart));
        Assert.StartsWith(stagingRoot, prepared.FileToStart);
        Assert.Contains("/UPDATEFROMAPP", prepared.Arguments);
        Assert.Contains("/SILENT", prepared.Arguments);
        Assert.Equal(Program(3), File.ReadAllBytes(prepared.FileToStart));
        Assert.Equal(Program(1), File.ReadAllBytes(exe));

        prepared.Undo();

        Assert.Empty(Directory.EnumerateFileSystemEntries(stagingRoot));
    }

    [Fact]
    public async Task The_setup_route_starts_the_downloaded_setup_with_the_arguments_it_was_given()
    {
        var stagingRoot = Directory.CreateDirectory(Path.Combine(_root, "programdata")).FullName;
        var exe = Path.Combine(_root, "installerclean-cli.exe");
        File.WriteAllBytes(exe, Program(1));
        var updater = new AppUpdater(AppCopyKind.Setup, exe,
            new UpdateDownloadService(new OneAnswer(Program(3)), null), stagingRoot,
            CommandLineUpdate.SetupArguments(canAsk: false), AdministratorsAndThisAccount);

        var prepared = await updater.PrepareAsync("3.5.0", null, CancellationToken.None);

        Assert.Equal(CommandLineUpdate.SetupArguments(canAsk: false), prepared.Arguments);
        Assert.DoesNotContain("/UPDATEFROMAPP", prepared.Arguments);

        prepared.Undo();
    }

    [Fact]
    public async Task The_command_lines_own_download_replaces_its_file_as_the_portable_does()
    {
        var exeFolder = Directory.CreateDirectory(Path.Combine(_root, "tools")).FullName;
        var stagingRoot = Directory.CreateDirectory(Path.Combine(_root, "programdata")).FullName;
        var exe = Path.Combine(exeFolder, AppCopy.CommandLineFileName);
        File.WriteAllBytes(exe, Program(1));
        var updater = new AppUpdater(AppCopyKind.CommandLineDownload, exe,
            new UpdateDownloadService(new OneAnswer(Program(2)), null), stagingRoot,
            CommandLineUpdate.SetupArguments(canAsk: false), AdministratorsAndThisAccount);

        var prepared = await updater.PrepareAsync("3.5.0", null, CancellationToken.None);

        Assert.Equal(exe, prepared.FileToStart);
        Assert.Equal(Program(2), File.ReadAllBytes(exe));
        Assert.Equal(Program(1), File.ReadAllBytes(exe + SelfReplacement.OldSuffix));
        Assert.Empty(Directory.EnumerateFileSystemEntries(stagingRoot));

        prepared.Undo();

        Assert.Equal(Program(1), File.ReadAllBytes(exe));
    }

    [Fact]
    public async Task A_failed_download_changes_nothing_and_leaves_no_staging_folder()
    {
        var stagingRoot = Directory.CreateDirectory(Path.Combine(_root, "programdata")).FullName;
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        var updater = new AppUpdater(AppCopyKind.Portable, exe,
            new UpdateDownloadService(new OneAnswer("not a program"u8.ToArray()), null), stagingRoot,
            AppUpdater.WindowSetupArguments, AdministratorsAndThisAccount);

        var failure = await Assert.ThrowsAsync<UpdateFailedException>(
            () => updater.PrepareAsync("3.5.0", null, CancellationToken.None));

        Assert.Equal(UpdateFailureStage.Download, failure.Stage);
        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
        Assert.Empty(Directory.EnumerateFileSystemEntries(stagingRoot));
    }
}
