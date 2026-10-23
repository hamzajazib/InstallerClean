using InstallerClean.Resources;

namespace InstallerClean.Services;

/// <summary>
/// What a command-line run's update ended in, which the host turns into an
/// Application-log notice and an exit code.
/// </summary>
internal abstract record CommandLineUpdateResult
{
    private CommandLineUpdateResult() { }

    /// <summary>
    /// The run goes on with the version it is. <paramref name="FailedAt"/> is null where
    /// nothing was tried: no newer version, the check failing, a no at the question, or a
    /// build from source. Otherwise it names the part that failed, or
    /// <see cref="UpdateFailureStage.InUse"/> where InstallerClean was in use, and the two
    /// versions are the running one and the one it could not install.
    /// </summary>
    public sealed record CarriedOn(UpdateFailureStage? FailedAt, string? FromVersion, string? ToVersion)
        : CommandLineUpdateResult;

    /// <summary>
    /// The newer version ran in this console with <paramref name="ArgumentsRun"/> and has
    /// finished; <paramref name="ExitCode"/> is its exit code, which the host ends this run
    /// with, or reads as Ctrl+C having ended it before it could answer. Where the newer
    /// version could be neither ended nor waited for, it is this run's
    /// <see cref="Helpers.CliExitCode.Error"/>, and the newer version may still be running
    /// in the console.
    /// </summary>
    public sealed record HandedOver(int ExitCode, string FromVersion, string ToVersion, string ArgumentsRun)
        : CommandLineUpdateResult;

    /// <summary>
    /// Ctrl+C before the newer version is in place: at the question, during the download,
    /// or after this exe was moved aside and before anything started, in which case it is
    /// put back. Nothing is left changed on disk.
    /// </summary>
    public sealed record Cancelled : CommandLineUpdateResult;

    /// <summary>
    /// <paramref name="ToVersion"/> is installed at the running exe's path and Ctrl+C came
    /// before it started, so nothing ran. The newer version stays in place.
    /// </summary>
    public sealed record UpdatedThenCancelled(string FromVersion, string ToVersion) : CommandLineUpdateResult;

    /// <summary>
    /// <paramref name="ToVersion"/> is installed at the running exe's path, its version
    /// read off the file, and could not be started, so nothing ran. The run ends here:
    /// this version's file is no longer at its path, and its process cannot load what a
    /// scan needs from a file that has been renamed.
    /// </summary>
    public sealed record NotStarted(string FromVersion, string ToVersion) : CommandLineUpdateResult;

    /// <summary>
    /// A part failed with the running exe renamed aside and it could not be put back, so
    /// the run ends with nothing run. <paramref name="FailureType"/> names what failed,
    /// and <paramref name="CrashLogPath"/> is where it was written, or null where it
    /// could not be.
    /// </summary>
    public sealed record Stopped(string FailureType, string? CrashLogPath) : CommandLineUpdateResult;

    /// <summary>
    /// The part named by <paramref name="Stage"/> failed, or the update was not tried
    /// where it is <see cref="UpdateFailureStage.InUse"/>, and Ctrl+C had come, so nothing
    /// runs. This exe is at its own path and nothing changed it.
    /// </summary>
    public sealed record FailedThenCancelled(UpdateFailureStage Stage, string FromVersion, string ToVersion)
        : CommandLineUpdateResult;

    /// <summary>The answer where nothing was tried.</summary>
    public static readonly CarriedOn NothingTried = new(null, null, null);
}

/// <summary>
/// What <see cref="CommandLineUpdate"/> needs from the console host: the console, the
/// single-instance mutex, the version a file carries, and starting a program in this
/// console. Each member is the host's because each touches the console or the process.
/// </summary>
internal interface ICommandLineUpdateHost
{
    /// <summary>Prints a line.</summary>
    void WriteLine(string line);

    /// <summary>Prints text and leaves the cursor after it, for the question.</summary>
    void Write(string text);

    /// <summary>
    /// Reads one line of the person's answer, or null at the end of input, which is also
    /// what Ctrl+C at the prompt can end the read with.
    /// </summary>
    string? ReadLine();

    /// <summary>
    /// Takes the single-instance mutex without waiting, on the calling thread. Answers
    /// whether it is now held.
    /// </summary>
    bool TryTakeInstanceLock();

    /// <summary>Releases the mutex <see cref="TryTakeInstanceLock"/> took, on the same thread.</summary>
    void ReleaseInstanceLock();

    /// <summary>
    /// Whether a process other than this one may be running from <paramref name="exePath"/>
    /// (<see cref="SelfReplacement.AnotherProcessMayRunFrom"/>).
    /// </summary>
    bool AnotherRunMayBeAlive(string exePath);

    /// <summary>
    /// Opens the handle this run holds on its own file against renaming
    /// (<see cref="SelfReplacement.HoldAgainstRenaming"/>). Does nothing where it is open.
    /// </summary>
    void HoldOwnFile();

    /// <summary>Closes that handle, immediately before this run renames its own file.</summary>
    void ReleaseOwnFile();

    /// <summary>
    /// The version the file at <paramref name="path"/> carries, or null where it carries
    /// none or cannot be read. It is read from the file itself
    /// (<see cref="Helpers.FileVersionResource"/>): the path is this exe's own once it has
    /// been renamed aside, and the version read Windows itself provides answers for that
    /// path with the running program's version.
    /// </summary>
    Version? FileVersion(string path);

    /// <summary>
    /// Loads what the rest of the run reaches, and checks the event source, while the
    /// running exe's file is still whole (<see cref="SelfReplacement.PrepareToMoveAside"/>).
    /// Called once, immediately before the running exe is renamed aside.
    /// </summary>
    void PrepareToMoveAside();

    /// <summary>
    /// Starts the setup at <paramref name="file"/> with <paramref name="arguments"/>,
    /// waits for it to end and answers with its exit code, or null where it could not be
    /// started. Throws <see cref="OperationCanceledException"/> where
    /// <paramref name="token"/> is cancelled before the setup has started; once it has,
    /// the setup runs to its end.
    /// </summary>
    int? RunSetup(string file, IReadOnlyList<string> arguments, CancellationToken token);

    /// <summary>
    /// Starts the newer version at <paramref name="file"/> with <paramref name="arguments"/>
    /// in this console, calls <paramref name="onStarted"/> and notes in the Application log
    /// that this run updated itself from <paramref name="fromVersion"/> to
    /// <paramref name="toVersion"/> once it is running, waits for it to end and answers with
    /// its exit code, or null where it could not be started. Where
    /// <paramref name="toVersion"/> takes <see cref="Helpers.HandOverContract.StartedEventSwitch"/>,
    /// running means it has set the event: one that ends first, or has not set it within
    /// <see cref="Helpers.HandOverContract.StartBound"/>, is ended and waited for, and the
    /// answer is null, with nothing said and nothing noted. One Windows will not end is
    /// waited for until it ends, and counts as running unless the event is then seen unset;
    /// where that wait fails, the answer is <see cref="Helpers.CliExitCode.Error"/> once
    /// <paramref name="onStarted"/> and the note have run. Throws
    /// <see cref="OperationCanceledException"/> where <paramref name="token"/> is cancelled
    /// before it has started, the newer version having been ended. Nothing is thrown once it
    /// has, a throw from <paramref name="onStarted"/> included: the run reads a throw as the
    /// newer version not having started, and on a swap puts this exe back at the path the
    /// newer version runs from. While it runs, and from the moment it is being started where
    /// the host passes it Ctrl+C, Ctrl+C is the newer version's to answer.
    /// </summary>
    int? RunNewerVersion(
        string file, IReadOnlyList<string> arguments, string fromVersion, string toVersion, Action onStarted,
        CancellationToken token);
}

/// <summary>
/// The command line's update, run before the scan of a <c>/s</c>, <c>/d</c> or <c>/m</c>:
/// it checks for a newer version and, where one is out, installs it and runs it in the
/// same console. Where a person can see the console and answer it, it asks first, and
/// after a yes the newer version scans and lists as <c>/s</c> does and moves or deletes
/// nothing. Where nobody can answer, it updates without asking and the newer version
/// runs the command as given. Where InstallerClean is in use, it says so and goes on
/// without updating. A part that fails leaves the run going on with the version it is,
/// and says so, wherever that version's file is back at its own path and Ctrl+C has not
/// come; otherwise the run ends there.
/// </summary>
/// <remarks>
/// Synchronous on purpose. The host calls it on the thread that owns the single-instance
/// mutex, and the mutex has to be released on the thread that took it, so the check and
/// the download are waited on here rather than awaited. A console host has no
/// SynchronizationContext, so the wait cannot deadlock.
/// </remarks>
internal sealed class CommandLineUpdate
{
    /// <summary>
    /// How long the check may take where a person is waiting on the console. Where nobody
    /// is, the check's own <see cref="UpdateCheckService.RequestTimeout"/> bounds it.
    /// </summary>
    internal static readonly TimeSpan WatchedCheckBound = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long the run waits for Ctrl+C's cancellation once something Ctrl+C can end has
    /// ended without it: the question's read, which Ctrl+C can end as the end of input, or
    /// the newer version, which Windows ends with <see cref="ControlCExit"/> where it has
    /// no handler for Ctrl+C yet. The host's handler cancels the token on the console's
    /// control-handler thread, which can run after either has ended, so the run waits up
    /// to this long for it and reads that Ctrl+C as its own.
    /// </summary>
    internal static readonly TimeSpan ControlCBound = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The exit code of a console process Windows ended for Ctrl+C because it had no
    /// handler for it yet, STATUS_CONTROL_C_EXIT.
    /// </summary>
    internal const int ControlCExit = unchecked((int)0xC000013A);

    private readonly Func<CancellationToken, Task<UpdateCheckResult>> _check;
    private readonly IAppUpdater _updater;
    private readonly ICommandLineUpdateHost _host;
    private readonly TimeSpan _watchedCheckBound;
    private readonly TimeSpan _controlCBound;
    private bool _saidInstalled;

    /// <param name="check">Asks GitHub for the newest version.</param>
    /// <param name="updater">
    /// Gets the newer version ready, built with <see cref="SetupArguments"/> for the
    /// same answer to whether a person can answer.
    /// </param>
    /// <param name="host">The console host.</param>
    public CommandLineUpdate(
        Func<CancellationToken, Task<UpdateCheckResult>> check, IAppUpdater updater, ICommandLineUpdateHost host)
        : this(check, updater, host, WatchedCheckBound, ControlCBound) { }

    /// <summary>
    /// Test seam: the bound on a watched check and the wait for Ctrl+C's cancellation, so
    /// a test need not wait either out.
    /// </summary>
    internal CommandLineUpdate(
        Func<CancellationToken, Task<UpdateCheckResult>> check, IAppUpdater updater, ICommandLineUpdateHost host,
        TimeSpan watchedCheckBound, TimeSpan controlCBound)
    {
        _check = check;
        _updater = updater;
        _host = host;
        _watchedCheckBound = watchedCheckBound;
        _controlCBound = controlCBound;
    }

    /// <summary>
    /// What the command line starts the newer setup with. /SP- leaves out the page asking
    /// whether to install, /NORESTART keeps Setup from restarting Windows, and
    /// /SUPPRESSMSGBOXES answers any box Setup would show with its default, so it never
    /// waits on one; a box it would have shown fails the setup. /NOCLOSEAPPLICATIONS keeps
    /// Setup from closing programs holding files it replaces, this run among them, which
    /// waits on it. /SILENT shows Setup's progress where a person is watching, and
    /// /VERYSILENT shows nothing where nobody is. /UPDATEFROMAPP is left out: it is what
    /// opens the window afterwards.
    /// </summary>
    internal static string SetupArguments(bool canAsk) =>
        (canAsk ? "/SILENT" : "/VERYSILENT") + " /SP- /NORESTART /SUPPRESSMSGBOXES /NOCLOSEAPPLICATIONS";

    /// <summary>
    /// Runs the update for a run of <paramref name="command"/> given
    /// <paramref name="arguments"/>.
    /// </summary>
    /// <param name="canAsk">Whether a person can see the console and answer it.</param>
    /// <param name="command">The command typed, lower-cased: /s, /d or /m.</param>
    /// <param name="arguments">The arguments as given, which the newer version gets where nobody can answer.</param>
    /// <param name="exePath">The running command line's own path.</param>
    /// <param name="token">Cancelled by Ctrl+C.</param>
    public CommandLineUpdateResult Run(
        bool canAsk, string command, IReadOnlyList<string> arguments, string exePath, CancellationToken token)
    {
        if (Check(canAsk, token) is not { } available)
            return token.IsCancellationRequested ? new CommandLineUpdateResult.Cancelled() : CommandLineUpdateResult.NothingTried;

        var current = available.CurrentVersion;
        var latest = available.LatestVersion;
        _host.WriteLine(string.Format(Strings.UpdateNotice_Available, latest));

        if (!_updater.CanUpdateItself)
        {
            _host.WriteLine(Strings.UpdateNotice_Recommended);
            _host.WriteLine(string.Format(Strings.Cli_UpdateDownloadLink, UpdateCheckService.ReleasesPageUrl));
            return CommandLineUpdateResult.NothingTried;
        }

        if (canAsk)
        {
            // Asked here as well as before the download, so a person is not asked a question
            // whose answer cannot be acted on.
            if (InUse(exePath))
                return CarryOn(InUseFailure(), command, current, latest, token);
            _host.WriteLine(Strings.UpdateNotice_Recommended);
            switch (Ask(token))
            {
                case Answer.Cancelled: return new CommandLineUpdateResult.Cancelled();
                case Answer.No: return CommandLineUpdateResult.NothingTried;
            }
        }

        try
        {
            return Update(canAsk, command, arguments, exePath, current, latest, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _host.HoldOwnFile();
            return new CommandLineUpdateResult.Cancelled();
        }
        catch (Exception ex)
        {
            return CarryOn(ex, command, current, latest, token);
        }
    }

    /// <summary>
    /// Whether InstallerClean is in use, so the update is not tried: the single-instance
    /// mutex is held elsewhere, or another process may be running from this exe's file.
    /// The mutex is taken and released again here, on the calling thread.
    /// </summary>
    private bool InUse(string exePath)
    {
        var holdsLock = _host.TryTakeInstanceLock();
        try
        {
            return InUse(holdsLock, exePath);
        }
        finally
        {
            if (holdsLock) _host.ReleaseInstanceLock();
        }
    }

    /// <summary>
    /// Whether InstallerClean is in use, given whether this run took the mutex: it is held
    /// elsewhere, or another process may be running from this exe's file.
    /// </summary>
    private bool InUse(bool holdsLock, string exePath) => !holdsLock || _host.AnotherRunMayBeAlive(exePath);

    private static UpdateFailedException InUseFailure(Exception? inner = null) =>
        new(UpdateFailureStage.InUse, "InstallerClean is in use, so the update was not tried.", inner);

    /// <summary>
    /// Says why the update did not happen and that the run goes on with this version, and
    /// answers <see cref="CommandLineUpdateResult.CarriedOn"/>. A failure is written to
    /// crash.log. InstallerClean being in use is not a failure, so nothing is, except a
    /// rename it refused, whose cause is written. This exe's file is back at its own path
    /// here, so the run holds it against renaming again. Where Ctrl+C has come by the time
    /// the reason is said, the run does not go on: nothing more is said, and the answer is
    /// <see cref="CommandLineUpdateResult.FailedThenCancelled"/>.
    /// </summary>
    private CommandLineUpdateResult CarryOn(
        Exception failure, string command, string current, string latest, CancellationToken token)
    {
        var stage = failure is UpdateFailedException failed ? failed.Stage : UpdateFailureStage.Install;
        _host.HoldOwnFile();
        if (stage == UpdateFailureStage.InUse)
        {
            if (failure.InnerException is not null) Helpers.CrashLog.TryWrite(failure);
            _host.WriteLine(Strings.Cli_UpdateInUse);
        }
        else
        {
            Helpers.CrashLog.TryWrite(failure);
            _host.WriteLine(stage == UpdateFailureStage.Download
                ? Strings.UpdateNotice_DownloadFailed
                : Strings.UpdateNotice_InstallFailed);
        }
        if (token.IsCancellationRequested)
            return new CommandLineUpdateResult.FailedThenCancelled(stage, current, latest);
        _host.WriteLine(string.Format(Strings.Cli_UpdateRunningInstead, command, current));
        return new CommandLineUpdateResult.CarriedOn(stage, current, latest);
    }

    /// <summary>
    /// The check, bounded by <see cref="WatchedCheckBound"/> where a person is waiting.
    /// Answers the newer version, or null for anything else: up to date, a failed check,
    /// the bound running out, or Ctrl+C, which the caller reads off the token.
    /// </summary>
    private UpdateAvailable? Check(bool canAsk, CancellationToken token)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (canAsk) bound.CancelAfter(_watchedCheckBound);
        try
        {
            return _check(bound.Token).GetAwaiter().GetResult() as UpdateAvailable;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private enum Answer { Yes, No, Cancelled }

    /// <summary>
    /// Asks the question until it gets an answer. Enter, or an answer starting with this
    /// language's letter for yes or with Y, is yes; one starting with its letter for no or
    /// with N is no; anything else asks again. Ctrl+C is read off the token once the read
    /// returns. The end of input is no, unless the token is cancelled while the question
    /// waits on it, up to <see cref="ControlCBound"/> in a run, since Ctrl+C at the prompt
    /// can itself be read as the end of input.
    /// </summary>
    private Answer Ask(CancellationToken token)
    {
        var yes = Strings.Cli_UpdateAnswerYes.Trim();
        var no = Strings.Cli_UpdateAnswerNo.Trim();
        var prompt = string.Format(Strings.Cli_UpdatePrompt,
            yes.ToUpper(System.Globalization.CultureInfo.CurrentUICulture),
            no.ToLower(System.Globalization.CultureInfo.CurrentUICulture));
        while (true)
        {
            _host.Write(prompt + " ");
            var line = _host.ReadLine();
            if (line is null) token.WaitHandle.WaitOne(_controlCBound);
            if (token.IsCancellationRequested) return Answer.Cancelled;
            if (line is null) return Answer.No;
            var answer = line.Trim();
            if (answer.Length == 0 || StartsWith(answer, yes) || StartsWith(answer, "Y")) return Answer.Yes;
            if (StartsWith(answer, no) || StartsWith(answer, "N")) return Answer.No;
        }

        static bool StartsWith(string answer, string letter) =>
            letter.Length > 0 && answer.StartsWith(letter, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Downloads the newer version, puts it in place and runs it. Throws where a part
    /// fails with this exe's file at its own path, having taken away what it changed; the
    /// caller says so and carries on, unless Ctrl+C has come. Answers
    /// <see cref="CommandLineUpdateResult.NotStarted"/> or
    /// <see cref="CommandLineUpdateResult.Stopped"/> where a part fails with it no longer
    /// there. Ctrl+C before the newer version is in place, or on a swap before it has
    /// started, answers <see cref="CommandLineUpdateResult.Cancelled"/>, with this exe back
    /// at its path and nothing changed; Ctrl+C once the setup has installed it answers
    /// <see cref="CommandLineUpdateResult.UpdatedThenCancelled"/>.
    /// </summary>
    private CommandLineUpdateResult Update(
        bool canAsk, string command, IReadOnlyList<string> arguments, string exePath,
        string current, string latest, CancellationToken token)
    {
        var isSetup = _updater.Kind == AppCopyKind.Setup;
        PreparedUpdate prepared;
        Func<bool> putBack;

        // The mutex is held from before the download until this exe has been renamed, on
        // every kind of copy, so nothing else that takes it runs meanwhile: the window,
        // which clears out earlier updates' download folders as it starts, a Move or
        // Delete, and another run's update. A /s takes no mutex, so a process running from
        // this exe's file is looked for as well, before the download and again just before
        // the rename, and one that has reached its own code holds the file against
        // renaming besides. The setup names the same mutex and stops for anything holding
        // it, so the mutex is released before the setup runs.
        var holdsLock = _host.TryTakeInstanceLock();
        try
        {
            if (InUse(holdsLock, exePath)) throw InUseFailure();
            _host.WriteLine(string.Format(Strings.UpdateNotice_Downloading, latest));
            prepared = _updater.PrepareAsync(latest, null, token).GetAwaiter().GetResult();

            _host.PrepareToMoveAside();
            if (_host.AnotherRunMayBeAlive(exePath))
            {
                prepared.Undo();
                throw InUseFailure();
            }
            if (token.IsCancellationRequested)
            {
                prepared.Undo();
                return new CommandLineUpdateResult.Cancelled();
            }
            _host.ReleaseOwnFile();
            try
            {
                putBack = isSetup ? SelfReplacement.MoveAside(exePath, token) : SelfReplacement.SwapIn(exePath, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Ctrl+C while the rename waited for another process to let go of this exe,
                // which is still at its path. Run answers it as Cancelled.
                prepared.Undo();
                throw;
            }
            catch (Exception ex)
            {
                prepared.Undo();
                if (!File.Exists(exePath)) return Stop(ex);
                // A rename refused while another run of this exe is alive is InstallerClean
                // being in use; any other refusal is a failure.
                if (_host.AnotherRunMayBeAlive(exePath)) throw InUseFailure(ex);
                throw;
            }
        }
        finally
        {
            if (holdsLock) _host.ReleaseInstanceLock();
        }

        // This exe is renamed aside from here until something puts it back. A throw that
        // nothing below answers ends the update as a newer version that did not start,
        // which is what it is: the host throws nothing once the newer version has started.
        var backAtPath = false;
        bool PutBack() => backAtPath = putBack();
        try
        {
            return AfterRename(canAsk, command, arguments, exePath, current, latest, isSetup, prepared, PutBack, token);
        }
        catch (Exception ex) when (!backAtPath)
        {
            try
            {
                return EndNotStarted(ex, isSetup, prepared, exePath, current, latest, PutBack, token);
            }
            catch (Exception again) when (!backAtPath)
            {
                // Saying so failed as well, so nothing more is said, and the run ends with
                // nothing run, answered by what the file at the path carries.
                var crash = Helpers.CrashLog.TryWrite(again);
                if (!Carries(exePath, latest))
                    return new CommandLineUpdateResult.Stopped(again.GetType().Name, crash.Written ? crash.Path : null);
                return token.IsCancellationRequested
                    ? new CommandLineUpdateResult.UpdatedThenCancelled(current, latest)
                    : new CommandLineUpdateResult.NotStarted(current, latest);
            }
        }
    }

    /// <summary>
    /// The part of <see cref="Update"/> after this exe has been renamed aside: runs the
    /// setup where it is the setup's copy, then starts the newer version.
    /// <paramref name="putBack"/> undoes the rename.
    /// </summary>
    private CommandLineUpdateResult AfterRename(
        bool canAsk, string command, IReadOnlyList<string> arguments, string exePath,
        string current, string latest, bool isSetup, PreparedUpdate prepared, Func<bool> putBack,
        CancellationToken token)
    {
        if (isSetup && RunSetup(prepared, exePath, latest, putBack, token) is { } ended) return ended;

        if (token.IsCancellationRequested)
        {
            // Ctrl+C by now stops the run before the newer version starts. Where the setup ran,
            // the newer version is in place and stays. On a swap it has only been renamed in
            // and nothing has said it is installed, so this exe is put back in its place.
            if (!isSetup) return CancelAfterSwap(prepared, exePath, current, latest, putBack);
            SayInstalled(latest);
            return new CommandLineUpdateResult.UpdatedThenCancelled(current, latest);
        }

        // The setup has installed the newer version whether or not it starts, so its copy
        // says so now. On a swap it is said once the newer version has started, since until
        // then a failed start or Ctrl+C puts this exe back wherever it can.
        if (isSetup) SayInstalledAndWhatRuns();

        IReadOnlyList<string> run = canAsk ? ["/s"] : arguments;
        int? exitCode;
        try
        {
            exitCode = _host.RunNewerVersion(
                exePath, run, current, latest, isSetup ? () => { } : SayInstalledAndWhatRuns, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Ctrl+C before the newer version started. On a swap nothing has said it is
            // installed, and a copy not yet seen to start is not left in this exe's place,
            // so this exe is put back. The setup's copy has said so, and it stays.
            if (!isSetup) return CancelAfterSwap(prepared, exePath, current, latest, putBack);
            return new CommandLineUpdateResult.UpdatedThenCancelled(current, latest);
        }
        // A newer version ended for Ctrl+C before it had a handler for it exits with
        // ControlCExit, and the same Ctrl+C can reach the token here after that exit, so
        // the run waits for it before handing the exit code back.
        if (exitCode == ControlCExit) token.WaitHandle.WaitOne(_controlCBound);
        if (exitCode is not null)
            return new CommandLineUpdateResult.HandedOver(exitCode.Value, current, latest, string.Join(' ', run));

        return EndNotStarted(new UpdateFailedException(UpdateFailureStage.Install, "The newer version could not be started."),
            isSetup, prepared, exePath, current, latest, putBack, token);

        // Says the newer version is installed and, where a person answered yes to a Delete
        // or a Move, that it runs a scan, unless Ctrl+C has come.
        void SayInstalledAndWhatRuns()
        {
            SayInstalled(latest);
            if (token.IsCancellationRequested) return;
            if (canAsk && command == "/d") _host.WriteLine(Strings.Cli_UpdateListsOnlyAfterDelete);
            if (canAsk && command == "/m") _host.WriteLine(Strings.Cli_UpdateListsOnlyAfterMove);
        }
    }

    /// <summary>
    /// Ends an update whose newer version did not start, for <paramref name="cause"/>, with
    /// this exe renamed aside. On a swap this exe is put back and what was readied taken
    /// away, and <paramref name="cause"/> thrown, so the caller says so and carries on, or
    /// stops where Ctrl+C has come. On the setup's copy, or where this exe could not be put
    /// back, it stays aside, since the setup may have put the newer version and the window
    /// beside it in place, and the file at the path is checked before anything says the
    /// newer version is installed. Where it carries the newer version the answer is
    /// <see cref="CommandLineUpdateResult.NotStarted"/>, or
    /// <see cref="CommandLineUpdateResult.UpdatedThenCancelled"/> where Ctrl+C has come,
    /// as everywhere else once the newer version is in place; where it does not, the
    /// answer is <see cref="CommandLineUpdateResult.Stopped"/>.
    /// </summary>
    private CommandLineUpdateResult EndNotStarted(
        Exception cause, bool isSetup, PreparedUpdate prepared, string exePath, string current, string latest,
        Func<bool> putBack, CancellationToken token)
    {
        if (!isSetup && putBack())
        {
            prepared.Undo();
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(cause);
        }
        if (token.IsCancellationRequested)
        {
            if (!Carries(exePath, latest)) return Stop(NotPutBackAfterCancel());
            SayInstalled(latest);
            return new CommandLineUpdateResult.UpdatedThenCancelled(current, latest);
        }
        if (!Carries(exePath, latest)) return Stop(cause);
        Helpers.CrashLog.TryWrite(cause);
        _host.WriteLine(string.Format(Strings.Cli_UpdateNotStarted, latest));
        return new CommandLineUpdateResult.NotStarted(current, latest);
    }

    /// <summary>
    /// Ctrl+C after the newer version was renamed into this exe's place and before the
    /// line saying it is installed: this exe is put back and what was readied taken away,
    /// answering <see cref="CommandLineUpdateResult.Cancelled"/>. Where it cannot be put
    /// back, the run ends as <see cref="CommandLineUpdateResult.UpdatedThenCancelled"/>
    /// where the file at the path carries the newer version, and otherwise as
    /// <see cref="CommandLineUpdateResult.Stopped"/>. Internal so a test can reach the two
    /// answers where the put-back fails, which no file a test makes can refuse.
    /// </summary>
    internal CommandLineUpdateResult CancelAfterSwap(
        PreparedUpdate prepared, string exePath, string current, string latest, Func<bool> putBack)
    {
        if (putBack())
        {
            prepared.Undo();
            _host.HoldOwnFile();
            return new CommandLineUpdateResult.Cancelled();
        }
        if (!Carries(exePath, latest)) return Stop(NotPutBackAfterCancel());
        SayInstalled(latest);
        return new CommandLineUpdateResult.UpdatedThenCancelled(current, latest);
    }

    private static UpdateFailedException NotPutBackAfterCancel() =>
        new(UpdateFailureStage.Install, "The running copy could not be put back after Ctrl+C.");

    /// <summary>Says the newer version is installed, once in a run.</summary>
    private void SayInstalled(string latest)
    {
        if (_saidInstalled) return;
        _saidInstalled = true;
        _host.WriteLine(string.Format(Strings.Cli_UpdateInstalled, latest));
    }

    /// <summary>
    /// Runs the newer setup with this command line renamed aside, so the setup writes the
    /// newer version at its path rather than meeting a file in use;
    /// <paramref name="putBack"/> undoes that rename. Answers null where the newer version
    /// is in place. Any exit code but 0, or a file at the path that does not carry the
    /// newer version, fails: this exe is put back and the failure thrown, or
    /// <see cref="CommandLineUpdateResult.Stopped"/> answered where it cannot be put back.
    /// Ctrl+C before the setup has started is thrown the same way, with this exe put back.
    /// The downloaded setup and its folder are removed either way, the setup having
    /// finished with them.
    /// </summary>
    private CommandLineUpdateResult? RunSetup(
        PreparedUpdate prepared, string exePath, string latest, Func<bool> putBack, CancellationToken token)
    {
        int? exitCode;
        try
        {
            token.ThrowIfCancellationRequested();
            exitCode = _host.RunSetup(
                prepared.FileToStart,
                prepared.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                token);
        }
        catch (Exception ex)
        {
            prepared.Undo();
            if (!putBack()) return Stop(ex is OperationCanceledException ? NotPutBackAfterCancel() : ex);
            throw;
        }
        prepared.Undo();

        UpdateFailedException? failed = null;
        if (exitCode != 0)
            failed = new UpdateFailedException(UpdateFailureStage.Install,
                exitCode is null ? "The setup could not be started." : $"The setup ended with exit code {exitCode}.");
        else if (!Carries(exePath, latest))
            failed = new UpdateFailedException(UpdateFailureStage.Install,
                "The setup finished without putting the newer command line in place.");
        if (failed is null) return null;
        if (!putBack()) return Stop(failed);
        throw failed;
    }

    /// <summary>
    /// Ends the run where this exe is renamed aside and cannot be put back: says the
    /// update could not be installed, writes what failed to crash.log, and answers
    /// <see cref="CommandLineUpdateResult.Stopped"/>.
    /// </summary>
    private CommandLineUpdateResult.Stopped Stop(Exception failure)
    {
        var crash = Helpers.CrashLog.TryWrite(failure);
        _host.WriteLine(Strings.UpdateNotice_InstallFailed);
        return new CommandLineUpdateResult.Stopped(failure.GetType().Name, crash.Written ? crash.Path : null);
    }

    /// <summary>
    /// Whether the file at <paramref name="path"/> carries <paramref name="version"/>
    /// (<see cref="Helpers.FileVersionResource.IsRelease"/>). The download itself is checked
    /// before it is put in place (<see cref="AppUpdater.PrepareAsync"/>). This reads the file
    /// at this exe's path after the setup has run, so the setup's copy starts nothing unless
    /// that file is the newer version, and wherever a failed part or Ctrl+C leaves the run
    /// saying whether the newer version is in place.
    /// </summary>
    private bool Carries(string path, string version) =>
        _host.FileVersion(path) is { } carried && Helpers.FileVersionResource.IsRelease(carried, version);
}
