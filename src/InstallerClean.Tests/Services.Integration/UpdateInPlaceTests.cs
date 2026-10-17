using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using InstallerClean.Services;

namespace InstallerClean.Tests.Services.Integration;

/// <summary>
/// The parts of the update from inside the app that touch the disk: the folder the
/// download is written to, the newer version written beside the running exe, the swap
/// that puts it at the exe's path and putting it back, the leftovers a later start
/// removes, and the whole of each route through <see cref="AppUpdater"/>, against a
/// temporary folder standing in for %ProgramData%.
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

    /// <summary>The exe and, beside it, the newer version written as an update writes it.</summary>
    private async Task<string> StagedBesideAsync(string exe, byte fill)
    {
        var download = Path.Combine(_root, "download-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(download, Program(fill));
        return await SelfReplacement.StageAsync(exe, download, CancellationToken.None);
    }

    [Fact]
    public async Task The_newer_version_is_written_beside_the_exe_as_a_file_made_in_that_folder()
    {
        var exeFolder = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        var exe = Path.Combine(exeFolder, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        var madeThere = Path.Combine(exeFolder, "control.exe");
        File.WriteAllBytes(madeThere, Program(3));

        var staged = await StagedBesideAsync(exe, 2);

        Assert.Equal(exe + SelfReplacement.NewSuffix, staged);
        Assert.Equal(Program(2), File.ReadAllBytes(staged));
        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        var security = new FileInfo(staged).GetAccessControl();
        Assert.False(security.AreAccessRulesProtected);
        Assert.Empty(security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)));
        Assert.Equal(
            new FileInfo(madeThere).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access),
            security.GetSecurityDescriptorSddlForm(AccessControlSections.Access));
    }

    [Fact]
    public async Task Writing_the_newer_version_replaces_a_plain_file_an_earlier_update_left_there()
    {
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        File.WriteAllBytes(exe + SelfReplacement.NewSuffix, Program(9));

        var staged = await StagedBesideAsync(exe, 2);

        Assert.Equal(Program(2), File.ReadAllBytes(staged));
    }

    [Fact]
    public async Task Writing_the_newer_version_fails_where_something_other_than_a_file_is_in_the_way()
    {
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        Directory.CreateDirectory(exe + SelfReplacement.NewSuffix);

        var failure = await Assert.ThrowsAsync<UpdateFailedException>(() => StagedBesideAsync(exe, 2));

        Assert.Equal(UpdateFailureStage.Install, failure.Stage);
        Assert.True(Directory.Exists(exe + SelfReplacement.NewSuffix));
        Assert.Equal(Program(1), File.ReadAllBytes(exe));
    }

    [Fact]
    public async Task The_swap_puts_the_newer_file_at_the_exe_path_and_putting_back_restores_it()
    {
        var exe = Path.Combine(_root, "InstallerClean-3.4.0-portable.exe");
        File.WriteAllBytes(exe, Program(1));
        await StagedBesideAsync(exe, 2);

        var putBack = SelfReplacement.SwapIn(exe);

        Assert.Equal(Program(2), File.ReadAllBytes(exe));
        Assert.Equal(Program(1), File.ReadAllBytes(exe + SelfReplacement.OldSuffix));
        Assert.False(File.Exists(exe + SelfReplacement.NewSuffix));

        Assert.True(putBack());

        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
        Assert.Equal(Program(2), File.ReadAllBytes(exe + SelfReplacement.NewSuffix));
    }

    [Fact]
    public async Task The_swap_clears_a_copy_an_earlier_update_left_aside()
    {
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        File.WriteAllBytes(exe + SelfReplacement.OldSuffix, Program(9));
        await StagedBesideAsync(exe, 2);

        SelfReplacement.SwapIn(exe);

        Assert.Equal(Program(1), File.ReadAllBytes(exe + SelfReplacement.OldSuffix));
        Assert.True(SelfReplacement.TryRemoveOldCopy(exe));
        Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
    }

    [Fact]
    public void A_swap_with_nothing_written_beside_the_exe_puts_the_running_exe_back()
    {
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));

        var failure = Assert.Throws<UpdateFailedException>(() => SelfReplacement.SwapIn(exe));

        Assert.Equal(UpdateFailureStage.Install, failure.Stage);
        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
    }

    [Fact]
    public void A_file_held_against_renaming_cannot_be_moved_aside_until_it_is_let_go()
    {
        var exe = Path.Combine(_root, AppCopy.CommandLineFileName);
        File.WriteAllBytes(exe, Program(1));

        using (var held = SelfReplacement.HoldAgainstRenaming(exe))
        {
            Assert.NotNull(held);
            Assert.Throws<UpdateFailedException>(() => SelfReplacement.MoveAside(exe));
            Assert.Equal(Program(1), File.ReadAllBytes(exe));
            Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
        }

        SelfReplacement.MoveAside(exe);

        Assert.Equal(Program(1), File.ReadAllBytes(exe + SelfReplacement.OldSuffix));
    }

    [Fact]
    public void A_process_started_from_the_file_is_found_and_one_started_from_another_file_of_its_name_is_not()
    {
        // A copy of a program under a name no other process has, and a second copy of the
        // same name in another folder that is never started.
        var name = $"ic-run-{Guid.NewGuid():N}.exe";
        var running = Path.Combine(_root, name);
        var elsewhere = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, "elsewhere")).FullName, name);
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), running);
        File.Copy(running, elsewhere);

        Assert.False(SelfReplacement.AnotherProcessMayRunFrom(running));

        using (SuspendedProcess.Start(running))
        {
            Assert.True(SelfReplacement.AnotherProcessMayRunFrom(running));
            Assert.False(SelfReplacement.AnotherProcessMayRunFrom(elsewhere));
        }

        Assert.False(SelfReplacement.AnotherProcessMayRunFrom(running));
    }

    /// <summary>
    /// A process started suspended, so it runs none of its own code and stays until it is
    /// ended. Disposing it ends it, waits for that and closes both of its handles, after
    /// which Windows no longer lists it.
    /// </summary>
    private sealed class SuspendedProcess : IDisposable
    {
        private const uint CreateSuspended = 0x00000004;
        private readonly IntPtr _process;
        private readonly IntPtr _thread;

        private SuspendedProcess(IntPtr process, IntPtr thread)
        {
            _process = process;
            _thread = thread;
        }

        public static SuspendedProcess Start(string path)
        {
            var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
            if (!CreateProcess(path, null, IntPtr.Zero, IntPtr.Zero, false, CreateSuspended,
                    IntPtr.Zero, null, ref startup, out var started))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return new SuspendedProcess(started.hProcess, started.hThread);
        }

        public void Dispose()
        {
            TerminateProcess(_process, 1);
            WaitForSingleObject(_process, 10_000);
            CloseHandle(_thread);
            CloseHandle(_process);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int cb;
            public string? lpReserved;
            public string? lpDesktop;
            public string? lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcess(
            string lpApplicationName, string? lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
            bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory,
            ref StartupInfo lpStartupInfo, out ProcessInformation lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
    }

    [Fact]
    public async Task Putting_back_answers_false_where_the_newer_file_cannot_move_out_of_the_way()
    {
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        await StagedBesideAsync(exe, 2);
        var putBack = SelfReplacement.SwapIn(exe);
        Directory.CreateDirectory(exe + SelfReplacement.NewSuffix);

        Assert.False(putBack());

        Assert.Equal(Program(2), File.ReadAllBytes(exe));
        Assert.Equal(Program(1), File.ReadAllBytes(exe + SelfReplacement.OldSuffix));
    }

    [Fact]
    public void Putting_back_after_the_setup_moves_whatever_it_left_at_the_path_aside()
    {
        var exe = Path.Combine(_root, "installerclean-cli.exe");
        File.WriteAllBytes(exe, Program(1));
        var putBack = SelfReplacement.MoveAside(exe);
        File.WriteAllBytes(exe, Program(4));

        Assert.True(putBack());

        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.Equal(Program(4), File.ReadAllBytes(exe + SelfReplacement.NewSuffix));
        Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
    }

    [Fact]
    public void The_leftovers_beside_the_exe_go_and_a_folder_in_their_place_is_left_alone()
    {
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        File.WriteAllBytes(exe + SelfReplacement.OldSuffix, Program(8));
        File.WriteAllBytes(exe + SelfReplacement.NewSuffix, Program(9));

        Assert.True(SelfReplacement.TryRemoveOldCopy(exe));
        Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
        Assert.False(File.Exists(exe + SelfReplacement.NewSuffix));

        Directory.CreateDirectory(exe + SelfReplacement.NewSuffix);
        Assert.True(SelfReplacement.TryRemoveOldCopy(exe));
        Assert.True(Directory.Exists(exe + SelfReplacement.NewSuffix));
        Assert.Equal(Program(1), File.ReadAllBytes(exe));
    }

    [Fact]
    public async Task A_hand_over_whose_newer_version_will_not_start_puts_everything_back_and_answers_false()
    {
        // Program() writes no runnable image, so every start fails and the process is never ended.
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        await StagedBesideAsync(exe, 2);

        Assert.False(SelfReplacement.HandOverAndEnd(exe));

        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
        Assert.Equal(Program(2), File.ReadAllBytes(exe + SelfReplacement.NewSuffix));
    }

    [Fact]
    public void A_hand_over_with_nothing_written_beside_the_exe_changes_nothing_and_answers_false()
    {
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));

        Assert.False(SelfReplacement.HandOverAndEnd(exe));

        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
    }

    [Fact]
    public void Everything_the_named_assemblies_reference_is_loaded_before_a_rename()
    {
        SelfReplacement.PrepareToMoveAside(typeof(System.Diagnostics.EventLog).Assembly);

        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roots = new[]
        {
            typeof(SelfReplacement).Assembly,
            typeof(System.Diagnostics.Process).Assembly,
            typeof(System.Diagnostics.EventLog).Assembly,
        };
        var missing = roots
            .SelectMany(root => root.GetReferencedAssemblies().Select(reference => $"{root.GetName().Name} -> {reference.Name}"))
            .Where(pair => !loaded.Contains(pair[(pair.IndexOf("-> ", StringComparison.Ordinal) + 3)..]))
            .ToList();
        Assert.Empty(missing);
        Assert.Contains("System.Diagnostics.StackTrace", loaded);
    }

    [Fact]
    public async Task The_portable_route_downloads_writes_the_newer_version_beside_the_exe_and_leaves_no_staging_folder()
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
        Assert.Equal(exe + SelfReplacement.NewSuffix, prepared.Replacement);
        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.Equal(Program(2), File.ReadAllBytes(exe + SelfReplacement.NewSuffix));
        Assert.Empty(Directory.EnumerateFileSystemEntries(stagingRoot));

        prepared.Undo();

        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.False(File.Exists(exe + SelfReplacement.NewSuffix));
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
    public async Task The_command_lines_own_download_writes_beside_its_file_as_the_portable_does()
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
        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.Equal(Program(2), File.ReadAllBytes(exe + SelfReplacement.NewSuffix));
        Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
        Assert.Empty(Directory.EnumerateFileSystemEntries(stagingRoot));

        prepared.Undo();

        Assert.False(File.Exists(exe + SelfReplacement.NewSuffix));
    }

    [Theory]
    [InlineData(AppCopyKind.Scoop)]
    [InlineData(AppCopyKind.FromSource)]
    public async Task A_copy_with_no_download_is_refused_before_anything_is_made(AppCopyKind kind)
    {
        var stagingRoot = Directory.CreateDirectory(Path.Combine(_root, "programdata")).FullName;
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        var updater = new AppUpdater(kind, exe,
            new UpdateDownloadService(new OneAnswer(Program(2)), null), stagingRoot,
            AppUpdater.WindowSetupArguments, AdministratorsAndThisAccount);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => updater.PrepareAsync("3.5.0", null, CancellationToken.None));

        Assert.False(updater.CanUpdateItself);
        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.False(File.Exists(exe + SelfReplacement.NewSuffix));
        Assert.Empty(Directory.EnumerateFileSystemEntries(stagingRoot));
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
        Assert.False(File.Exists(exe + SelfReplacement.NewSuffix));
        Assert.Empty(Directory.EnumerateFileSystemEntries(stagingRoot));
    }
}
