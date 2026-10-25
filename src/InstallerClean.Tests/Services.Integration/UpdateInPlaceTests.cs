using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using InstallerClean.Helpers;
using InstallerClean.Services;
using InstallerClean.Tests.Helpers;
using NSubstitute;

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
    private readonly IDisposable _crashLog = TestCrashLog.OwnFolder();

    /// <summary>The SID of the account running the suite.</summary>
    private static readonly string ThisAccountSid = WindowsIdentity.GetCurrent().User!.Value;

    /// <summary>
    /// The shipping list with the account running the suite added, owned by that account
    /// rather than Administrators. The suite runs without administrator rights, so it
    /// cannot set Administrators as the owner the way the shipping descriptor does, and
    /// could not write into the folder without its own entry.
    /// </summary>
    private static readonly string AdministratorsAndThisAccount =
        $"O:{ThisAccountSid}D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FA;;;{ThisAccountSid})";

    /// <summary>
    /// What <see cref="UpdateStagingFolder.RemoveLeftovers(string, DirectoryGuard.FolderExpectation)"/>
    /// is told to accept for a folder made with <see cref="AdministratorsAndThisAccount"/>:
    /// this account alongside SYSTEM and Administrators.
    /// </summary>
    private static readonly DirectoryGuard.FolderExpectation ThisAccountExpectation = new(
        new HashSet<string> { ThisAccountSid, DirectoryGuard.SystemSid, DirectoryGuard.AdministratorsSid },
        new HashSet<string> { ThisAccountSid, DirectoryGuard.SystemSid, DirectoryGuard.AdministratorsSid },
        requireProtected: true);

    public void Dispose()
    {
        try
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
        finally
        {
            _crashLog.Dispose();
        }
    }

    private static byte[] Program(byte fill)
    {
        var bytes = new byte[2048];
        Array.Fill(bytes, fill);
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        return bytes;
    }

    /// <summary>The Core library's own file, which carries a version resource.</summary>
    private static readonly string CoreLibrary = typeof(FileVersionResource).Assembly.Location;

    /// <summary>
    /// The release the Core library's file version names, which a download built by
    /// <see cref="Release"/> carries, as the update check gives a version.
    /// </summary>
    private static readonly string CoreRelease = FileVersionResource.Read(CoreLibrary) is { } v
        ? UpdateCheckService.NormaliseToBuild(v).ToString()
        : throw new InvalidOperationException("The Core library carries no file version.");

    /// <summary>
    /// A download that carries <see cref="CoreRelease"/>: the Core library's bytes with
    /// <paramref name="tag"/> appended, so two of them differ while the version resource
    /// both carry is the same.
    /// </summary>
    private static byte[] Release(byte tag) => [.. File.ReadAllBytes(CoreLibrary), .. Enumerable.Repeat(tag, 16)];

    /// <summary>A registry that answers nothing, so .NET Framework 4.8 reads as not installed.</summary>
    private static readonly IRegistryReader NoRegistry = NSubstitute.Substitute.For<IRegistryReader>();

    /// <summary>An update.json that lists every download with nothing required.</summary>
    private const string AllowsEveryDownload =
        """{"schema": 1, "downloads": {"setup": {"requires": []}, "portable": {"requires": []}, "slim": {"requires": []}, "cli": {"requires": []}}}""";

    /// <summary>
    /// Answers every download with <paramref name="body"/>, and a release's update.json
    /// with <paramref name="manifest"/>, or 404 Not Found where it is null, as for a
    /// release that has none.
    /// </summary>
    private sealed class OneAnswer(byte[] body, string? manifest = AllowsEveryDownload) : HttpMessageHandler
    {
        /// <summary>How many download requests it has answered.</summary>
        public int Requests { get; private set; }

        /// <summary>How many update.json requests it has answered.</summary>
        public int ManifestRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/" + UpdateManifest.FileName, StringComparison.Ordinal))
            {
                ManifestRequests++;
                return Task.FromResult(manifest is null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifest) });
            }
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }

    [AdministratorFact]
    public void The_staging_folder_is_new_each_time_owned_by_Administrators_and_only_they_and_SYSTEM_hold_any_right()
    {
        // The shipping descriptor itself, which sets Administrators as the owner. Only a
        // process with administrator rights can assign that owner, which is why this runs
        // where the suite holds them.
        var first = UpdateStagingFolder.Create(_root);
        var second = UpdateStagingFolder.Create(_root);

        Assert.NotEqual(first.FullPath, second.FullPath);
        Assert.StartsWith(UpdateStagingFolder.NamePrefix, Path.GetFileName(first.FullPath));

        var security = new DirectoryInfo(first.FullPath).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        Assert.Equal(administrators, security.GetOwner(typeof(SecurityIdentifier)));
        var holders = security.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(rule => (SecurityIdentifier)rule.IdentityReference)
            .Distinct()
            .ToHashSet();
        var expected = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.LocalSystemSid, null),
            administrators,
        };
        Assert.True(holders.SetEquals(expected), string.Join(", ", holders));
    }

    [Fact]
    public void The_staging_folder_is_not_made_where_the_root_is_missing()
    {
        Assert.Throws<IOException>(() => UpdateStagingFolder.Create(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void Leftovers_in_the_apps_own_folders_go_and_nothing_else_does()
    {
        // A folder the app made, holding a download: cleaned, and the folder with it.
        var ours = UpdateStagingFolder.Create(_root, AdministratorsAndThisAccount).FullPath;
        File.WriteAllBytes(Path.Combine(ours, "InstallerClean-3.5.0-setup.exe"), Program(1));

        // The app's own folder, but a file that is not a download keeps it: the download
        // goes, the other file and the folder stay.
        var oursWithOtherFile = UpdateStagingFolder.Create(_root, AdministratorsAndThisAccount).FullPath;
        File.WriteAllBytes(Path.Combine(oursWithOtherFile, "InstallerClean-3.5.0-setup.exe"), Program(1));
        File.WriteAllText(Path.Combine(oursWithOtherFile, "notes.txt"), "kept");

        // A folder whose name is not the download shape, left alone.
        var notOurName = Path.Combine(_root, UpdateStagingFolder.NamePrefix + "something-else");
        Directory.CreateDirectory(notOurName);
        File.WriteAllBytes(Path.Combine(notOurName, "InstallerClean-3.5.0-setup.exe"), Program(2));

        var allGone = UpdateStagingFolder.RemoveLeftovers(_root, ThisAccountExpectation);

        Assert.False(allGone);
        Assert.False(Directory.Exists(ours));
        Assert.False(File.Exists(Path.Combine(oursWithOtherFile, "InstallerClean-3.5.0-setup.exe")));
        Assert.True(File.Exists(Path.Combine(oursWithOtherFile, "notes.txt")));
        Assert.True(File.Exists(Path.Combine(notOurName, "InstallerClean-3.5.0-setup.exe")));
    }

    [Fact]
    public void A_folder_another_account_could_have_made_is_left_untouched()
    {
        // A folder with the download name and a file of the download shape inside it,
        // but made as an ordinary folder: its owner and list are not the app's own, so
        // the real check leaves it and the file whole. This is the account another user
        // on the machine would act as.
        var foreign = Path.Combine(_root, UpdateStagingFolder.NamePrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(foreign);
        var planted = Path.Combine(foreign, "InstallerClean-anything.exe");
        File.WriteAllBytes(planted, Program(1));

        var allGone = UpdateStagingFolder.RemoveLeftovers(_root);

        // A folder that is not the app's own is not one the app is waiting on, so it does
        // not keep the clean-up retrying.
        Assert.True(allGone);
        Assert.True(Directory.Exists(foreign));
        Assert.True(File.Exists(planted));
    }

    [Fact]
    public void A_link_in_place_of_a_folder_is_left_and_its_target_is_untouched()
    {
        // A download-named candidate that is a link: the clean-up opens it without
        // following the link, finds a reparse point and leaves it, so a file reached
        // through the link is untouched. Best-effort because creating a link needs a
        // privilege not every host grants.
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        var decoy = Path.Combine(target, "keep.txt");
        File.WriteAllText(decoy, "untouched");

        var link = Path.Combine(_root, UpdateStagingFolder.NamePrefix + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // this host cannot create links; the refusal path is exercised where it can
        }

        var allGone = UpdateStagingFolder.RemoveLeftovers(_root, ThisAccountExpectation);

        // A link is not a folder the app made, so it is left and does not keep the
        // clean-up retrying.
        Assert.True(allGone);
        Assert.True(File.Exists(decoy));
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
        // A portable saved under the versioned name 3.4.0 gave it: the update puts the
        // newer file at this path and does not rename it.
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
    public async Task A_swap_that_can_put_neither_file_at_the_exe_path_keeps_both_beside_it_until_one_can_go()
    {
        // A folder at the exe's path refuses every rename to it.
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        await StagedBesideAsync(exe, 2);

        var failure = Assert.Throws<UpdateFailedException>(() =>
            SelfReplacement.SwapInRunning(exe, CancellationToken.None, () => Directory.CreateDirectory(exe)));

        Assert.Equal(UpdateFailureStage.Install, failure.Stage);
        Assert.Equal(Program(1), File.ReadAllBytes(exe + SelfReplacement.OldSuffix));
        Assert.Equal(Program(2), File.ReadAllBytes(exe + SelfReplacement.NewSuffix));
        Assert.False(SelfReplacement.TryRefillPath(exe));

        Directory.Delete(exe);
        Assert.True(SelfReplacement.TryRefillPath(exe));

        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
        Assert.Equal(Program(2), File.ReadAllBytes(exe + SelfReplacement.NewSuffix));
    }

    [Fact]
    public async Task A_swap_whose_running_exe_will_not_go_back_puts_the_newer_version_at_the_exe_path()
    {
        // The newer version is held open past the first rename's wait and let go during the
        // last, and a folder in place of the renamed exe refuses the rename back.
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        var staged = await StagedBesideAsync(exe, 2);
        var held = new FileStream(staged, FileMode.Open, FileAccess.Read, FileShare.None);
        var letGo = Task.CompletedTask;
        var aside = Path.Combine(_root, "aside.exe");

        Func<bool> putBack;
        try
        {
            putBack = SelfReplacement.SwapInRunning(exe, CancellationToken.None, () =>
            {
                File.Move(exe + SelfReplacement.OldSuffix, aside);
                Directory.CreateDirectory(exe + SelfReplacement.OldSuffix);
                letGo = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(_ => held.Dispose());
            });
        }
        finally
        {
            await letGo;
            held.Dispose();
        }

        Assert.NotNull(putBack);
        Assert.Equal(Program(2), File.ReadAllBytes(exe));
        Assert.False(File.Exists(exe + SelfReplacement.NewSuffix));
        // The first rename's wait ran out and was written to crash.log, and the rename back of
        // the running exe was tried and refused, before the swap went ahead all the same.
        Assert.Equal(1, EntriesInCrashLog<FileHeldOpenException>(staged));
        Assert.Equal(1, EntriesInCrashLog<FileNotFoundException>(exe + SelfReplacement.OldSuffix));
    }

    /// <summary>
    /// How many entries in the crash log are a <typeparamref name="T"/> whose message names
    /// <paramref name="path"/>, on the entry's first line.
    /// </summary>
    private static int EntriesInCrashLog<T>(string path) where T : Exception =>
        TestCrashLog.Text().Split('\n').Count(line =>
            line.StartsWith(typeof(T).FullName + ":", StringComparison.Ordinal)
            && line.Contains($"'{path}'", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task A_put_back_whose_running_exe_will_not_go_back_leaves_the_newer_version_at_the_exe_path()
    {
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        await StagedBesideAsync(exe, 2);
        var putBack = SelfReplacement.SwapIn(exe);
        File.Delete(exe + SelfReplacement.OldSuffix);
        Directory.CreateDirectory(exe + SelfReplacement.OldSuffix);

        Assert.False(putBack());

        Assert.Equal(Program(2), File.ReadAllBytes(exe));
        Assert.False(File.Exists(exe + SelfReplacement.NewSuffix));
    }

    [Fact]
    public void Refilling_an_exe_path_puts_the_newer_version_there_where_no_copy_was_moved_aside()
    {
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe + SelfReplacement.NewSuffix, Program(2));

        Assert.True(SelfReplacement.TryRefillPath(exe));

        Assert.Equal(Program(2), File.ReadAllBytes(exe));
    }

    [Fact]
    public void Refilling_an_exe_path_that_holds_a_file_changes_nothing()
    {
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        File.WriteAllBytes(exe + SelfReplacement.OldSuffix, Program(9));

        Assert.True(SelfReplacement.TryRefillPath(exe));

        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.Equal(Program(9), File.ReadAllBytes(exe + SelfReplacement.OldSuffix));
    }

    [Fact]
    public void A_file_held_against_renaming_cannot_be_moved_aside_until_it_is_let_go()
    {
        var exe = Path.Combine(_root, AppCopy.CommandLineFileName);
        File.WriteAllBytes(exe, Program(1));

        using (var held = SelfReplacement.HoldAgainstRenaming(exe))
        {
            Assert.NotNull(held);
            var failure = Assert.Throws<UpdateFailedException>(() => SelfReplacement.MoveAside(exe));
            // The Restart Manager answers, and its list leaves this process out.
            var holders = Assert.IsType<FileHeldOpenException>(failure.InnerException).Message;
            Assert.StartsWith($"The file '{exe}' stayed held open. The Restart Manager names ", holders);
            Assert.DoesNotContain($"(process {Environment.ProcessId}", holders);
            Assert.Equal(Program(1), File.ReadAllBytes(exe));
            Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
        }

        SelfReplacement.MoveAside(exe);

        Assert.Equal(Program(1), File.ReadAllBytes(exe + SelfReplacement.OldSuffix));
    }

    [Fact]
    public void A_file_let_go_while_the_rename_waits_is_moved_aside()
    {
        var exe = Path.Combine(_root, AppCopy.CommandLineFileName);
        File.WriteAllBytes(exe, Program(1));
        using var held = SelfReplacement.HoldAgainstRenaming(exe);
        Assert.NotNull(held);
        using var letGo = new Timer(_ => held.Dispose(), null, TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);

        SelfReplacement.MoveAside(exe);

        Assert.Equal(Program(1), File.ReadAllBytes(exe + SelfReplacement.OldSuffix));
        Assert.False(File.Exists(exe));
    }

    [Fact]
    public void Ctrl_C_while_the_rename_waits_ends_the_wait_with_nothing_moved()
    {
        var exe = Path.Combine(_root, AppCopy.CommandLineFileName);
        File.WriteAllBytes(exe, Program(1));
        using var held = SelfReplacement.HoldAgainstRenaming(exe);
        Assert.NotNull(held);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var clock = Stopwatch.StartNew();

        Assert.Throws<OperationCanceledException>(() => SelfReplacement.MoveAside(exe, cts.Token));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1.5), $"The wait took {clock.Elapsed}.");
        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
    }

    [Fact]
    public async Task Ctrl_C_does_not_end_the_wait_for_the_newer_version_once_this_exe_is_aside()
    {
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        var staged = await StagedBesideAsync(exe, 2);
        using var held = SelfReplacement.HoldAgainstRenaming(staged);
        Assert.NotNull(held);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var clock = Stopwatch.StartNew();

        var failure = Assert.Throws<UpdateFailedException>(() => SelfReplacement.SwapIn(exe, cts.Token));

        Assert.True(clock.Elapsed >= TimeSpan.FromSeconds(1.5), $"The wait took {clock.Elapsed}.");
        Assert.IsType<FileHeldOpenException>(failure.InnerException);
        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
        Assert.True(File.Exists(staged));
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
    public void A_command_line_moved_aside_with_nothing_in_its_place_is_put_back()
    {
        var exe = Path.Combine(_root, AppCopy.CommandLineFileName);
        File.WriteAllBytes(exe + SelfReplacement.OldSuffix, Program(1));

        Assert.True(SelfReplacement.TryPutBackMissing(exe, () => false));

        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
    }

    [Fact]
    public void A_newer_command_line_at_the_path_stays_and_the_copy_beside_it_is_left()
    {
        var exe = Path.Combine(_root, AppCopy.CommandLineFileName);
        File.WriteAllBytes(exe, Program(2));
        File.WriteAllBytes(exe + SelfReplacement.OldSuffix, Program(1));

        Assert.False(SelfReplacement.TryPutBackMissing(exe, () => false));

        Assert.Equal(Program(2), File.ReadAllBytes(exe));
        Assert.Equal(Program(1), File.ReadAllBytes(exe + SelfReplacement.OldSuffix));
    }

    [Fact]
    public void Nothing_is_put_back_while_a_setup_is_running()
    {
        var exe = Path.Combine(_root, AppCopy.CommandLineFileName);
        File.WriteAllBytes(exe + SelfReplacement.OldSuffix, Program(1));

        Assert.False(SelfReplacement.TryPutBackMissing(exe, () => true));

        Assert.False(File.Exists(exe));
        Assert.Equal(Program(1), File.ReadAllBytes(exe + SelfReplacement.OldSuffix));
    }

    [Fact]
    public void A_copy_a_program_holds_open_is_not_put_back()
    {
        var exe = Path.Combine(_root, AppCopy.CommandLineFileName);
        File.WriteAllBytes(exe + SelfReplacement.OldSuffix, Program(1));

        using (new FileStream(exe + SelfReplacement.OldSuffix, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            Assert.False(SelfReplacement.TryPutBackMissing(exe, () => false));

        Assert.False(File.Exists(exe));
        Assert.True(SelfReplacement.TryPutBackMissing(exe, () => false));
    }

    [Fact]
    public void A_folder_in_place_of_the_copy_is_not_put_back()
    {
        var exe = Path.Combine(_root, AppCopy.CommandLineFileName);
        Directory.CreateDirectory(exe + SelfReplacement.OldSuffix);

        Assert.False(SelfReplacement.TryPutBackMissing(exe, () => false));

        Assert.False(File.Exists(exe));
        Assert.False(Directory.Exists(exe));
    }

    [Fact]
    public void With_neither_copy_there_nothing_is_put_back_and_the_setup_is_not_asked()
    {
        var exe = Path.Combine(_root, AppCopy.CommandLineFileName);
        var asked = false;

        Assert.False(SelfReplacement.TryPutBackMissing(exe, () => asked = true));

        Assert.False(asked);
        Assert.False(File.Exists(exe));
    }

    [Fact]
    public void A_setup_is_seen_as_running_while_its_mutex_exists()
    {
        var name = $@"Local\InstallerClean_SetupMutexTest_{Guid.NewGuid():N}";

        Assert.False(SelfReplacement.SetupRunning(name));
        using (new Mutex(false, name))
            Assert.True(SelfReplacement.SetupRunning(name));
        Assert.False(SelfReplacement.SetupRunning(name));
    }

    [Fact]
    public async Task A_hand_over_whose_newer_version_will_not_start_puts_everything_back_and_answers_false()
    {
        // Program() writes no runnable image, so every start fails and the process is never ended.
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        await StagedBesideAsync(exe, 2);

        Assert.False(SelfReplacement.HandOverAndEnd(exe, "9.9.9"));

        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
        Assert.Equal(Program(2), File.ReadAllBytes(exe + SelfReplacement.NewSuffix));
        // One entry for each try: each but the last as it fails, the last once the exe is back.
        Assert.Equal(ProcessStartRetry.Attempts, FailedStartsInCrashLog(exe));
    }

    /// <summary>How many entries in the crash log are a start of <paramref name="exe"/> that Windows refused.</summary>
    private static int FailedStartsInCrashLog(string exe) =>
        TestCrashLog.Text().Split('\n').Count(line =>
            line.StartsWith("System.ComponentModel.Win32Exception", StringComparison.Ordinal)
            && line.Contains(exe, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void A_hand_over_with_nothing_written_beside_the_exe_changes_nothing_and_answers_false()
    {
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));

        Assert.False(SelfReplacement.HandOverAndEnd(exe, "9.9.9"));

        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
    }

    [Fact]
    public void Everything_the_named_assemblies_reference_is_loaded_before_a_rename()
    {
        SelfReplacement.PrepareToMoveAside(typeof(System.Diagnostics.EventLog).Assembly);
        // The walk readies the crash log too, which keeps a header for every later write.
        InstallerClean.Helpers.CrashLog.ForgetPreparedHeader();

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
    public void An_assembly_the_build_does_not_carry_is_passed_over_without_a_crash_log_entry()
    {
        var loaded = SelfReplacement.TryLoad(new System.Reflection.AssemblyName("Ic.Not.In.This.Build, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null"));

        Assert.Null(loaded);
        Assert.Empty(TestCrashLog.Text());
    }

    [Fact]
    public void A_file_not_found_error_counts_as_absence_only_where_it_names_the_assembly_asked_for()
    {
        var asked = new System.Reflection.AssemblyName("Ic.Asked, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");

        Assert.True(SelfReplacement.NamesTheAssemblyAsked(
            new FileNotFoundException("m", "Ic.Asked, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"), asked));
        Assert.False(SelfReplacement.NamesTheAssemblyAsked(
            new FileNotFoundException("m", "Ic.Dependency, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"), asked));
        Assert.False(SelfReplacement.NamesTheAssemblyAsked(new FileNotFoundException("m"), asked));
        Assert.False(SelfReplacement.NamesTheAssemblyAsked(new FileNotFoundException("m", "not an assembly name,,,"), asked));
    }

    [Fact]
    public async Task The_portable_route_downloads_writes_the_newer_version_beside_the_exe_and_leaves_no_staging_folder()
    {
        var exeFolder = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        var stagingRoot = Directory.CreateDirectory(Path.Combine(_root, "programdata")).FullName;
        var exe = Path.Combine(exeFolder, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        var updater = new AppUpdater(AppCopyKind.Portable, exe,
            new UpdateDownloadService(new OneAnswer(Release(2)), null), stagingRoot,
            AppUpdater.WindowSetupArguments, AdministratorsAndThisAccount, NoRegistry);

        var prepared = await updater.PrepareAsync(CoreRelease, null, CancellationToken.None);

        Assert.Equal(exe, prepared.FileToStart);
        Assert.Equal(string.Empty, prepared.Arguments);
        Assert.Equal(exe + SelfReplacement.NewSuffix, prepared.Replacement);
        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.Equal(Release(2), File.ReadAllBytes(exe + SelfReplacement.NewSuffix));
        Assert.Empty(Directory.EnumerateFileSystemEntries(stagingRoot));

        prepared.Undo();

        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.False(File.Exists(exe + SelfReplacement.NewSuffix));
    }

    [Fact]
    public async Task The_portable_routes_undo_keeps_the_newer_version_while_nothing_is_at_the_exe_path()
    {
        var exeFolder = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        var stagingRoot = Directory.CreateDirectory(Path.Combine(_root, "programdata")).FullName;
        var exe = Path.Combine(exeFolder, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        var updater = new AppUpdater(AppCopyKind.Portable, exe,
            new UpdateDownloadService(new OneAnswer(Release(2)), null), stagingRoot,
            AppUpdater.WindowSetupArguments, AdministratorsAndThisAccount, NoRegistry);
        var prepared = await updater.PrepareAsync(CoreRelease, null, CancellationToken.None);
        File.Move(exe, exe + SelfReplacement.OldSuffix);

        prepared.Undo();

        Assert.Equal(Release(2), File.ReadAllBytes(exe + SelfReplacement.NewSuffix));
    }

    [Fact]
    public async Task A_prepared_update_holds_the_clean_up_off_until_its_undo_takes_the_newer_version_away()
    {
        var exeFolder = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        var stagingRoot = Directory.CreateDirectory(Path.Combine(_root, "programdata")).FullName;
        var exe = Path.Combine(exeFolder, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        var gate = new UpdateLeftoverGate();
        var updater = new AppUpdater(AppCopyKind.Portable, exe,
            new UpdateDownloadService(new OneAnswer(Release(2)), null), stagingRoot,
            AppUpdater.WindowSetupArguments, AdministratorsAndThisAccount, NoRegistry, gate);

        var prepared = await updater.PrepareAsync(CoreRelease, null, CancellationToken.None);
        Assert.False(gate.TryRunPass(() => { }));

        prepared.Undo();

        Assert.False(File.Exists(exe + SelfReplacement.NewSuffix));
        Assert.True(gate.TryRunPass(() => { }));
    }

    [Fact]
    public async Task A_prepared_update_whose_undo_keeps_the_newer_version_keeps_the_clean_up_off()
    {
        var exeFolder = Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;
        var stagingRoot = Directory.CreateDirectory(Path.Combine(_root, "programdata")).FullName;
        var exe = Path.Combine(exeFolder, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        var gate = new UpdateLeftoverGate();
        var updater = new AppUpdater(AppCopyKind.Portable, exe,
            new UpdateDownloadService(new OneAnswer(Release(2)), null), stagingRoot,
            AppUpdater.WindowSetupArguments, AdministratorsAndThisAccount, NoRegistry, gate);
        var prepared = await updater.PrepareAsync(CoreRelease, null, CancellationToken.None);
        File.Move(exe, exe + SelfReplacement.OldSuffix);

        prepared.Undo();

        Assert.False(gate.TryRunPass(() => { }));
    }

    [Fact]
    public async Task A_prepared_setup_holds_the_clean_up_off_until_its_undo_deletes_it()
    {
        var stagingRoot = Directory.CreateDirectory(Path.Combine(_root, "programdata")).FullName;
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        var gate = new UpdateLeftoverGate();
        var updater = new AppUpdater(AppCopyKind.Setup, exe,
            new UpdateDownloadService(new OneAnswer(Release(3)), null), stagingRoot,
            AppUpdater.WindowSetupArguments, AdministratorsAndThisAccount, NoRegistry, gate);

        var prepared = await updater.PrepareAsync(CoreRelease, null, CancellationToken.None);
        Assert.False(gate.TryRunPass(() => { }));

        prepared.Undo();

        Assert.False(File.Exists(prepared.FileToStart));
        Assert.True(gate.TryRunPass(() => { }));
    }

    [Fact]
    public async Task The_setup_route_starts_the_downloaded_setup_with_the_apps_switch()
    {
        var stagingRoot = Directory.CreateDirectory(Path.Combine(_root, "programdata")).FullName;
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        var updater = new AppUpdater(AppCopyKind.Setup, exe,
            new UpdateDownloadService(new OneAnswer(Release(3)), null), stagingRoot,
            AppUpdater.WindowSetupArguments, AdministratorsAndThisAccount, NoRegistry);

        var prepared = await updater.PrepareAsync(CoreRelease, null, CancellationToken.None);

        Assert.Equal($"InstallerClean-{CoreRelease}-setup.exe", Path.GetFileName(prepared.FileToStart));
        Assert.StartsWith(stagingRoot, prepared.FileToStart);
        Assert.Contains("/UPDATEFROMAPP", prepared.Arguments);
        Assert.Contains("/SILENT", prepared.Arguments);
        Assert.Equal(Release(3), File.ReadAllBytes(prepared.FileToStart));
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
            new UpdateDownloadService(new OneAnswer(Release(3)), null), stagingRoot,
            CommandLineUpdate.SetupArguments(canAsk: false), AdministratorsAndThisAccount, NoRegistry);

        var prepared = await updater.PrepareAsync(CoreRelease, null, CancellationToken.None);

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
            new UpdateDownloadService(new OneAnswer(Release(2)), null), stagingRoot,
            CommandLineUpdate.SetupArguments(canAsk: false), AdministratorsAndThisAccount, NoRegistry);

        var prepared = await updater.PrepareAsync(CoreRelease, null, CancellationToken.None);

        Assert.Equal(exe, prepared.FileToStart);
        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.Equal(Release(2), File.ReadAllBytes(exe + SelfReplacement.NewSuffix));
        Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
        Assert.Empty(Directory.EnumerateFileSystemEntries(stagingRoot));

        prepared.Undo();

        Assert.False(File.Exists(exe + SelfReplacement.NewSuffix));
    }

#if DEBUG
    /// <summary>
    /// Runs <paramref name="test"/> with the local download switch set to
    /// <paramref name="path"/> and the local setup switch cleared, and puts both back as
    /// they were.
    /// </summary>
    private static async Task WithLocalDownload(string? path, Func<Task> test)
    {
        var download = Environment.GetEnvironmentVariable(AppUpdater.LocalDownloadVariable);
        var setup = Environment.GetEnvironmentVariable(AppUpdater.LocalSetupVariable);
        try
        {
            Environment.SetEnvironmentVariable(AppUpdater.LocalSetupVariable, null);
            Environment.SetEnvironmentVariable(AppUpdater.LocalDownloadVariable, path);
            await test();
        }
        finally
        {
            Environment.SetEnvironmentVariable(AppUpdater.LocalDownloadVariable, download);
            Environment.SetEnvironmentVariable(AppUpdater.LocalSetupVariable, setup);
        }
    }

    /// <summary>An updater for the command line's own download in its own folder, and the exe it updates.</summary>
    private (AppUpdater Updater, string Exe, string StagingRoot) CommandLineUpdater(OneAnswer network)
    {
        var exeFolder = Directory.CreateDirectory(Path.Combine(_root, "tools")).FullName;
        var stagingRoot = Directory.CreateDirectory(Path.Combine(_root, "programdata")).FullName;
        var exe = Path.Combine(exeFolder, AppCopy.CommandLineFileName);
        File.WriteAllBytes(exe, Program(1));
        var updater = new AppUpdater(AppCopyKind.CommandLineDownload, exe,
            new UpdateDownloadService(network, null), stagingRoot,
            CommandLineUpdate.SetupArguments(canAsk: false), AdministratorsAndThisAccount, NoRegistry);
        return (updater, exe, stagingRoot);
    }

    [Fact]
    public async Task In_a_debug_build_the_local_download_switch_stages_that_file_in_place_of_a_download()
    {
        var network = new OneAnswer(Release(2));
        var (updater, exe, stagingRoot) = CommandLineUpdater(network);
        var local = Path.Combine(_root, "local-newer.exe");
        File.WriteAllBytes(local, Release(4));

        await WithLocalDownload(local, async () =>
        {
            var prepared = await updater.PrepareAsync(CoreRelease, null, CancellationToken.None);

            Assert.Equal(0, network.Requests);
            Assert.Equal(Release(4), File.ReadAllBytes(exe + SelfReplacement.NewSuffix));
            Assert.Equal(Release(4), File.ReadAllBytes(local));
            Assert.Empty(Directory.EnumerateFileSystemEntries(stagingRoot));
            prepared.Undo();

            // The control: the same update with the switch cleared downloads.
            Environment.SetEnvironmentVariable(AppUpdater.LocalDownloadVariable, null);
            prepared = await updater.PrepareAsync(CoreRelease, null, CancellationToken.None);

            Assert.Equal(1, network.Requests);
            Assert.Equal(Release(2), File.ReadAllBytes(exe + SelfReplacement.NewSuffix));
            prepared.Undo();
        });
    }

    [Theory]
    [InlineData("not a program")]
    [InlineData("a relative path")]
    [InlineData("a missing file")]
    public async Task In_a_debug_build_a_local_download_the_download_would_refuse_fails_as_a_download(string which)
    {
        var network = new OneAnswer(Program(2));
        var (updater, exe, stagingRoot) = CommandLineUpdater(network);
        var local = Path.Combine(_root, "local-newer.exe");
        File.WriteAllBytes(local, which == "not a program" ? "PK not a program"u8.ToArray() : Program(4));
        var path = which switch
        {
            "a relative path" => Path.GetFileName(local),
            "a missing file" => local + ".missing",
            _ => local,
        };

        await WithLocalDownload(path, async () =>
        {
            var failure = await Assert.ThrowsAsync<UpdateFailedException>(
                () => updater.PrepareAsync("3.5.0", null, CancellationToken.None));

            Assert.Equal(UpdateFailureStage.Download, failure.Stage);
            Assert.Equal(0, network.Requests);
            Assert.False(File.Exists(exe + SelfReplacement.NewSuffix));
            Assert.Empty(Directory.EnumerateFileSystemEntries(stagingRoot));
        });
    }

    [Fact]
    public async Task In_a_debug_build_the_local_download_switch_leaves_the_setup_route_to_download()
    {
        var stagingRoot = Directory.CreateDirectory(Path.Combine(_root, "programdata")).FullName;
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        var local = Path.Combine(_root, "local-newer.exe");
        File.WriteAllBytes(local, Release(4));
        var network = new OneAnswer(Release(3));
        var updater = new AppUpdater(AppCopyKind.Setup, exe,
            new UpdateDownloadService(network, null), stagingRoot,
            AppUpdater.WindowSetupArguments, AdministratorsAndThisAccount, NoRegistry);

        await WithLocalDownload(local, async () =>
        {
            var prepared = await updater.PrepareAsync(CoreRelease, null, CancellationToken.None);

            Assert.Equal(1, network.Requests);
            Assert.Equal(Release(3), File.ReadAllBytes(prepared.FileToStart));
            prepared.Undo();
        });
    }
#endif

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
            AppUpdater.WindowSetupArguments, AdministratorsAndThisAccount, NoRegistry);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => updater.PrepareAsync("3.5.0", null, CancellationToken.None));

        Assert.False(AppCopy.CanUpdateItself(updater.Kind));
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
            AppUpdater.WindowSetupArguments, AdministratorsAndThisAccount, NoRegistry);

        var failure = await Assert.ThrowsAsync<UpdateFailedException>(
            () => updater.PrepareAsync("3.5.0", null, CancellationToken.None));

        Assert.Equal(UpdateFailureStage.Download, failure.Stage);
        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
        Assert.False(File.Exists(exe + SelfReplacement.NewSuffix));
        Assert.Empty(Directory.EnumerateFileSystemEntries(stagingRoot));
    }

    [Theory]
    [InlineData(AppCopyKind.Setup)]
    [InlineData(AppCopyKind.Portable)]
    [InlineData(AppCopyKind.CommandLineDownload)]
    public async Task A_release_that_rules_this_copy_out_is_refused_before_anything_is_downloaded(AppCopyKind kind)
    {
        var stagingRoot = Directory.CreateDirectory(Path.Combine(_root, "programdata")).FullName;
        var exe = Path.Combine(_root, kind == AppCopyKind.CommandLineDownload ? AppCopy.CommandLineFileName : "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        var key = UpdateManifest.DownloadKey(kind);
        var network = new OneAnswer(Release(2),
            "{\"schema\": 1, \"downloads\": {\"" + key + "\": {\"requires\": [\"netfx-4.8\"]}}}");
        var updater = new AppUpdater(kind, exe, new UpdateDownloadService(network, null), stagingRoot,
            AppUpdater.WindowSetupArguments, AdministratorsAndThisAccount, NoRegistry);

        Assert.Equal(UpdateRefusal.NeedsNetFramework48, await updater.CheckRequirementsAsync(CoreRelease, null, CancellationToken.None));
        var refused = await Assert.ThrowsAsync<UpdateRefusedException>(
            () => updater.PrepareAsync(CoreRelease, null, CancellationToken.None));

        Assert.Equal(UpdateRefusal.NeedsNetFramework48, refused.Refusal);
        Assert.Equal(1, network.ManifestRequests);
        Assert.Equal(0, network.Requests);
        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.False(File.Exists(exe + SelfReplacement.NewSuffix));
        Assert.Empty(Directory.EnumerateFileSystemEntries(stagingRoot));
    }

    [Fact]
    public async Task The_releases_file_is_read_once_and_the_PC_is_asked_again_each_time()
    {
        var stagingRoot = Directory.CreateDirectory(Path.Combine(_root, "programdata")).FullName;
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        var network = new OneAnswer(Release(2),
            "{\"schema\": 1, \"downloads\": {\"portable\": {\"requires\": [\"netfx-4.8\"]}}}");
        var registry = NSubstitute.Substitute.For<IRegistryReader>();
        var updater = new AppUpdater(AppCopyKind.Portable, exe, new UpdateDownloadService(network, null), stagingRoot,
            AppUpdater.WindowSetupArguments, AdministratorsAndThisAccount, registry);

        Assert.Equal(UpdateRefusal.NeedsNetFramework48, await updater.CheckRequirementsAsync(CoreRelease, null, CancellationToken.None));
        registry.LocalMachineDwordValue(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new RegistryDwordRead(RegistryDwordState.Read, 528040));
        var prepared = await updater.PrepareAsync(CoreRelease, null, CancellationToken.None);

        Assert.Equal(1, network.ManifestRequests);
        Assert.Equal(1, network.Requests);
        prepared.Undo();
    }

    [Fact]
    public async Task A_release_above_the_last_without_a_file_that_has_none_is_refused()
    {
        var stagingRoot = Directory.CreateDirectory(Path.Combine(_root, "programdata")).FullName;
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        var network = new OneAnswer(Release(2), manifest: null);
        var updater = new AppUpdater(AppCopyKind.Portable, exe, new UpdateDownloadService(network, null), stagingRoot,
            AppUpdater.WindowSetupArguments, AdministratorsAndThisAccount, NoRegistry);
        var above = $"{UpdateManifest.LastVersionWithoutManifest.Major}.{UpdateManifest.LastVersionWithoutManifest.Minor + 1}.0";
        var atOrBelow = UpdateManifest.LastVersionWithoutManifest.ToString(3);

        Assert.Equal(UpdateRefusal.CannotInstallFromThisCopy, await updater.CheckRequirementsAsync(above, null, CancellationToken.None));
        Assert.Null(await updater.CheckRequirementsAsync(atOrBelow, null, CancellationToken.None));
        Assert.Equal(2, network.ManifestRequests);
    }

    [Fact]
    public async Task A_release_with_no_file_is_asked_again_each_time()
    {
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        var network = new OneAnswer(Release(2), manifest: null);
        var updater = new AppUpdater(AppCopyKind.Portable, exe, new UpdateDownloadService(network, null), _root,
            AppUpdater.WindowSetupArguments, AdministratorsAndThisAccount, NoRegistry);

        await updater.CheckRequirementsAsync("9.9.9", null, CancellationToken.None);
        await updater.CheckRequirementsAsync("9.9.9", null, CancellationToken.None);

        Assert.Equal(2, network.ManifestRequests);
    }

    [Fact]
    public async Task A_body_that_is_not_JSON_fails_as_a_read_and_is_not_kept()
    {
        var exe = Path.Combine(_root, "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        var network = new OneAnswer(Release(2), "<html>Sign in to the network</html>");
        var updater = new AppUpdater(AppCopyKind.Portable, exe, new UpdateDownloadService(network, null), _root,
            AppUpdater.WindowSetupArguments, AdministratorsAndThisAccount, NoRegistry);

        for (var i = 0; i < 2; i++)
        {
            var failure = await Assert.ThrowsAsync<UpdateFailedException>(
                () => updater.CheckRequirementsAsync(CoreRelease, null, CancellationToken.None));
            Assert.Equal(UpdateFailureStage.Download, failure.Stage);
        }
        Assert.Equal(2, network.ManifestRequests);
    }

    [Theory]
    [InlineData(AppCopyKind.Setup, true)]
    [InlineData(AppCopyKind.Setup, false)]
    [InlineData(AppCopyKind.Portable, true)]
    [InlineData(AppCopyKind.Portable, false)]
    [InlineData(AppCopyKind.CommandLineDownload, true)]
    [InlineData(AppCopyKind.CommandLineDownload, false)]
    public async Task A_download_that_does_not_carry_the_version_asked_for_is_deleted_and_nothing_is_written_beside_the_exe(
        AppCopyKind kind, bool carriesAnotherVersion)
    {
        var stagingRoot = Directory.CreateDirectory(Path.Combine(_root, "programdata")).FullName;
        var exe = Path.Combine(_root, kind == AppCopyKind.CommandLineDownload ? AppCopy.CommandLineFileName : "InstallerClean.exe");
        File.WriteAllBytes(exe, Program(1));
        // Either the Core library's bytes asked for as the release after the one they carry,
        // or bytes that start as a program and carry no version at all.
        var core = Version.Parse(CoreRelease);
        var asked = $"{core.Major}.{core.Minor + 1}.0";
        var updater = new AppUpdater(kind, exe,
            new UpdateDownloadService(new OneAnswer(carriesAnotherVersion ? Release(2) : Program(2)), null), stagingRoot,
            AppUpdater.WindowSetupArguments, AdministratorsAndThisAccount, NoRegistry);

        var failure = await Assert.ThrowsAsync<UpdateFailedException>(
            () => updater.PrepareAsync(asked, null, CancellationToken.None));

        Assert.Equal(UpdateFailureStage.Install, failure.Stage);
        Assert.Equal(Program(1), File.ReadAllBytes(exe));
        Assert.False(File.Exists(exe + SelfReplacement.NewSuffix));
        Assert.False(File.Exists(exe + SelfReplacement.OldSuffix));
        Assert.Empty(Directory.EnumerateFileSystemEntries(stagingRoot));
    }
}
