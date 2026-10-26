using InstallerClean.Interop.Native;
using InstallerClean.Resources;
using InstallerClean.Services;

namespace InstallerClean.Tests.Services;

/// <summary>
/// The command line's update before its scan: what it asks, what it runs, and what it
/// leaves behind when a part fails. The console, the mutex and the processes are a
/// recording host; the running exe is a real file in a temporary folder, since each
/// route renames it aside. A file's version is read off its text: "old" is the running
/// version and "new" the newer one.
/// </summary>
public sealed class CommandLineUpdateTests : IDisposable
{
    private const string Current = "3.4.0";
    private const string Latest = "3.5.0";

    private readonly string _folder =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ic-cli-update-" + Guid.NewGuid().ToString("N"))).FullName;
    private readonly string _exe;
    private readonly RecordingHost _host;
    private readonly IDisposable _crashLog;

    public CommandLineUpdateTests()
    {
        _exe = Path.Combine(_folder, "installerclean-cli.exe");
        _host = new RecordingHost(_exe);
        File.WriteAllText(_exe, "old");
        _crashLog = TestCrashLog.OwnFolder();
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
        finally { _crashLog.Dispose(); }
    }

    private static Func<CancellationToken, Task<UpdateCheckResult>> Answers(UpdateCheckResult result) =>
        _ => Task.FromResult(result);

    private static readonly Func<CancellationToken, Task<UpdateCheckResult>> NewerOut =
        Answers(new UpdateAvailable(Current, Latest));

    /// <summary>The watched check's bound every run here is given.</summary>
    private static readonly TimeSpan WatchedBound = TimeSpan.FromMilliseconds(200);

    private CommandLineUpdateResult Run(
        FakeUpdater updater, bool canAsk, string command = "/s", string[]? arguments = null,
        Func<CancellationToken, Task<UpdateCheckResult>>? check = null, CancellationToken token = default,
        TimeSpan? controlCBound = null, TimeSpan? readHoldWait = null, TimeSpan? readHoldNotice = null,
        TimeSpan? readHoldWaitAfterCancel = null, Func<string, CancellationToken, Func<bool>>? moveAside = null) =>
        new CommandLineUpdate(check ?? NewerOut, updater, _host, WatchedBound,
                controlCBound ?? TimeSpan.FromMilliseconds(20), readHoldWait, readHoldNotice, readHoldWaitAfterCancel)
            {
                MoveAside = moveAside ?? SelfReplacement.MoveAside,
            }
            .Run(canAsk, command, arguments ?? [command], _exe, token);

    /// <summary>
    /// The separate download: the updater has downloaded the newer file into a folder of its
    /// own, from where the update writes it beside the exe, and then runs
    /// <paramref name="then"/>.
    /// </summary>
    private FakeUpdater Download(Action? then = null) =>
        new(AppCopyKind.CommandLineDownload, DownloadPath, onPrepare: () =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DownloadPath)!);
            File.WriteAllText(DownloadPath, "new");
            then?.Invoke();
        });

    /// <summary>Where the stand-in for the command line's own download saves the newer version.</summary>
    private string DownloadPath => Path.Combine(_folder, "download", AppCopy.CommandLineFileName);

    private FakeUpdater Setup() => new(AppCopyKind.Setup, Path.Combine(_folder, "InstallerClean-3.5.0-setup.exe"));

    /// <summary>The setup writes the newer command line at the exe's path and exits with 0.</summary>
    private void SetupInstalls()
    {
        _host.OnSetup = () =>
        {
            File.WriteAllText(_exe, "new");
            return 0;
        };
    }

    [Fact]
    public void Up_to_date_carries_on_and_prints_nothing()
    {
        var updater = Download();

        var result = Run(updater, canAsk: true, check: Answers(new UpToDate(Current)));

        Assert.Same(CommandLineUpdateResult.NothingTried, result);
        Assert.Empty(_host.Output);
        Assert.False(updater.Prepared);
    }

    [Fact]
    public void A_failed_check_carries_on_and_prints_nothing()
    {
        var result = Run(Download(), canAsk: false,
            check: Answers(new CheckFailed(UpdateCheckFailureReason.NetworkUnavailable)));

        Assert.Same(CommandLineUpdateResult.NothingTried, result);
        Assert.Empty(_host.Output);
    }

    [Fact]
    public void A_watched_check_that_outruns_its_bound_carries_on_and_prints_nothing()
    {
        var result = Run(Download(), canAsk: true,
            check: async ct => { await Task.Delay(Timeout.Infinite, ct); return new UpToDate(Current); });

        Assert.Same(CommandLineUpdateResult.NothingTried, result);
        Assert.Empty(_host.Output);
    }

    [Theory]
    [InlineData("")]
    [InlineData("y")]
    [InlineData("Yes")]
    [InlineData("  Y  ")]
    public void A_watched_yes_installs_and_runs_the_newer_version_as_a_scan(string answer)
    {
        _host.Answers.Enqueue(answer);
        _host.NewerVersionExitCode = 0;

        var result = Run(Download(), canAsk: true);

        var handedOver = Assert.IsType<CommandLineUpdateResult.HandedOver>(result);
        Assert.Equal(new CommandLineUpdateResult.HandedOver(0, Current, Latest, "/s"), handedOver);
        Assert.Equal(new[]
        {
            string.Format(Strings.UpdateNotice_Available, Latest),
            Strings.UpdateNotice_Recommended,
            Prompt(),
            string.Format(Strings.UpdateNotice_Downloading, Latest),
            string.Format(Strings.Cli_UpdateInstalled, Latest),
        }, _host.Output);
        var run = _host.NewerVersionRun!.Value;
        Assert.Equal(_exe, run.File);
        Assert.Equal(new[] { "/s" }, run.Arguments);
        Assert.Equal((Current, Latest), (run.From, run.To));
        Assert.Equal("new", run.ExeHeld);
        Assert.Equal("old", File.ReadAllText(_exe + SelfReplacement.OldSuffix));
        Assert.Equal(new[] { "old" }, _host.PreparedWhileExeHeld);
        Assert.False(Directory.Exists(Path.GetDirectoryName(DownloadPath)));
    }

    [Fact]
    public void The_newer_versions_exit_code_is_the_runs()
    {
        _host.Answers.Enqueue("");
        _host.NewerVersionExitCode = 130;

        var result = Run(Download(), canAsk: true);

        Assert.Equal(130, Assert.IsType<CommandLineUpdateResult.HandedOver>(result).ExitCode);
    }

    [Theory]
    [InlineData("/d", "Cli.UpdateListsOnlyAfterDelete")]
    [InlineData("/m", "Cli.UpdateListsOnlyAfterMove")]
    public void A_watched_yes_after_a_delete_or_a_move_says_the_scan_only_lists_before_it_starts(
        string command, string line)
    {
        _host.Answers.Enqueue("y");
        _host.NewerVersionExitCode = 0;

        Run(Download(), canAsk: true, command: command, arguments: [command, "D:\\Backup"]);

        Assert.Equal(Strings.Get(line), _host.Output[^1]);
        Assert.Equal(new[] { "/s" }, _host.NewerVersionRun!.Value.Arguments);
    }

    [Fact]
    public void On_a_swap_the_installed_line_comes_once_the_newer_version_has_started()
    {
        _host.Answers.Enqueue("y");
        _host.NewerVersionExitCode = 0;

        Run(Download(), canAsk: true, command: "/d", arguments: ["/d", "D:\\Backup"]);

        Assert.Equal(new[]
        {
            string.Format(Strings.Cli_UpdateInstalled, Latest),
            Strings.Cli_UpdateListsOnlyAfterDelete,
        }, _host.Output.Skip(_host.LinesBeforeStart!.Value));
    }

    [Fact]
    public void Ctrl_C_as_the_swapped_in_version_starts_says_it_is_installed_and_nothing_about_a_scan()
    {
        using var cts = new CancellationTokenSource();
        _host.Answers.Enqueue("y");
        _host.NewerVersionExitCode = CommandLineUpdate.ControlCExit;
        _host.OnStarted = cts.Cancel;
        var updater = Download();

        var result = Run(updater, canAsk: true, command: "/d", arguments: ["/d", "D:\\Backup"], token: cts.Token);

        Assert.Equal(
            new CommandLineUpdateResult.HandedOver(CommandLineUpdate.ControlCExit, Current, Latest, "/s"), result);
        Assert.False(updater.Undone);
        Assert.Equal("new", File.ReadAllText(_exe));
        Assert.Equal(string.Format(Strings.Cli_UpdateInstalled, Latest), _host.Output[^1]);
        Assert.DoesNotContain(Strings.Cli_UpdateListsOnlyAfterDelete, _host.Output);
    }

    [Fact]
    public void A_swap_whose_newer_version_will_not_start_never_says_it_is_installed()
    {
        _host.Answers.Enqueue("y");
        _host.NewerVersionExitCode = null;
        var updater = Download();

        var result = Run(updater, canAsk: true, command: "/d", arguments: ["/d", "D:\\Backup"]);

        Assert.Equal(new CommandLineUpdateResult.CarriedOn(UpdateFailureStage.Install, Current, Latest), result);
        Assert.Equal("old", File.ReadAllText(_exe));
        Assert.DoesNotContain(string.Format(Strings.Cli_UpdateInstalled, Latest), _host.Output);
        Assert.DoesNotContain(Strings.Cli_UpdateListsOnlyAfterDelete, _host.Output);
        Assert.Equal(string.Format(Strings.Cli_UpdateRunningInstead, "/d", Current), _host.Output[^1]);
    }

    [Theory]
    [InlineData("n")]
    [InlineData("No")]
    [InlineData(null)]
    public void A_watched_no_or_the_end_of_input_carries_on_with_nothing_downloaded(string? answer)
    {
        _host.Answers.Enqueue(answer);
        var updater = Download();

        var result = Run(updater, canAsk: true);

        Assert.Same(CommandLineUpdateResult.NothingTried, result);
        Assert.False(updater.Prepared);
        Assert.Null(_host.NewerVersionRun);
    }

    [Fact]
    public void Any_other_answer_asks_again()
    {
        _host.Answers.Enqueue("maybe");
        _host.Answers.Enqueue("y");
        _host.NewerVersionExitCode = 0;

        Run(Download(), canAsk: true);

        Assert.Equal(2, _host.Output.Count(line => line == Prompt()));
    }

    [Fact]
    public void Nobody_watching_is_asked_nothing_and_the_newer_version_runs_the_command_as_given()
    {
        _host.NewerVersionExitCode = 2;

        var result = Run(Download(), canAsk: false, command: "/m", arguments: ["/m", "D:\\My Backup"]);

        Assert.Equal(new CommandLineUpdateResult.HandedOver(2, Current, Latest, "/m D:\\My Backup"), result);
        Assert.Equal(new[] { "/m", "D:\\My Backup" }, _host.NewerVersionRun!.Value.Arguments);
        Assert.Equal(0, _host.Reads);
        Assert.Equal(new[]
        {
            string.Format(Strings.UpdateNotice_Available, Latest),
            string.Format(Strings.UpdateNotice_Downloading, Latest),
            string.Format(Strings.Cli_UpdateInstalled, Latest),
        }, _host.Output);
    }

    [Fact]
    public void Ctrl_C_at_the_question_cancels_with_nothing_downloaded()
    {
        using var cts = new CancellationTokenSource();
        _host.OnRead = () => { cts.Cancel(); return null; };
        var updater = Download();

        var result = Run(updater, canAsk: true, token: cts.Token);

        Assert.IsType<CommandLineUpdateResult.Cancelled>(result);
        Assert.False(updater.Prepared);
    }

    [Fact]
    public void A_close_with_the_question_still_waiting_cancels_through_the_token_the_read_is_handed()
    {
        // A console being closed cancels the token and leaves the read waiting; the host's
        // read answers null as the token it was handed is cancelled.
        using var cts = new CancellationTokenSource();
        _host.OnRead = () =>
        {
            Assert.Equal(cts.Token, _host.ReadToken);
            cts.Cancel();
            return _host.ReadToken.IsCancellationRequested ? null : "y";
        };
        var updater = Download();

        var result = Run(updater, canAsk: true, token: cts.Token);

        Assert.IsType<CommandLineUpdateResult.Cancelled>(result);
        Assert.False(updater.Prepared);
    }

    [Fact]
    public void Ctrl_C_answered_just_after_the_read_returns_the_end_of_input_cancels_with_nothing_downloaded()
    {
        // Ctrl+C at the prompt can end the read as the end of input, with the token
        // cancelled on another thread a moment later.
        using var cts = new CancellationTokenSource();
        _host.OnRead = () =>
        {
            cts.CancelAfter(TimeSpan.FromMilliseconds(50));
            return null;
        };
        var updater = Download();
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var result = Run(updater, canAsk: true, token: cts.Token, controlCBound: TimeSpan.FromSeconds(30));

        Assert.IsType<CommandLineUpdateResult.Cancelled>(result);
        Assert.False(updater.Prepared);
        Assert.Null(_host.NewerVersionRun);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), $"The question waited {clock.Elapsed} for the cancellation.");
    }

    [Fact]
    public void A_newer_version_ended_by_Ctrl_C_is_handed_back_once_the_cancellation_has_come()
    {
        // Windows ends a newer version with no handler yet for Ctrl+C, and the token is
        // cancelled on another thread a moment later.
        using var cts = new CancellationTokenSource();
        _host.Answers.Enqueue("y");
        _host.NewerVersionExitCode = CommandLineUpdate.ControlCExit;
        _host.OnNewerVersion = () => cts.CancelAfter(TimeSpan.FromSeconds(1));
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var result = Run(Download(), canAsk: true, token: cts.Token, controlCBound: TimeSpan.FromSeconds(30));

        Assert.Equal(
            new CommandLineUpdateResult.HandedOver(CommandLineUpdate.ControlCExit, Current, Latest, "/s"), result);
        Assert.True(cts.IsCancellationRequested);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), $"The run waited {clock.Elapsed} for the cancellation.");
    }

    [Fact]
    public void A_newer_version_ending_with_the_Ctrl_C_code_and_no_Ctrl_C_is_handed_back_as_it_is()
    {
        _host.Answers.Enqueue("y");
        _host.NewerVersionExitCode = CommandLineUpdate.ControlCExit;

        var result = Run(Download(), canAsk: true);

        Assert.Equal(
            new CommandLineUpdateResult.HandedOver(CommandLineUpdate.ControlCExit, Current, Latest, "/s"), result);
    }

    [Theory]
    [InlineData(1, true, true)]
    [InlineData(1, false, true)]
    [InlineData(2, true, true)]
    [InlineData(2, false, true)]
    [InlineData(2, true, false)]
    [InlineData(2, false, false)]
    public void Ctrl_C_during_either_read_of_the_release_cancels_with_nothing_downloaded_or_refused(
        int cancelledRead, bool canAsk, bool refuses)
    {
        using var cts = new CancellationTokenSource();
        var updater = Download();
        var reads = 0;
        // The first read fails where the second is the one cancelled, so the run reads again.
        updater.Requirements = () =>
        {
            if (++reads < cancelledRead) throw new UpdateFailedException(UpdateFailureStage.Download, "failed");
            cts.Cancel();
            return refuses ? UpdateRefusal.NeedsNetFramework48 : null;
        };
        if (canAsk) _host.Answers.Enqueue("y");

        var result = Run(updater, canAsk, token: cts.Token);

        Assert.IsType<CommandLineUpdateResult.Cancelled>(result);
        Assert.DoesNotContain(Strings.Cli_UpdateNeedsNetFramework48, _host.Output);
        Assert.DoesNotContain(string.Format(Strings.UpdateNotice_Downloading, Latest), _host.Output);
        Assert.False(updater.Prepared);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_read_before_the_question_is_given_the_watched_bound_and_the_updates_own_read_none(bool canAsk)
    {
        var updater = Download();
        var reads = 0;
        updater.Requirements = () => ++reads == 1
            ? throw new UpdateFailedException(UpdateFailureStage.Download, "failed")
            : null;
        if (canAsk) _host.Answers.Enqueue("y");
        _host.NewerVersionExitCode = 0;

        Run(updater, canAsk);

        Assert.Equal(canAsk ? [WatchedBound, null] : new TimeSpan?[] { null, null }, updater.RequirementLimits);
    }

    [Fact]
    public void A_refusal_that_comes_back_with_Ctrl_C_pressed_ends_the_run_as_cancelled()
    {
        using var cts = new CancellationTokenSource();
        var updater = new FakeUpdater(AppCopyKind.CommandLineDownload, DownloadPath, onPrepare: () =>
        {
            cts.Cancel();
            throw new UpdateRefusedException(UpdateRefusal.NeedsNetFramework48, Latest);
        });

        var result = Run(updater, canAsk: false, token: cts.Token);

        Assert.IsType<CommandLineUpdateResult.Cancelled>(result);
        Assert.DoesNotContain(Strings.Cli_UpdateNeedsNetFramework48, _host.Output);
    }

    [Fact]
    public void Ctrl_C_during_the_download_cancels_and_starts_nothing()
    {
        using var cts = new CancellationTokenSource();
        var updater = new FakeUpdater(AppCopyKind.CommandLineDownload, DownloadPath, onPrepare: () =>
        {
            cts.Cancel();
            cts.Token.ThrowIfCancellationRequested();
        });

        var result = Run(updater, canAsk: false, token: cts.Token);

        Assert.IsType<CommandLineUpdateResult.Cancelled>(result);
        Assert.Null(_host.NewerVersionRun);
        Assert.False(_host.HoldsLock);
    }

    [Theory]
    [InlineData(UpdateFailureStage.Download, "UpdateNotice.DownloadFailed")]
    [InlineData(UpdateFailureStage.Install, "UpdateNotice.InstallFailed")]
    public void A_failed_download_or_install_says_so_and_carries_on_with_this_version(
        UpdateFailureStage stage, string line)
    {
        var updater = new FakeUpdater(AppCopyKind.CommandLineDownload, DownloadPath,
            onPrepare: () => throw new UpdateFailedException(stage, "failed"));

        var result = Run(updater, canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.CarriedOn(stage, Current, Latest), result);
        Assert.Equal(Strings.Get(line), _host.Output[^2]);
        Assert.Equal(string.Format(Strings.Cli_UpdateRunningInstead, "/s", Current), _host.Output[^1]);
        Assert.Null(_host.NewerVersionRun);
        Assert.False(_host.HoldsLock);
        Assert.Equal("old", File.ReadAllText(_exe));
        Assert.False(File.Exists(_exe + SelfReplacement.OldSuffix));
        Assert.Empty(_host.PreparedWhileExeHeld);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_failed_download_whose_lines_the_console_refuses_carries_on_with_one_crash_log_entry(bool refusesRunningInstead)
    {
        var updater = new FakeUpdater(AppCopyKind.CommandLineDownload, DownloadPath,
            onPrepare: () => throw new UpdateFailedException(UpdateFailureStage.Download, "failed"));
        var refused = refusesRunningInstead
            ? string.Format(Strings.Cli_UpdateRunningInstead, "/s", Current)
            : Strings.UpdateNotice_DownloadFailed;
        _host.OnWriteLine = line =>
        {
            if (line == refused) throw new IOException("the console refused it");
        };
        var result = Run(updater, canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.CarriedOn(UpdateFailureStage.Download, Current, Latest), result);
        Assert.DoesNotContain("the console refused it", TestCrashLog.OnlyEntry());
    }

    [Fact]
    public void A_closing_line_that_fails_other_than_as_a_console_refusal_is_written_after_the_runs_entry()
    {
        var updater = new FakeUpdater(AppCopyKind.CommandLineDownload, DownloadPath,
            onPrepare: () => throw new UpdateFailedException(UpdateFailureStage.Download, "failed"));
        _host.OnWriteLine = line =>
        {
            if (line == Strings.UpdateNotice_DownloadFailed) throw new InvalidOperationException("the host's own fault");
        };

        var result = Run(updater, canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.CarriedOn(UpdateFailureStage.Download, Current, Latest), result);
        AssertRunEntryThenHostFault(typeof(UpdateFailedException).FullName!);
    }

    /// <summary>
    /// The crash log holds the entry the run wrote for how it ended, naming
    /// <paramref name="runEntry"/>, and after it the host's own fault, which the run's
    /// entry does not carry.
    /// </summary>
    private static void AssertRunEntryThenHostFault(string runEntry)
    {
        var entries = TestCrashLog.Entries();
        Assert.Equal(2, entries.Count);
        Assert.Contains(runEntry, entries[0]);
        Assert.DoesNotContain("the host's own fault", entries[0]);
        Assert.Contains("the host's own fault", entries[1]);
    }

    /// <summary>The line giving the newer version's release page.</summary>
    private static string ReleaseLink => string.Format(Strings.Cli_UpdateDownloadLink,
        Strings.UpdateNotice_Download, "https://github.com/no-faff/InstallerClean/releases/tag/v" + Latest);

    /// <summary>
    /// The lines for a refusal: why, then Microsoft's .NET Framework 4.8 installer where that
    /// is why, and the release page otherwise.
    /// </summary>
    private static string[] RefusalLines(UpdateRefusal refusal) => refusal == UpdateRefusal.NeedsNetFramework48
        ? [
            string.Format(Strings.UpdateNotice_Available, Latest),
            Strings.Cli_UpdateNeedsNetFramework48,
            string.Format(Strings.Cli_UpdateDownloadLink,
                Strings.UpdateNotice_DownloadNetFramework48, "https://go.microsoft.com/fwlink/?linkid=2088631"),
        ]
        : [
            string.Format(Strings.UpdateNotice_Available, Latest),
            string.Format(Strings.UpdateNotice_CannotInstallAutomatically, Latest),
            ReleaseLink,
        ];

    [Theory]
    [InlineData(UpdateRefusal.NeedsNetFramework48, true)]
    [InlineData(UpdateRefusal.NeedsNetFramework48, false)]
    [InlineData(UpdateRefusal.CannotInstallFromThisCopy, true)]
    [InlineData(UpdateRefusal.CannotInstallFromThisCopy, false)]
    public void A_release_that_rules_this_copy_out_says_why_and_where_to_go_without_asking(
        UpdateRefusal refusal, bool canAsk)
    {
        var updater = Download();
        updater.Requirements = () => refusal;

        var result = Run(updater, canAsk);

        Assert.Equal(CommandLineUpdateResult.NothingTried, result);
        Assert.Equal(RefusalLines(refusal), _host.Output);
        Assert.False(updater.Prepared);
        Assert.Equal(0, _host.Reads);
        Assert.Null(_host.NewerVersionRun);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_release_whose_file_cannot_be_read_goes_on_to_the_question_and_reads_it_again(bool canAsk)
    {
        var updater = Download();
        var reads = 0;
        updater.Requirements = () => ++reads == 1
            ? throw new UpdateFailedException(UpdateFailureStage.Download, "failed")
            : null;
        _host.Answers.Enqueue("y");
        _host.NewerVersionExitCode = 0;

        Run(updater, canAsk);

        Assert.Equal(2, updater.RequirementReads);
        Assert.Equal(canAsk ? 1 : 0, _host.Reads);
        Assert.True(updater.Prepared);
    }

    [Fact]
    public void A_release_whose_file_still_cannot_be_read_fails_as_a_download_and_carries_on()
    {
        var updater = Download();
        updater.Requirements = () => throw new UpdateFailedException(UpdateFailureStage.Download, "failed");

        var result = Run(updater, canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.CarriedOn(UpdateFailureStage.Download, Current, Latest), result);
        Assert.False(updater.Prepared);
        Assert.Equal(Strings.UpdateNotice_DownloadFailed, _host.Output[^2]);
    }

    [Fact]
    public void A_refusal_from_the_updates_own_read_says_why_before_any_download_and_starts_nothing()
    {
        var updater = Download();
        var reads = 0;
        updater.Requirements = () => ++reads == 1
            ? throw new UpdateFailedException(UpdateFailureStage.Download, "failed")
            : UpdateRefusal.NeedsNetFramework48;

        var result = Run(updater, canAsk: false);

        Assert.Equal(CommandLineUpdateResult.NothingTried, result);
        Assert.Equal(RefusalLines(UpdateRefusal.NeedsNetFramework48), _host.Output);
        Assert.Null(_host.NewerVersionRun);
        Assert.False(updater.Prepared);
        Assert.True(_host.HoldsOwnFile);
        Assert.False(_host.HoldsLock);
    }

    [Fact]
    public void A_refusal_from_the_updates_own_read_after_the_question_names_the_version_again()
    {
        var updater = Download();
        var reads = 0;
        updater.Requirements = () => ++reads == 1
            ? throw new UpdateFailedException(UpdateFailureStage.Download, "failed")
            : UpdateRefusal.NeedsNetFramework48;
        _host.Answers.Enqueue("y");

        var result = Run(updater, canAsk: true);

        Assert.Equal(CommandLineUpdateResult.NothingTried, result);
        Assert.Equal(1, _host.Reads);
        Assert.Equal(RefusalLines(UpdateRefusal.NeedsNetFramework48), _host.Output[^3..]);
        Assert.Null(_host.NewerVersionRun);
        Assert.False(updater.Prepared);
    }

    [Fact]
    public void An_undetermined_copy_says_it_cannot_tell_and_gives_the_release_page()
    {
        var updater = new FakeUpdater(AppCopyKind.Undetermined, _exe);

        var result = Run(updater, canAsk: true);

        Assert.Equal(CommandLineUpdateResult.NothingTried, result);
        Assert.Equal(new[]
        {
            string.Format(Strings.UpdateNotice_Available, Latest),
            Strings.UpdateNotice_CopyCannotTell,
            ReleaseLink,
        }, _host.Output);
        Assert.Equal(0, updater.RequirementReads);
        Assert.False(updater.Prepared);
    }

    [Fact]
    public void A_newer_version_that_will_not_start_is_put_back_and_the_run_carries_on()
    {
        _host.NewerVersionExitCode = null;
        var updater = Download();

        var result = Run(updater, canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.CarriedOn(UpdateFailureStage.Install, Current, Latest), result);
        Assert.True(updater.Undone);
        Assert.Equal("old", File.ReadAllText(_exe));
        Assert.False(File.Exists(_exe + SelfReplacement.OldSuffix));
        Assert.Equal(string.Format(Strings.Cli_UpdateRunningInstead, "/s", Current), _host.Output[^1]);
    }

    [Fact]
    public void A_newer_version_that_will_not_start_and_cannot_be_moved_out_of_the_way_ends_the_run_as_installed()
    {
        _host.NewerVersionExitCode = null;
        _host.OnNewerVersion = () => Directory.CreateDirectory(_exe + SelfReplacement.NewSuffix);

        var result = Run(Download(), canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.NotStarted(Current, Latest), result);
        Assert.Equal("new", File.ReadAllText(_exe));
        Assert.Equal(string.Format(Strings.Cli_UpdateNotStarted, Latest), _host.Output[^1]);
    }

    [Fact]
    public void A_newer_version_whose_start_throws_is_put_back_and_the_run_carries_on()
    {
        _host.OnNewerVersion = () => throw new InvalidOperationException("thrown before the start");
        var updater = Download();

        var result = Run(updater, canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.CarriedOn(UpdateFailureStage.Install, Current, Latest), result);
        Assert.True(updater.Undone);
        Assert.Equal("old", File.ReadAllText(_exe));
        Assert.False(File.Exists(_exe + SelfReplacement.OldSuffix));
        Assert.True(_host.HoldsOwnFile);
        Assert.Equal(string.Format(Strings.Cli_UpdateRunningInstead, "/s", Current), _host.Output[^1]);
    }

    [Fact]
    public void A_swapped_in_version_that_has_started_is_not_put_back_whatever_its_installed_line_does()
    {
        _host.NewerVersionExitCode = 0;
        _host.OnWriteLine = line =>
        {
            if (line == string.Format(Strings.Cli_UpdateInstalled, Latest)) throw new IOException("the console refused it");
        };
        var updater = Download();

        var result = Run(updater, canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.HandedOver(0, Current, Latest, "/s"), result);
        Assert.False(updater.Undone);
        Assert.Equal("new", File.ReadAllText(_exe));
        Assert.NotNull(_host.NewerVersionRun);
    }

    [Fact]
    public void A_setup_whose_newer_version_has_gone_from_the_path_before_it_starts_puts_this_exe_back_and_runs_nothing()
    {
        // The run has said the newer version is installed and that it will only list, so it
        // ends rather than running the command with this exe.
        SetupInstalls();
        _host.NewerVersionExitCode = null;
        _host.OnNewerVersion = () => File.Delete(_exe);
        _host.Answers.Enqueue("y");

        var result = Run(Setup(), canAsk: true, command: "/d");

        var stopped = Assert.IsType<CommandLineUpdateResult.Stopped>(result);
        Assert.Equal("old", File.ReadAllText(_exe));
        Assert.False(File.Exists(_exe + SelfReplacement.OldSuffix));
        Assert.Equal(Strings.UpdateNotice_InstallFailed, _host.Output[^1]);
        Assert.Equal(nameof(UpdateFailedException), stopped.FailureType);
        var entry = TestCrashLog.OnlyEntry();
        Assert.Contains("The run ended. The running copy was put back at the command line's path.", entry);
        Assert.Contains("The newer version could not be started.", entry);
    }

    [Fact]
    public void Ctrl_C_as_a_setups_newer_version_goes_from_the_path_puts_this_exe_back_and_names_both_in_the_crash_log()
    {
        using var cts = new CancellationTokenSource();
        SetupInstalls();
        _host.NewerVersionExitCode = null;
        _host.OnNewerVersion = () => File.Delete(_exe);
        _host.OnNotStarted = cts.Cancel;

        var result = Run(Setup(), canAsk: false, token: cts.Token);

        Assert.IsType<CommandLineUpdateResult.Stopped>(result);
        Assert.Equal("old", File.ReadAllText(_exe));
        Assert.Equal(Strings.UpdateNotice_InstallFailed, _host.Output[^1]);
        var entry = TestCrashLog.OnlyEntry();
        Assert.Contains("A failure ended the run and Ctrl+C came as well. The running copy was put back at the command line's path.", entry);
        Assert.Contains("The newer version could not be started.", entry);
    }

    [Fact]
    public void A_console_that_refuses_the_installed_line_after_the_setup_still_starts_the_newer_version()
    {
        SetupInstalls();
        _host.NewerVersionExitCode = 0;
        _host.OnWriteLine = line =>
        {
            if (line == string.Format(Strings.Cli_UpdateInstalled, Latest)) throw new IOException("the console refused it");
        };

        var result = Run(Setup(), canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.HandedOver(0, Current, Latest, "/s"), result);
        Assert.Equal("new", File.ReadAllText(_exe));
        Assert.NotNull(_host.NewerVersionRun);
        Assert.DoesNotContain(string.Format(Strings.Cli_UpdateNotStarted, Latest), _host.Output);
        Assert.Empty(TestCrashLog.Entries());
    }

    [Theory]
    [InlineData("/d")]
    [InlineData("/m")]
    public void A_refused_installed_line_after_the_setup_still_says_the_newer_version_only_lists(string command)
    {
        _host.Answers.Enqueue("y");
        SetupInstalls();
        _host.NewerVersionExitCode = 0;
        _host.OnWriteLine = line =>
        {
            if (line == string.Format(Strings.Cli_UpdateInstalled, Latest)) throw new IOException("the console refused it");
        };

        var result = Run(Setup(), canAsk: true, command: command, arguments: [command, "D:\\Backup"]);

        Assert.Equal(new CommandLineUpdateResult.HandedOver(0, Current, Latest, "/s"), result);
        Assert.Contains(
            command == "/d" ? Strings.Cli_UpdateListsOnlyAfterDelete : Strings.Cli_UpdateListsOnlyAfterMove,
            _host.Output);
        Assert.Equal(new[] { "/s" }, _host.NewerVersionRun!.Value.Arguments);
        Assert.Empty(TestCrashLog.Entries());
    }

    [Theory]
    [InlineData(AppCopyKind.Setup, false)]
    [InlineData(AppCopyKind.Setup, true)]
    [InlineData(AppCopyKind.CommandLineDownload, false)]
    [InlineData(AppCopyKind.CommandLineDownload, true)]
    public void An_installed_line_that_fails_still_leaves_the_line_saying_a_delete_only_lists(AppCopyKind kind, bool hostFault)
    {
        _host.Answers.Enqueue("y");
        if (kind == AppCopyKind.Setup) SetupInstalls();
        _host.NewerVersionExitCode = 0;
        _host.OnWriteLine = line =>
        {
            if (line != string.Format(Strings.Cli_UpdateInstalled, Latest)) return;
            if (hostFault) throw new InvalidOperationException("a fault in the host");
            throw new IOException("the console refused it");
        };

        var result = Run(kind == AppCopyKind.Setup ? Setup() : Download(), canAsk: true, command: "/d",
            arguments: ["/d", "D:\\Backup"]);

        Assert.Equal(new CommandLineUpdateResult.HandedOver(0, Current, Latest, "/s"), result);
        Assert.Contains(Strings.Cli_UpdateListsOnlyAfterDelete, _host.Output);
        if (hostFault) Assert.Contains("a fault in the host", TestCrashLog.OnlyEntry());
        else Assert.Empty(TestCrashLog.Entries());
    }

    [Fact]
    public void A_console_that_refuses_every_line_after_the_setup_still_runs_the_scan_a_delete_answered_yes_to()
    {
        _host.Answers.Enqueue("y");
        SetupInstalls();
        _host.NewerVersionExitCode = 0;
        var refused = new List<string>();
        _host.OnWriteLine = line =>
        {
            if (line == string.Format(Strings.Cli_UpdateInstalled, Latest)
                || line == Strings.Cli_UpdateListsOnlyAfterDelete)
            {
                refused.Add(line);
                throw new IOException("the console refused it");
            }
        };

        var result = Run(Setup(), canAsk: true, command: "/d", arguments: ["/d", "D:\\Backup"]);

        Assert.Equal(new CommandLineUpdateResult.HandedOver(0, Current, Latest, "/s"), result);
        Assert.Equal(new[] { string.Format(Strings.Cli_UpdateInstalled, Latest), Strings.Cli_UpdateListsOnlyAfterDelete }, refused);
        Assert.Equal(new[] { "/s" }, _host.NewerVersionRun!.Value.Arguments);
        Assert.Empty(TestCrashLog.Entries());
    }

    [Fact]
    public void A_setups_newer_version_whose_start_throws_is_left_in_place_and_the_run_ends_not_started()
    {
        SetupInstalls();
        _host.OnNewerVersion = () => throw new InvalidOperationException("thrown before the start");

        var result = Run(Setup(), canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.NotStarted(Current, Latest), result);
        Assert.Equal("new", File.ReadAllText(_exe));
        Assert.Equal(string.Format(Strings.Cli_UpdateNotStarted, Latest), _host.Output[^1]);
        Assert.Contains("thrown before the start", TestCrashLog.OnlyEntry());
    }

    [Fact]
    public void A_line_after_the_setup_that_fails_other_than_as_a_console_refusal_goes_to_the_crash_log_and_the_newer_version_still_starts()
    {
        SetupInstalls();
        _host.NewerVersionExitCode = 0;
        _host.OnWriteLine = line =>
        {
            if (line == string.Format(Strings.Cli_UpdateInstalled, Latest)) throw new InvalidOperationException("a fault in the host");
        };

        var result = Run(Setup(), canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.HandedOver(0, Current, Latest, "/s"), result);
        Assert.NotNull(_host.NewerVersionRun);
        Assert.Contains("a fault in the host", TestCrashLog.OnlyEntry());
    }

    [Fact]
    public void A_console_that_refuses_every_line_after_a_failed_setup_ends_the_run_with_nothing_run()
    {
        _host.OnSetup = () =>
        {
            File.WriteAllText(_exe, "partial");
            Directory.CreateDirectory(_exe + SelfReplacement.NewSuffix);
            return 1;
        };
        _host.OnWriteLine = line =>
        {
            if (line == Strings.UpdateNotice_InstallFailed) throw new IOException("the console refused it");
        };

        var result = Run(Setup(), canAsk: false);

        // The setup's failure is what ended the run, and the line the console refused is not.
        Assert.Equal(nameof(UpdateFailedException), Assert.IsType<CommandLineUpdateResult.Stopped>(result).FailureType);
        Assert.Null(_host.NewerVersionRun);
        // The run's entry is the last, and the refused line writes none of its own.
        var entries = TestCrashLog.Entries();
        Assert.Contains("The setup ended with exit code 1.", entries[^1]);
        Assert.DoesNotContain(entries, entry => entry.Contains("the console refused it"));
    }

    [Fact]
    public void A_throw_after_the_setup_once_Ctrl_C_has_come_leaves_the_newer_version_installed_and_starts_nothing()
    {
        using var cts = new CancellationTokenSource();
        SetupInstalls();
        _host.Answers.Enqueue("y");
        _host.NewerVersionExitCode = 0;
        _host.OnWriteLine = line =>
        {
            if (line != Strings.Cli_UpdateListsOnlyAfterDelete) return;
            cts.Cancel();
            throw new IOException("the console refused it");
        };

        var result = Run(Setup(), canAsk: true, command: "/d", token: cts.Token);

        Assert.Equal(new CommandLineUpdateResult.UpdatedThenCancelled(Current, Latest), result);
        Assert.Single(_host.Output, string.Format(Strings.Cli_UpdateInstalled, Latest));
        Assert.Equal("new", File.ReadAllText(_exe));
        Assert.Null(_host.NewerVersionRun);
    }

    [Theory]
    [InlineData(AppCopyKind.CommandLineDownload, true)]
    [InlineData(AppCopyKind.CommandLineDownload, false)]
    [InlineData(AppCopyKind.Setup, true)]
    [InlineData(AppCopyKind.Setup, false)]
    public void Where_the_mutex_is_held_elsewhere_nothing_is_asked_or_downloaded(AppCopyKind kind, bool canAsk)
    {
        _host.LockAvailable = false;
        var updater = kind == AppCopyKind.Setup ? Setup() : Download();

        var result = Run(updater, canAsk);

        Assert.Equal(new CommandLineUpdateResult.CarriedOn(UpdateFailureStage.InUse, Current, Latest), result);
        Assert.Equal(InUseLines(), _host.Output);
        Assert.Equal(0, _host.Reads);
        Assert.False(updater.Prepared);
        Assert.Equal("old", File.ReadAllText(_exe));
        Assert.True(_host.HoldsOwnFile);
    }

    [Theory]
    [InlineData(AppCopyKind.CommandLineDownload)]
    [InlineData(AppCopyKind.Setup)]
    public void Where_another_run_of_this_file_is_alive_nothing_is_asked_or_downloaded(AppCopyKind kind)
    {
        _host.AnotherRunAt = _ => true;
        var updater = kind == AppCopyKind.Setup ? Setup() : Download();

        var result = Run(updater, canAsk: true);

        Assert.Equal(new CommandLineUpdateResult.CarriedOn(UpdateFailureStage.InUse, Current, Latest), result);
        Assert.Equal(InUseLines(), _host.Output);
        Assert.Equal(0, _host.Reads);
        Assert.False(updater.Prepared);
        Assert.False(_host.HoldsLock);
    }

    [Theory]
    [InlineData(AppCopyKind.CommandLineDownload)]
    [InlineData(AppCopyKind.Setup)]
    public void A_run_of_this_file_that_starts_during_the_download_stops_the_update_before_the_rename(AppCopyKind kind)
    {
        // The look before the download finds nothing; the one just before the rename finds
        // a run.
        _host.AnotherRunAt = look => look >= 2;
        var updater = kind == AppCopyKind.Setup ? Setup() : Download();

        var result = Run(updater, canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.CarriedOn(UpdateFailureStage.InUse, Current, Latest), result);
        Assert.Equal(new[]
        {
            string.Format(Strings.UpdateNotice_Available, Latest),
            string.Format(Strings.UpdateNotice_Downloading, Latest),
            Strings.Cli_UpdateInUse,
            string.Format(Strings.Cli_UpdateRunningInstead, "/s", Current),
        }, _host.Output);
        Assert.True(updater.Undone);
        Assert.Equal("old", File.ReadAllText(_exe));
        Assert.False(File.Exists(_exe + SelfReplacement.OldSuffix));
        Assert.Null(_host.SetupRun);
        Assert.Null(_host.NewerVersionRun);
        Assert.True(_host.HoldsOwnFile);
    }

    [Theory]
    [InlineData(true, UpdateFailureStage.InUse)]
    [InlineData(false, UpdateFailureStage.Install)]
    public void A_rename_refused_is_in_use_only_where_another_run_of_this_file_is_alive(
        bool anotherRunAfterwards, UpdateFailureStage stage)
    {
        // A handle that does not share delete is what a run holding its own file has open.
        using var held = File.Open(_exe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        _host.AnotherRunAt = look => anotherRunAfterwards && look >= 3;
        var updater = Download();

        var result = Run(updater, canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.CarriedOn(stage, Current, Latest), result);
        Assert.Equal(3, _host.AnotherRunLooks);
        Assert.True(updater.Undone);
        Assert.Equal("old", File.ReadAllText(_exe));
        Assert.False(File.Exists(_exe + SelfReplacement.OldSuffix));
        Assert.True(_host.HoldsOwnFile);
    }

    [Theory]
    [InlineData(AppCopyKind.CommandLineDownload)]
    [InlineData(AppCopyKind.Setup)]
    public void Ctrl_C_while_the_rename_waits_for_this_exe_to_be_let_go_cancels_with_nothing_renamed(AppCopyKind kind)
    {
        // A handle that does not share delete, as another program holding the file has open.
        using var held = File.Open(_exe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var cts = new CancellationTokenSource();
        _host.OnReleaseOwnFile = () => cts.CancelAfter(300);
        var updater = kind == AppCopyKind.Setup ? Setup() : Download();

        var result = Run(updater, canAsk: false, token: cts.Token);

        Assert.IsType<CommandLineUpdateResult.Cancelled>(result);
        Assert.True(updater.Undone);
        Assert.Equal("old", File.ReadAllText(_exe));
        Assert.False(File.Exists(_exe + SelfReplacement.OldSuffix));
        Assert.True(_host.HoldsOwnFile);
        Assert.Null(_host.SetupRun);
        Assert.Null(_host.NewerVersionRun);
    }

    [Fact]
    public void The_rename_comes_with_the_mutex_held_and_this_runs_own_file_let_go()
    {
        _host.NewerVersionExitCode = 0;

        Run(Download(), canAsk: false);

        Assert.Equal(new[] { true }, _host.LockHeldAtPrepare);
        Assert.Equal(2, _host.AnotherRunLooks);
        Assert.False(_host.NewerVersionRun!.Value.OwnFileHeld);
    }

    [Fact]
    public void The_setups_copy_runs_the_setup_with_this_exe_renamed_aside_and_no_mutex_held()
    {
        SetupInstalls();
        _host.NewerVersionExitCode = 0;
        var updater = Setup();

        var result = Run(updater, canAsk: false);

        Assert.IsType<CommandLineUpdateResult.HandedOver>(result);
        Assert.Equal(updater.FileToStart, _host.SetupRun!.Value.File);
        Assert.Equal(new[] { "/VERYSILENT", "/SP-" }, _host.SetupRun!.Value.Arguments);
        Assert.False(_host.SetupRun!.Value.HeldLock);
        Assert.True(_host.SetupRun!.Value.ExeWasAside);
        Assert.Equal("old", File.ReadAllText(_exe + SelfReplacement.OldSuffix));
        Assert.Equal("new", File.ReadAllText(_exe));
        Assert.True(updater.Undone);
        Assert.Equal(_exe, _host.NewerVersionRun!.Value.File);
        Assert.Equal(new[] { "old" }, _host.PreparedWhileExeHeld);
    }

    [Fact]
    public void A_setups_newer_version_that_will_not_start_ends_the_run_as_installed_with_nothing_put_back()
    {
        SetupInstalls();
        _host.NewerVersionExitCode = null;

        var result = Run(Setup(), canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.NotStarted(Current, Latest), result);
        Assert.Equal("new", File.ReadAllText(_exe));
        Assert.Equal(string.Format(Strings.Cli_UpdateNotStarted, Latest), _host.Output[^1]);
        Assert.DoesNotContain(string.Format(Strings.Cli_UpdateRunningInstead, "/s", Current), _host.Output);
    }

    [Fact]
    public void A_setup_that_leaves_a_file_without_the_newer_version_has_it_moved_aside_and_the_run_carries_on()
    {
        _host.OnSetup = () =>
        {
            File.WriteAllText(_exe, "unreadable");
            return 0;
        };

        var result = Run(Setup(), canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.CarriedOn(UpdateFailureStage.Install, Current, Latest), result);
        Assert.Equal("old", File.ReadAllText(_exe));
        Assert.Equal("unreadable", File.ReadAllText(_exe + SelfReplacement.NewSuffix));
        Assert.Null(_host.NewerVersionRun);
    }

    [Fact]
    public void A_console_that_refuses_the_not_started_line_keeps_the_failed_start_as_the_runs_entry()
    {
        SetupInstalls();
        _host.NewerVersionExitCode = null;
        _host.OnWriteLine = line =>
        {
            if (line == string.Format(Strings.Cli_UpdateNotStarted, Latest)) throw new IOException("the console refused it");
        };

        var result = Run(Setup(), canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.NotStarted(Current, Latest), result);
        var entry = TestCrashLog.OnlyEntry();
        Assert.Contains("The newer version could not be started.", entry);
        Assert.DoesNotContain("the console refused it", entry);
    }

    [Fact]
    public void A_not_started_line_that_fails_keeps_the_run_not_started_and_its_entry_naming_the_failed_start()
    {
        SetupInstalls();
        _host.NewerVersionExitCode = null;
        var tries = 0;
        _host.OnWriteLine = line =>
        {
            if (line != string.Format(Strings.Cli_UpdateNotStarted, Latest)) return;
            tries++;
            throw new InvalidOperationException("the host's own fault");
        };

        var result = Run(Setup(), canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.NotStarted(Current, Latest), result);
        Assert.Equal(1, tries);
        AssertRunEntryThenHostFault("The newer version could not be started.");
    }

    [Fact]
    public void A_stopped_runs_closing_line_that_fails_keeps_the_run_stopped_and_its_entry_naming_the_failed_start()
    {
        SetupInstalls();
        _host.NewerVersionExitCode = null;
        _host.OnNewerVersion = () => File.Delete(_exe);
        var tries = 0;
        _host.OnWriteLine = line =>
        {
            if (line != Strings.UpdateNotice_InstallFailed) return;
            tries++;
            throw new InvalidOperationException("the host's own fault");
        };

        var result = Run(Setup(), canAsk: false);

        var stopped = Assert.IsType<CommandLineUpdateResult.Stopped>(result);
        Assert.Equal(nameof(UpdateFailedException), stopped.FailureType);
        Assert.Equal("old", File.ReadAllText(_exe));
        Assert.Equal(1, tries);
        AssertRunEntryThenHostFault("The run ended. The running copy was put back at the command line's path.");
        Assert.Contains("The newer version could not be started.", TestCrashLog.Entries()[0]);
    }

    [Fact]
    public void A_setup_whose_newer_version_cannot_be_read_afterwards_is_left_in_place_with_nothing_run()
    {
        var reads = 0;
        SetupInstalls();
        _host.OnSetup = () =>
        {
            File.WriteAllText(_exe, "new");
            _host.OnFileVersion = () =>
            {
                reads++;
                throw new IOException("the version could not be read");
            };
            return 0;
        };

        var result = Run(Setup(), canAsk: false);

        Assert.IsType<CommandLineUpdateResult.Stopped>(result);
        Assert.Equal(1, reads);
        Assert.Equal("new", File.ReadAllText(_exe));
        Assert.Null(_host.NewerVersionRun);
        Assert.Equal(Strings.Cli_UpdateNotChecked, _host.Output[^1]);
        var entry = TestCrashLog.OnlyEntry();
        Assert.Contains("could not be read after the setup", entry);
        Assert.Contains("the version could not be read", entry);
    }

    /// <summary>
    /// What the host's read of a file another process holds sharing nothing for reading
    /// throws on Windows: an <see cref="IOException"/> carrying ERROR_SHARING_VIOLATION.
    /// </summary>
    private static IOException SharingViolation() =>
        new("The process cannot access the file because it is being used by another process.",
            unchecked((int)0x80070000) | Kernel32.ERROR_SHARING_VIOLATION);

    /// <summary>What a read of a file another process has locked part of throws on Windows.</summary>
    private static IOException LockViolation() =>
        new("The process cannot access the file because another process has locked a portion of the file.",
            unchecked((int)0x80070000) | Kernel32.ERROR_LOCK_VIOLATION);

    [Fact]
    public void A_setup_whose_newer_version_is_locked_for_a_moment_runs_it_once_let_go()
    {
        _host.NewerVersionExitCode = 0;
        var refused = 0;
        _host.OnSetup = () =>
        {
            File.WriteAllText(_exe, "new");
            _host.OnFileVersion = () =>
            {
                if (refused++ < 2) throw LockViolation();
            };
            return 0;
        };

        var result = Run(Setup(), canAsk: false, readHoldWait: TimeSpan.FromSeconds(10),
            readHoldNotice: TimeSpan.FromSeconds(10));

        Assert.Equal(new CommandLineUpdateResult.HandedOver(0, Current, Latest, "/s"), result);
        Assert.Equal(3, refused);
    }

    [Fact]
    public void Ctrl_C_while_the_setups_newer_version_is_held_ends_the_wait_with_it_left_in_place_and_nothing_run()
    {
        using var cts = new CancellationTokenSource();
        _host.OnSetup = () =>
        {
            File.WriteAllText(_exe, "new");
            _host.OnFileVersion = () =>
            {
                cts.Cancel();
                throw SharingViolation();
            };
            return 0;
        };
        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        var result = Run(Setup(), canAsk: false, token: cts.Token, readHoldWait: TimeSpan.FromSeconds(30),
            readHoldNotice: TimeSpan.FromSeconds(30));

        Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(10));
        Assert.IsType<CommandLineUpdateResult.Stopped>(result);
        Assert.Equal("new", File.ReadAllText(_exe));
        Assert.Null(_host.NewerVersionRun);
        Assert.Equal(Strings.Cli_UpdateNotChecked, _host.Output[^1]);
        var entry = TestCrashLog.OnlyEntry();
        Assert.Contains("Ctrl+C ended the run, and a failure came as it ended.", entry);
        Assert.Contains("could not be read after the setup", entry);
    }

    [Fact]
    public void A_console_closing_as_the_setups_newer_version_is_held_reads_it_once_and_ends_with_nothing_run()
    {
        using var cts = new CancellationTokenSource();
        var refused = 0;
        _host.OnSetup = () =>
        {
            File.WriteAllText(_exe, "new");
            _host.Closing = true;
            _host.ClosedFirst = true;
            cts.Cancel();
            _host.OnFileVersion = () =>
            {
                refused++;
                throw SharingViolation();
            };
            return 0;
        };

        var result = Run(Setup(), canAsk: false, token: cts.Token, readHoldWait: TimeSpan.FromSeconds(30),
            readHoldNotice: TimeSpan.FromSeconds(30));

        Assert.IsType<CommandLineUpdateResult.Stopped>(result);
        Assert.Equal(1, refused);
        Assert.Equal("new", File.ReadAllText(_exe));
        Assert.Equal(Strings.Cli_UpdateNotChecked, _host.Output[^1]);
        Assert.Contains("The console closing or Windows shutting down ended the run, and a failure came as it ended.", TestCrashLog.OnlyEntry());
    }

    [Fact]
    public void Ctrl_C_during_the_setup_then_a_hold_that_outlasts_the_wait_ends_with_the_newer_version_unchecked()
    {
        using var cts = new CancellationTokenSource();
        var refused = 0;
        _host.OnSetup = () =>
        {
            File.WriteAllText(_exe, "new");
            cts.Cancel();
            _host.OnFileVersion = () =>
            {
                refused++;
                throw SharingViolation();
            };
            return 0;
        };

        var result = Run(Setup(), canAsk: false, token: cts.Token, readHoldWait: TimeSpan.FromMilliseconds(500),
            readHoldNotice: TimeSpan.FromMilliseconds(100));

        Assert.IsType<CommandLineUpdateResult.Stopped>(result);
        Assert.True(refused > 1);
        Assert.Contains(string.Format(Strings.Cli_UpdateWaitingForFile, Path.GetFileName(_exe)), _host.Output);
        Assert.Equal(Strings.Cli_UpdateNotChecked, _host.Output[^1]);
        Assert.Contains("A failure ended the run and Ctrl+C came as well.", TestCrashLog.OnlyEntry());
    }

    [Fact]
    public void Ctrl_C_during_the_setup_then_a_read_that_fails_outright_names_the_read_as_what_ended_the_run()
    {
        using var cts = new CancellationTokenSource();
        _host.OnSetup = () =>
        {
            File.WriteAllText(_exe, "new");
            cts.Cancel();
            _host.OnFileVersion = () => throw new UnauthorizedAccessException("access denied");
            return 0;
        };

        var result = Run(Setup(), canAsk: false, token: cts.Token);

        Assert.IsType<CommandLineUpdateResult.Stopped>(result);
        Assert.Equal(Strings.Cli_UpdateNotChecked, _host.Output[^1]);
        Assert.Contains("A failure ended the run and Ctrl+C came as well.", TestCrashLog.OnlyEntry());
    }

    [Fact]
    public void A_setups_confirmed_newer_version_that_will_not_start_and_is_then_held_ends_as_installed_and_not_started()
    {
        SetupInstalls();
        _host.NewerVersionExitCode = null;
        var readsAfterStart = 0;
        _host.OnNewerVersion = () => _host.OnFileVersion = () =>
        {
            readsAfterStart++;
            throw SharingViolation();
        };

        var result = Run(Setup(), canAsk: false, readHoldWait: TimeSpan.FromMilliseconds(300),
            readHoldNotice: TimeSpan.FromSeconds(30));

        Assert.Equal(new CommandLineUpdateResult.NotStarted(Current, Latest), result);
        Assert.Equal(0, readsAfterStart);
        Assert.Equal("new", File.ReadAllText(_exe));
        Assert.Equal(string.Format(Strings.Cli_UpdateNotStarted, Latest), _host.Output[^1]);
    }

    [Fact]
    public void The_console_closing_part_way_through_a_wait_that_Ctrl_C_came_before_ends_it_at_once_and_names_Ctrl_C()
    {
        using var cts = new CancellationTokenSource();
        var refused = 0;
        _host.OnSetup = () =>
        {
            File.WriteAllText(_exe, "new");
            cts.Cancel();
            _host.OnFileVersion = () =>
            {
                if (++refused == 3) _host.Closing = true;
                throw SharingViolation();
            };
            return 0;
        };

        var result = Run(Setup(), canAsk: false, token: cts.Token, readHoldWait: TimeSpan.FromSeconds(30),
            readHoldNotice: TimeSpan.FromSeconds(30));

        Assert.IsType<CommandLineUpdateResult.Stopped>(result);
        Assert.Equal(3, refused);
        Assert.Equal(Strings.Cli_UpdateNotChecked, _host.Output[^1]);
        Assert.Contains("Ctrl+C ended the run, and a failure came as it ended.", TestCrashLog.OnlyEntry());
    }

    [Fact]
    public void A_close_that_came_first_ends_a_wait_before_the_runs_token_is_cancelled_and_is_named()
    {
        _host.OnSetup = () =>
        {
            File.WriteAllText(_exe, "new");
            _host.Closing = true;
            _host.ClosedFirst = true;
            _host.OnFileVersion = () => throw SharingViolation();
            return 0;
        };

        var result = Run(Setup(), canAsk: false);

        Assert.IsType<CommandLineUpdateResult.Stopped>(result);
        Assert.Equal(Strings.Cli_UpdateNotChecked, _host.Output[^1]);
        Assert.Contains("The console closing or Windows shutting down ended the run, and a failure came as it ended.", TestCrashLog.OnlyEntry());
    }

    [Fact]
    public void A_setup_that_left_another_version_at_the_path_says_the_update_failed_though_a_later_read_is_held()
    {
        var reads = 0;
        _host.OnSetup = () =>
        {
            File.WriteAllText(_exe, "partial");
            Directory.CreateDirectory(_exe + SelfReplacement.NewSuffix);
            _host.OnFileVersion = () =>
            {
                if (reads++ > 0) throw SharingViolation();
            };
            return 0;
        };

        var result = Run(Setup(), canAsk: false, readHoldWait: TimeSpan.FromMilliseconds(300),
            readHoldNotice: TimeSpan.FromSeconds(30));

        Assert.IsType<CommandLineUpdateResult.Stopped>(result);
        Assert.True(reads > 1);
        Assert.Equal(Strings.UpdateNotice_InstallFailed, _host.Output[^1]);
    }

    [Fact]
    public void Ctrl_C_during_the_setup_bounds_the_wait_for_a_held_newer_version_by_the_shorter_bound()
    {
        using var cts = new CancellationTokenSource();
        var refused = 0;
        _host.OnSetup = () =>
        {
            File.WriteAllText(_exe, "new");
            cts.Cancel();
            _host.OnFileVersion = () =>
            {
                refused++;
                throw SharingViolation();
            };
            return 0;
        };

        var result = Run(Setup(), canAsk: false, token: cts.Token, readHoldWait: TimeSpan.FromSeconds(30),
            readHoldNotice: TimeSpan.FromSeconds(30), readHoldWaitAfterCancel: TimeSpan.FromMilliseconds(300));

        // Reads come every 100 ms: about 4 in the 300 ms bound, and about 20 in the built-in one.
        Assert.InRange(refused, 2, 12);
        Assert.IsType<CommandLineUpdateResult.Stopped>(result);
        Assert.Equal(Strings.Cli_UpdateNotChecked, _host.Output[^1]);
    }

    [Fact]
    public void The_console_closing_during_the_setup_names_the_closing_in_the_entry_where_the_read_fails_outright()
    {
        using var cts = new CancellationTokenSource();
        _host.OnSetup = () =>
        {
            File.WriteAllText(_exe, "new");
            _host.Closing = true;
            _host.ClosedFirst = true;
            cts.Cancel();
            _host.OnFileVersion = () => throw new UnauthorizedAccessException("access denied");
            return 0;
        };

        var result = Run(Setup(), canAsk: false, token: cts.Token);

        Assert.IsType<CommandLineUpdateResult.Stopped>(result);
        Assert.Contains("A failure ended the run and the console closing or Windows shutting down came as well.",
            TestCrashLog.OnlyEntry());
    }

    [Fact]
    public void A_close_while_Ctrl_C_is_still_being_answered_leaves_Ctrl_C_named_as_what_ended_the_run()
    {
        _host.OnSetup = () =>
        {
            File.WriteAllText(_exe, "new");
            _host.Closing = true;
            _host.ClosedFirst = false;
            _host.OnFileVersion = () => throw SharingViolation();
            return 0;
        };

        var result = Run(Setup(), canAsk: false);

        Assert.IsType<CommandLineUpdateResult.Stopped>(result);
        Assert.Contains("Ctrl+C ended the run, and a failure came as it ended.", TestCrashLog.OnlyEntry());
    }

    [Fact]
    public void Ctrl_C_during_the_setup_still_waits_out_a_brief_hold_on_its_newer_version()
    {
        using var cts = new CancellationTokenSource();
        var refused = 0;
        _host.OnSetup = () =>
        {
            File.WriteAllText(_exe, "new");
            cts.Cancel();
            _host.OnFileVersion = () =>
            {
                if (refused++ < 3) throw SharingViolation();
            };
            return 0;
        };

        var result = Run(Setup(), canAsk: false, token: cts.Token, readHoldWait: TimeSpan.FromSeconds(10),
            readHoldNotice: TimeSpan.FromSeconds(10));

        Assert.Equal(new CommandLineUpdateResult.UpdatedThenCancelled(Current, Latest), result);
        Assert.Equal("new", File.ReadAllText(_exe));
        Assert.True(refused > 3);
    }

    [Fact]
    public void A_setup_whose_newer_version_is_held_sharing_nothing_for_a_moment_runs_it_once_let_go()
    {
        _host.NewerVersionExitCode = 0;
        var refused = 0;
        _host.OnSetup = () =>
        {
            File.WriteAllText(_exe, "new");
            _host.OnFileVersion = () =>
            {
                if (refused++ < 3) throw SharingViolation();
            };
            return 0;
        };

        var result = Run(Setup(), canAsk: false, readHoldWait: TimeSpan.FromSeconds(10),
            readHoldNotice: TimeSpan.FromSeconds(10));

        Assert.Equal(new CommandLineUpdateResult.HandedOver(0, Current, Latest, "/s"), result);
        Assert.Equal("new", File.ReadAllText(_exe));
        Assert.NotNull(_host.NewerVersionRun);
        Assert.Equal(4, refused);
        Assert.DoesNotContain(_host.Output, line => line.Contains("Waiting for another program"));
    }

    [Fact]
    public void A_setup_whose_newer_version_stays_held_sharing_nothing_says_so_once_and_is_left_in_place_with_nothing_run()
    {
        var refused = 0;
        _host.OnSetup = () =>
        {
            File.WriteAllText(_exe, "new");
            _host.OnFileVersion = () =>
            {
                refused++;
                throw SharingViolation();
            };
            return 0;
        };

        var result = Run(Setup(), canAsk: false, readHoldWait: TimeSpan.FromMilliseconds(600),
            readHoldNotice: TimeSpan.FromMilliseconds(200));

        Assert.IsType<CommandLineUpdateResult.Stopped>(result);
        Assert.Equal("new", File.ReadAllText(_exe));
        Assert.False(File.Exists(_exe + SelfReplacement.NewSuffix));
        Assert.Null(_host.NewerVersionRun);
        Assert.True(refused > 1);
        Assert.Single(_host.Output, string.Format(Strings.Cli_UpdateWaitingForFile, Path.GetFileName(_exe)));
        Assert.Equal(Strings.Cli_UpdateNotChecked, _host.Output[^1]);
        var entry = TestCrashLog.OnlyEntry();
        Assert.Contains("could not be read after the setup", entry);
        Assert.Contains("being used by another process", entry);
    }

    [Fact]
    public void A_setup_that_fails_with_this_exe_unable_to_go_back_ends_the_run_with_nothing_run()
    {
        _host.OnSetup = () =>
        {
            File.WriteAllText(_exe, "partial");
            Directory.CreateDirectory(_exe + SelfReplacement.NewSuffix);
            return 1;
        };

        var result = Run(Setup(), canAsk: false);

        var stopped = Assert.IsType<CommandLineUpdateResult.Stopped>(result);
        Assert.Equal(nameof(UpdateFailedException), stopped.FailureType);
        Assert.Equal(Strings.UpdateNotice_InstallFailed, _host.Output[^1]);
        Assert.Null(_host.NewerVersionRun);
        // What the setup left stays at the path, this exe beside it and the folder at .new.
        Assert.Equal("partial", File.ReadAllText(_exe));
        Assert.True(File.Exists(_exe + SelfReplacement.OldSuffix));
        Assert.True(Directory.Exists(_exe + SelfReplacement.NewSuffix));
        // The put-back's refused rename onto that folder is written first, then the run's entry.
        var entries = TestCrashLog.Entries();
        var rename = entries.ToList().FindIndex(entry => entry.Contains("SelfReplacement.TryRename"));
        var run = entries.ToList().FindIndex(entry => entry.Contains("The setup ended with exit code 1."));
        Assert.InRange(rename, 0, run - 1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(null)]
    public void A_setup_that_fails_or_will_not_start_puts_this_exe_back(int? exitCode)
    {
        _host.OnSetup = () => exitCode;
        var updater = Setup();

        var result = Run(updater, canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.CarriedOn(UpdateFailureStage.Install, Current, Latest), result);
        Assert.Equal("old", File.ReadAllText(_exe));
        Assert.False(File.Exists(_exe + SelfReplacement.OldSuffix));
        Assert.True(updater.Undone);
        Assert.Null(_host.NewerVersionRun);
        Assert.True(_host.HoldsOwnFile);
    }

    [Fact]
    public void A_setup_that_reports_success_without_the_newer_command_line_is_never_run()
    {
        _host.OnSetup = () => 0;
        var updater = Setup();

        var result = Run(updater, canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.CarriedOn(UpdateFailureStage.Install, Current, Latest), result);
        Assert.Equal("old", File.ReadAllText(_exe));
        Assert.Null(_host.NewerVersionRun);
    }

    [Theory]
    [InlineData("/s")]
    [InlineData("/d")]
    public void Ctrl_C_while_the_setup_runs_says_the_newer_version_is_installed_and_starts_nothing(string command)
    {
        using var cts = new CancellationTokenSource();
        _host.OnSetup = () =>
        {
            File.WriteAllText(_exe, "new");
            cts.Cancel();
            return 0;
        };
        _host.Answers.Enqueue("y");

        var result = Run(Setup(), canAsk: true, command: command, token: cts.Token);

        Assert.Equal(new CommandLineUpdateResult.UpdatedThenCancelled(Current, Latest), result);
        Assert.Equal(string.Format(Strings.Cli_UpdateInstalled, Latest), _host.Output[^1]);
        Assert.Equal("new", File.ReadAllText(_exe));
        Assert.Null(_host.NewerVersionRun);
    }

    [Fact]
    public void A_setup_that_fails_after_Ctrl_C_says_so_puts_this_exe_back_and_runs_nothing()
    {
        using var cts = new CancellationTokenSource();
        _host.OnSetup = () =>
        {
            cts.Cancel();
            return 1;
        };
        var updater = Setup();

        var result = Run(updater, canAsk: false, token: cts.Token);

        Assert.Equal(new CommandLineUpdateResult.FailedThenCancelled(UpdateFailureStage.Install, Current, Latest), result);
        Assert.Equal(Strings.UpdateNotice_InstallFailed, _host.Output[^1]);
        Assert.DoesNotContain(string.Format(Strings.Cli_UpdateRunningInstead, "/s", Current), _host.Output);
        Assert.Equal("old", File.ReadAllText(_exe));
        Assert.False(File.Exists(_exe + SelfReplacement.OldSuffix));
        Assert.True(updater.Undone);
        Assert.True(_host.HoldsOwnFile);
        Assert.Null(_host.NewerVersionRun);
    }

    [Fact]
    public void A_download_that_fails_after_Ctrl_C_says_so_and_runs_nothing()
    {
        using var cts = new CancellationTokenSource();
        var updater = new FakeUpdater(AppCopyKind.CommandLineDownload, DownloadPath, onPrepare: () =>
        {
            cts.Cancel();
            throw new UpdateFailedException(UpdateFailureStage.Download, "failed");
        });

        var result = Run(updater, canAsk: false, token: cts.Token);

        Assert.Equal(new CommandLineUpdateResult.FailedThenCancelled(UpdateFailureStage.Download, Current, Latest), result);
        Assert.Equal(Strings.UpdateNotice_DownloadFailed, _host.Output[^1]);
        Assert.False(_host.HoldsLock);
    }

    [Fact]
    public void A_run_of_this_file_found_just_before_the_rename_after_Ctrl_C_says_so_and_changes_nothing()
    {
        using var cts = new CancellationTokenSource();
        // The second look is the one just before the rename.
        _host.AnotherRunAt = look =>
        {
            if (look < 2) return false;
            cts.Cancel();
            return true;
        };
        var updater = Download();

        var result = Run(updater, canAsk: false, token: cts.Token);

        Assert.Equal(new CommandLineUpdateResult.FailedThenCancelled(UpdateFailureStage.InUse, Current, Latest), result);
        Assert.Equal(Strings.Cli_UpdateInUse, _host.Output[^1]);
        Assert.True(updater.Undone);
        Assert.False(_host.OwnFileReleased);
        Assert.Equal("old", File.ReadAllText(_exe));
    }

    [Fact]
    public void InstallerClean_found_in_use_before_the_question_after_Ctrl_C_asks_nothing_and_runs_nothing()
    {
        using var cts = new CancellationTokenSource();
        _host.AnotherRunAt = _ =>
        {
            cts.Cancel();
            return true;
        };
        var updater = Download();

        var result = Run(updater, canAsk: true, token: cts.Token);

        Assert.Equal(new CommandLineUpdateResult.FailedThenCancelled(UpdateFailureStage.InUse, Current, Latest), result);
        Assert.Equal(new[] { string.Format(Strings.UpdateNotice_Available, Latest), Strings.Cli_UpdateInUse }, _host.Output);
        Assert.Equal(0, _host.Reads);
        Assert.False(updater.Prepared);
    }

    [Theory]
    [InlineData(AppCopyKind.CommandLineDownload)]
    [InlineData(AppCopyKind.Setup)]
    public void Ctrl_C_after_the_download_and_before_the_rename_takes_it_away_and_changes_nothing(AppCopyKind kind)
    {
        using var cts = new CancellationTokenSource();
        var updater = kind == AppCopyKind.Setup
            ? new FakeUpdater(kind, Path.Combine(_folder, "InstallerClean-3.5.0-setup.exe"), onPrepare: cts.Cancel)
            : Download(cts.Cancel);

        var result = Run(updater, canAsk: false, token: cts.Token);

        Assert.IsType<CommandLineUpdateResult.Cancelled>(result);
        Assert.True(updater.Undone);
        Assert.False(_host.OwnFileReleased);
        Assert.Equal("old", File.ReadAllText(_exe));
        Assert.False(File.Exists(_exe + SelfReplacement.OldSuffix));
        Assert.True(_host.HoldsOwnFile);
        Assert.False(_host.HoldsLock);
        Assert.Null(_host.SetupRun);
        Assert.Null(_host.NewerVersionRun);
    }

    [Theory]
    [InlineData(AppCopyKind.CommandLineDownload)]
    [InlineData(AppCopyKind.Setup)]
    public void Ctrl_C_once_this_exe_is_renamed_and_before_anything_starts_puts_it_back(AppCopyKind kind)
    {
        using var cts = new CancellationTokenSource();
        // The last call before the rename, after the token was last read.
        _host.OnReleaseOwnFile = cts.Cancel;
        var updater = kind == AppCopyKind.Setup ? Setup() : Download();

        var result = Run(updater, canAsk: false, token: cts.Token);

        Assert.IsType<CommandLineUpdateResult.Cancelled>(result);
        Assert.True(updater.Undone);
        Assert.Equal("old", File.ReadAllText(_exe));
        Assert.False(File.Exists(_exe + SelfReplacement.OldSuffix));
        Assert.True(_host.HoldsOwnFile);
        Assert.Null(_host.SetupRun);
        Assert.Null(_host.NewerVersionRun);
        Assert.DoesNotContain(string.Format(Strings.Cli_UpdateInstalled, Latest), _host.Output);
    }

    [Fact]
    public void Ctrl_C_after_the_swap_where_this_exe_cannot_go_back_says_the_newer_version_is_installed()
    {
        File.WriteAllText(_exe, "new");
        var update = new CommandLineUpdate(NewerOut, Download(), _host);
        var prepared = new PreparedUpdate("9.9.9", _exe, string.Empty, () => { }, _exe + SelfReplacement.NewSuffix);

        var result = update.CancelAfterSwap(prepared, _exe, Current, Latest, putBack: () => false);

        Assert.Equal(new CommandLineUpdateResult.UpdatedThenCancelled(Current, Latest), result);
        Assert.Equal(string.Format(Strings.Cli_UpdateInstalled, Latest), Assert.Single(_host.Output));
    }

    [Fact]
    public void Ctrl_C_after_the_swap_where_the_file_at_the_path_cannot_be_read_names_both_in_the_crash_log()
    {
        File.WriteAllText(_exe, "new");
        _host.OnFileVersion = () => throw new IOException("the version could not be read");
        var update = new CommandLineUpdate(NewerOut, Download(), _host);
        var prepared = new PreparedUpdate("9.9.9", _exe, string.Empty, () => { }, _exe + SelfReplacement.NewSuffix);

        var result = update.CancelAfterSwap(prepared, _exe, Current, Latest, putBack: () => false);

        Assert.IsType<CommandLineUpdateResult.Stopped>(result);
        Assert.Equal(Strings.UpdateNotice_InstallFailed, _host.Output[^1]);
        var entry = TestCrashLog.OnlyEntry();
        Assert.Contains("Ctrl+C ended the run, and a failure came as it ended.", entry);
        Assert.Contains("the version could not be read", entry);
    }

    [Fact]
    public void Ctrl_C_after_the_swap_where_this_exe_cannot_go_back_ends_as_installed_when_the_console_refuses_the_line()
    {
        File.WriteAllText(_exe, "new");
        _host.OnWriteLine = line =>
        {
            if (line == string.Format(Strings.Cli_UpdateInstalled, Latest)) throw new IOException("the console refused it");
        };
        var update = new CommandLineUpdate(NewerOut, Download(), _host);
        var prepared = new PreparedUpdate("9.9.9", _exe, string.Empty, () => { }, _exe + SelfReplacement.NewSuffix);

        var result = update.CancelAfterSwap(prepared, _exe, Current, Latest, putBack: () => false);

        Assert.Equal(new CommandLineUpdateResult.UpdatedThenCancelled(Current, Latest), result);
        Assert.Equal("new", File.ReadAllText(_exe));
    }

    [Fact]
    public void Ctrl_C_after_the_swap_with_neither_version_at_the_path_ends_the_run_with_nothing_run()
    {
        File.Delete(_exe);
        var update = new CommandLineUpdate(NewerOut, Download(), _host);
        var prepared = new PreparedUpdate("9.9.9", _exe, string.Empty, () => { }, _exe + SelfReplacement.NewSuffix);

        var result = update.CancelAfterSwap(prepared, _exe, Current, Latest, putBack: () => false);

        Assert.Equal(nameof(UpdateFailedException), Assert.IsType<CommandLineUpdateResult.Stopped>(result).FailureType);
        Assert.Equal(Strings.UpdateNotice_InstallFailed, Assert.Single(_host.Output));
    }

    [Fact]
    public void A_swap_that_fails_with_nothing_at_the_exe_path_puts_the_newer_version_there_and_starts_nothing()
    {
        _host.OnReleaseOwnFile = () => File.Delete(_exe);
        var updater = Download();

        var result = Run(updater, canAsk: false);

        Assert.Equal(new CommandLineUpdateResult.NotStarted(Current, Latest), result);
        Assert.False(updater.Undone);
        Assert.Equal("new", File.ReadAllText(_exe));
        Assert.Equal(string.Format(Strings.Cli_UpdateNotStarted, Latest), _host.Output[^1]);
        Assert.Null(_host.NewerVersionRun);
    }

    [Fact]
    public void A_setup_copy_whose_rename_aside_fails_with_nothing_left_at_the_path_removes_its_download_and_tries_again_as_it_ends()
    {
        // The rename aside fails after this exe has gone from its path, and the copy aside is
        // held so it cannot go back: the try straight after the failure and the one as the run
        // ends each wait out the hold and are written to crash.log.
        FileStream? held = null;
        var updater = Setup();
        try
        {
            var result = Run(updater, canAsk: false, moveAside: (path, _) =>
            {
                File.Move(path, path + SelfReplacement.OldSuffix);
                held = new FileStream(path + SelfReplacement.OldSuffix, FileMode.Open, FileAccess.Read, FileShare.None);
                throw new IOException("the rename aside failed");
            });

            Assert.IsType<CommandLineUpdateResult.Stopped>(result);
            Assert.True(updater.Undone);
            Assert.Null(_host.NewerVersionRun);
            Assert.False(File.Exists(_exe));
            Assert.True(File.Exists(_exe + SelfReplacement.OldSuffix));
            Assert.Contains(TestCrashLog.Entries(), entry => entry.Contains("The running copy was left renamed aside."));
            Assert.Equal(2, TestCrashLog.Entries().Count(entry => entry.Contains(nameof(FileHeldOpenException))));
        }
        finally
        {
            held?.Dispose();
        }
    }

    [Fact]
    public void A_failed_setup_that_leaves_nothing_at_the_path_renames_no_file_this_run_did_not_write_there()
    {
        // The renamed exe is gone, so putting it back fails, and a file sits beside the path
        // under the name the copies that replace their own file write the newer version to.
        _host.OnSetup = () =>
        {
            File.Delete(_exe + SelfReplacement.OldSuffix);
            File.WriteAllText(_exe + SelfReplacement.NewSuffix, "new");
            return 1;
        };

        var result = Run(Setup(), canAsk: false);

        Assert.IsType<CommandLineUpdateResult.Stopped>(result);
        Assert.False(File.Exists(_exe));
        Assert.Equal("new", File.ReadAllText(_exe + SelfReplacement.NewSuffix));
    }

    [Fact]
    public void Ctrl_C_after_the_swap_where_this_exe_will_not_go_back_puts_the_newer_version_at_the_path()
    {
        // A folder in place of the renamed exe refuses every rename of it.
        File.Delete(_exe);
        Directory.CreateDirectory(_exe + SelfReplacement.OldSuffix);
        File.WriteAllText(_exe + SelfReplacement.NewSuffix, "new");
        var update = new CommandLineUpdate(NewerOut, Download(), _host);
        var prepared = new PreparedUpdate("9.9.9", _exe, string.Empty, () => { }, _exe + SelfReplacement.NewSuffix);

        var result = update.CancelAfterSwap(prepared, _exe, Current, Latest, putBack: () => false);

        Assert.Equal(new CommandLineUpdateResult.UpdatedThenCancelled(Current, Latest), result);
        Assert.Equal("new", File.ReadAllText(_exe));
    }

    [Fact]
    public void Ctrl_C_before_the_swapped_in_version_starts_puts_this_exe_back_and_starts_nothing()
    {
        using var cts = new CancellationTokenSource();
        _host.NewerVersionExitCode = 0;
        _host.OnNewerVersion = cts.Cancel;
        var updater = Download();

        var result = Run(updater, canAsk: false, token: cts.Token);

        Assert.IsType<CommandLineUpdateResult.Cancelled>(result);
        Assert.True(updater.Undone);
        Assert.Equal("old", File.ReadAllText(_exe));
        Assert.False(File.Exists(_exe + SelfReplacement.OldSuffix));
        Assert.True(_host.HoldsOwnFile);
        Assert.DoesNotContain(string.Format(Strings.Cli_UpdateInstalled, Latest), _host.Output);
        Assert.Null(_host.NewerVersionRun);
    }

    [Fact]
    public void Ctrl_C_before_the_newer_version_from_the_setup_starts_leaves_it_installed_and_starts_nothing()
    {
        using var cts = new CancellationTokenSource();
        SetupInstalls();
        _host.NewerVersionExitCode = 0;
        _host.OnNewerVersion = cts.Cancel;
        var updater = Setup();

        var result = Run(updater, canAsk: false, token: cts.Token);

        Assert.Equal(new CommandLineUpdateResult.UpdatedThenCancelled(Current, Latest), result);
        Assert.Equal(string.Format(Strings.Cli_UpdateInstalled, Latest), _host.Output[^1]);
        Assert.Equal("new", File.ReadAllText(_exe));
        Assert.Null(_host.NewerVersionRun);
    }

    [Fact]
    public void A_build_from_source_names_the_release_page_and_asks_nothing()
    {
        var updater = new FakeUpdater(AppCopyKind.FromSource, _exe);

        var result = Run(updater, canAsk: true);

        Assert.Same(CommandLineUpdateResult.NothingTried, result);
        Assert.Equal(0, _host.Reads);
        Assert.False(updater.Prepared);
        Assert.Equal(new[]
        {
            string.Format(Strings.UpdateNotice_Available, Latest),
            Strings.UpdateNotice_Recommended,
            ReleaseLink,
        }, _host.Output);
    }

    [Fact]
    public void The_mutex_is_taken_and_released_on_the_calling_thread_and_never_held_once_anything_starts()
    {
        _host.NewerVersionExitCode = 0;
        var thread = Environment.CurrentManagedThreadId;

        Run(Download(), canAsk: false);

        Assert.Equal(new[] { thread }, _host.LockThreads.Distinct());
        Assert.Equal(2, _host.LockThreads.Count);
        Assert.False(_host.NewerVersionHeldLock);
    }

    [Theory]
    [InlineData(true, "/SILENT")]
    [InlineData(false, "/VERYSILENT")]
    public void The_setup_waits_for_the_app_never_opens_the_window_and_shows_progress_only_to_a_person(bool canAsk, string shown)
    {
        var arguments = CommandLineUpdate.SetupArguments(canAsk).Split(' ');

        Assert.Equal(shown, arguments[0]);
        Assert.Contains("/UPDATEFROMCLI", arguments);
        Assert.DoesNotContain("/UPDATEFROMAPP", arguments);
        Assert.Contains("/SUPPRESSMSGBOXES", arguments);
        Assert.Contains("/NOCLOSEAPPLICATIONS", arguments);
    }

    /// <summary>What a run prints where InstallerClean is in use before anything is asked.</summary>
    private static string[] InUseLines() =>
    [
        string.Format(Strings.UpdateNotice_Available, Latest),
        Strings.Cli_UpdateInUse,
        string.Format(Strings.Cli_UpdateRunningInstead, "/s", Current),
    ];

    private static string Prompt() =>
        string.Format(Strings.Cli_UpdatePrompt,
            Strings.Cli_UpdateAnswerYes.ToUpper(System.Globalization.CultureInfo.CurrentUICulture),
            Strings.Cli_UpdateAnswerNo.ToLower(System.Globalization.CultureInfo.CurrentUICulture)) + " ";

    private sealed class FakeUpdater(AppCopyKind kind, string fileToStart, Action? onPrepare = null) : IAppUpdater
    {
        public AppCopyKind Kind => kind;
        public ScoopUpdate? ScoopUpdate => null;

        /// <summary>
        /// What the release's update.json answers; a throw stands for a read that failed. A
        /// cancel of the token, before or during the answer, is answered as the cancel, as
        /// the real check does.
        /// </summary>
        public Func<UpdateRefusal?> Requirements { get; set; } = () => null;
        public int RequirementReads { get; private set; }

        /// <summary>The time limits the reads were given, in order.</summary>
        public List<TimeSpan?> RequirementLimits { get; } = [];

        public Task<UpdateRefusal?> CheckRequirementsAsync(
            string version, TimeSpan? within, CancellationToken cancellationToken)
        {
            RequirementReads++;
            RequirementLimits.Add(within);
            var answer = Requirements();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(answer);
        }
        public string FileToStart => fileToStart;
        public bool Prepared { get; private set; }
        public bool Undone { get; private set; }

        public Task<PreparedUpdate> PrepareAsync(
            string version, IProgress<double?>? progress, CancellationToken cancellationToken)
        {
            Prepared = true;
            onPrepare?.Invoke();
            return Task.FromResult(kind == AppCopyKind.Setup
                ? new PreparedUpdate(version, fileToStart, "/VERYSILENT /SP-", () => Undone = true)
                : new PreparedUpdate(version, fileToStart, string.Empty, () =>
                    {
                        DeleteDownload();
                        Undone = true;
                    },
                    deleteDownload: DeleteDownload));
        }

        /// <summary>Deletes the download and its folder where nothing else is in it, as the real download does.</summary>
        private void DeleteDownload()
        {
            InstallerClean.Helpers.StorageHelpers.TryDeleteTempFile(fileToStart);
            try { Directory.Delete(Path.GetDirectoryName(fileToStart)!, recursive: false); }
            catch (IOException) { }
        }
    }

    private sealed class RecordingHost(string exe) : ICommandLineUpdateHost
    {
        public List<string> Output { get; } = [];
        public Queue<string?> Answers { get; } = new();
        public Func<string?>? OnRead { get; set; }

        /// <summary>The token the last read was handed.</summary>
        public CancellationToken ReadToken { get; private set; }
        public int Reads { get; private set; }
        public bool LockAvailable { get; set; } = true;
        public bool HoldsLock { get; private set; }
        public List<int> LockThreads { get; } = [];

        /// <summary>Answers each look for another run of this file, given its count from 1.</summary>
        public Func<int, bool> AnotherRunAt { get; set; } = _ => false;
        public int AnotherRunLooks { get; private set; }

        /// <summary>Held from the start, as the console host holds its own file before the update.</summary>
        public bool HoldsOwnFile { get; private set; } = true;

        /// <summary>Whether the mutex was held at each call to <see cref="PrepareToMoveAside"/>.</summary>
        public List<bool> LockHeldAtPrepare { get; } = [];
        public Func<int?> OnSetup { get; set; } = () => 0;
        public Action? OnNewerVersion { get; set; }
        public int? NewerVersionExitCode { get; set; }
        public (string File, string[] Arguments, bool HeldLock, bool ExeWasAside)? SetupRun { get; private set; }
        public (string File, string[] Arguments, string From, string To, string ExeHeld, bool OwnFileHeld)? NewerVersionRun { get; private set; }
        public bool NewerVersionHeldLock { get; private set; }

        /// <summary>Runs once the newer version has started, before what is said then.</summary>
        public Action? OnStarted { get; set; }

        /// <summary>Runs where the newer version is not started, just before the start answers so.</summary>
        public Action? OnNotStarted { get; set; }

        /// <summary>How many lines had been written when the newer version started.</summary>
        public int? LinesBeforeStart { get; private set; }

        /// <summary>What the exe's path held at each call to <see cref="PrepareToMoveAside"/>.</summary>
        public List<string> PreparedWhileExeHeld { get; } = [];

        /// <summary>Runs before each line is recorded, so a test can make a line's write throw.</summary>
        public Action<string>? OnWriteLine { get; set; }

        public void WriteLine(string line)
        {
            OnWriteLine?.Invoke(line);
            Output.Add(line);
        }

        // The question and the answer share a line on screen; recorded as an entry of its own.
        public void Write(string text) => Output.Add(text);

        public string? ReadLine(CancellationToken token)
        {
            Reads++;
            ReadToken = token;
            if (OnRead is not null) return OnRead();
            return Answers.Count > 0 ? Answers.Dequeue() : null;
        }

        public bool TryTakeInstanceLock()
        {
            LockThreads.Add(Environment.CurrentManagedThreadId);
            HoldsLock = LockAvailable;
            return HoldsLock;
        }

        public void ReleaseInstanceLock()
        {
            LockThreads.Add(Environment.CurrentManagedThreadId);
            HoldsLock = false;
        }

        public bool AnotherRunMayBeAlive(string exePath) => AnotherRunAt(++AnotherRunLooks);

        public void HoldOwnFile() => HoldsOwnFile = true;

        public Action? OnReleaseOwnFile { get; set; }

        /// <summary>Whether the run let go of its own file, which it does only to rename it.</summary>
        public bool OwnFileReleased { get; private set; }

        public void ReleaseOwnFile()
        {
            HoldsOwnFile = false;
            OwnFileReleased = true;
            OnReleaseOwnFile?.Invoke();
        }

        /// <summary>Runs before each read of a file's version, and may throw in its place.</summary>
        public Action? OnFileVersion { get; set; }

        public bool Closing { get; set; }

        public bool ClosedFirst { get; set; }

        public Version? FileVersion(string path)
        {
            OnFileVersion?.Invoke();
            return !File.Exists(path) ? null : File.ReadAllText(path) switch
            {
                "old" => new Version(3, 4, 0, 0),
                "new" => new Version(3, 5, 0, 0),
                _ => null,
            };
        }

        public void PrepareToMoveAside()
        {
            PreparedWhileExeHeld.Add(File.Exists(exe) ? File.ReadAllText(exe) : "(nothing)");
            LockHeldAtPrepare.Add(HoldsLock);
        }

        // Each start reads the token first, as the console host's does before every try.
        public int? RunSetup(string file, IReadOnlyList<string> arguments, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            SetupRun = (file, arguments.ToArray(), HoldsLock,
                !File.Exists(exe) && File.Exists(exe + SelfReplacement.OldSuffix));
            return OnSetup();
        }

        public int? RunNewerVersion(
            string file, IReadOnlyList<string> arguments, string fromVersion, string toVersion, Action onStarted,
            CancellationToken token)
        {
            NewerVersionHeldLock = HoldsLock;
            OnNewerVersion?.Invoke();
            token.ThrowIfCancellationRequested();
            if (NewerVersionExitCode is null)
            {
                OnNotStarted?.Invoke();
                return null;
            }
            NewerVersionRun = (file, arguments.ToArray(), fromVersion, toVersion, File.ReadAllText(file), HoldsOwnFile);
            LinesBeforeStart = Output.Count;
            OnStarted?.Invoke();
            // As the console host does, a throw from what is said once it has started goes
            // no further.
            try
            {
                onStarted();
            }
            catch (Exception)
            {
            }
            return NewerVersionExitCode;
        }
    }
}
