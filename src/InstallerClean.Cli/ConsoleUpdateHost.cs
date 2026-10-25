using System.Diagnostics;
using System.Threading;
using InstallerClean.Helpers;
using InstallerClean.Resources;
using InstallerClean.Services;
using Microsoft.Win32.SafeHandles;

namespace InstallerClean.Cli;

/// <summary>
/// The console, the single-instance mutex and the processes <see cref="CommandLineUpdate"/>
/// works with, for one run.
/// </summary>
internal sealed class ConsoleUpdateHost(string arg) : ICommandLineUpdateHost
{
    private InstanceLock? _instanceLock;
    private SafeFileHandle? _ownFile;
    private readonly object _ctrlCGate = new();
    private bool _newerVersionAnswersCtrlC;
    private bool _cancellingHeldBack;
    private volatile bool _setupRunning;

    /// <summary>
    /// The read of the console still waiting for a line, which the next read in this process
    /// waits on rather than start a second read beside it. One for the process, as there is
    /// one console.
    /// </summary>
    private static Task<string?>? s_pendingRead;
    private static readonly object s_readGate = new();

    /// <summary>
    /// Takes the line saying the run is cancelling, for a Ctrl+C that has just come: answers
    /// whether this run prints it now. The run's answer to Ctrl+C calls it once. Once the
    /// newer version is being started with the event that passes it Ctrl+C, or has
    /// started, the newer version answers Ctrl+C, so this run holds the line back, prints
    /// nothing and keeps waiting for its exit code; the newer version writes the run's
    /// summary, unless Ctrl+C ended it before it could answer, when this run writes it.
    /// This run prints the line it held back where that start fails, or where Ctrl+C ended
    /// the newer version before it could answer. Otherwise a Ctrl+C cancels this run as
    /// anywhere else, which stops the newer version being started.
    /// </summary>
    public bool TakeCancellingLine()
    {
        lock (_ctrlCGate)
        {
            if (!_newerVersionAnswersCtrlC) return true;
            _cancellingHeldBack = true;
            return false;
        }
    }

    /// <summary>
    /// Whether the newer setup is running. This run waits for the setup to end whatever
    /// Ctrl+C does, so the run's answer to Ctrl+C says it stops once the setup has finished.
    /// </summary>
    public bool SetupRunning => _setupRunning;

    // Neither throws, as IConsolePrompt promises: Main puts ConsoleGuard on the console's
    // output before any run starts, and it takes a refused write, records a run's first in
    // crash.log and lets the run go on. Writing anywhere but Console.Out loses that.
    public void WriteLine(string line) => Console.WriteLine(line);

    public void Write(string text) => Console.Write(text);

    /// <summary>
    /// Reads a line on a thread of its own and waits for it or for <paramref name="token"/>.
    /// A console being closed cancels the token and leaves the read waiting, so the run goes
    /// on to end as cancelled while the read is left to end with the process. A later read
    /// in this process waits on that same read.
    /// </summary>
    public string? ReadLine(CancellationToken token)
    {
        Task<string?> read;
        lock (s_readGate)
            read = s_pendingRead ??= Task.Factory.StartNew(ReadConsoleLine, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            read.Wait(token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        lock (s_readGate)
            if (ReferenceEquals(s_pendingRead, read)) s_pendingRead = null;
        return read.GetAwaiter().GetResult();
    }

    /// <summary>Test seam: forgets the read left waiting, so a test starts with none.</summary>
    internal static void ForgetPendingRead()
    {
        lock (s_readGate) s_pendingRead = null;
    }

    private static string? ReadConsoleLine()
    {
        try
        {
            return Console.ReadLine();
        }
        catch (IOException ex)
        {
            CrashLog.TryWrite(ex);
            return null;
        }
    }

    public bool TryTakeInstanceLock()
    {
        _instanceLock = new InstanceLock();
        if (_instanceLock.TryTake()) return true;
        _instanceLock.Dispose();
        _instanceLock = null;
        return false;
    }

    public void ReleaseInstanceLock()
    {
        _instanceLock?.Release();
        _instanceLock?.Dispose();
        _instanceLock = null;
    }

    public bool AnotherRunMayBeAlive(string exePath) => SelfReplacement.AnotherProcessMayRunFrom(exePath);

    /// <summary>
    /// Holds this exe's own file, at the path it was started from, against renaming. The
    /// run calls it before anything else touches the update, and it stays held until the
    /// process ends or the update renames this exe.
    /// </summary>
    public void HoldOwnFile()
    {
        if (_ownFile is null && Environment.ProcessPath is { Length: > 0 } path)
            _ownFile = SelfReplacement.HoldAgainstRenaming(path);
    }

    public void ReleaseOwnFile()
    {
        _ownFile?.Dispose();
        _ownFile = null;
    }

    public Version? FileVersion(string path) =>
        FileVersionResource.ReadIfThere(path) is { } version && (version.Major, version.Minor, version.Build) != (0, 0, 0)
            ? version
            : null;

    /// <summary>
    /// Loads, before this exe is renamed aside, what the run reaches after it: starting
    /// the setup and the newer version, the Application-log write, which also makes its
    /// source check here, and the English machine line, whose first lookup in en-GB
    /// probes for resources the console's own language never asks for.
    /// </summary>
    /// <remarks>
    /// Two sets of strings are open once this returns: the display language's, through
    /// the lookup <see cref="SelfReplacement.PrepareToMoveAside"/> makes, and the neutral
    /// one under en-GB, through the machine line. A string read after the rename comes
    /// from one of the two: a line for the console is in every satellite, and a machine
    /// line is built inside <see cref="MachineContract.English"/>. A lookup in any other
    /// culture, such as one a language setting chose on one thread only, would open a set
    /// from a file that has been renamed.
    /// </remarks>
    public void PrepareToMoveAside()
    {
        SelfReplacement.PrepareToMoveAside(typeof(EventLog).Assembly, typeof(ConsoleUpdateHost).Assembly);
        EventLogWriter.PrepareForWrites();
        try
        {
            _ = MachineContract.English(() => Program.UpdatedEventLogLine(arg, "0.0.0", "0.0.0", []));
        }
        catch (Exception ex)
        {
            CrashLog.TryWrite(ex);
        }
    }

    public int? RunSetup(string file, IReadOnlyList<string> arguments, CancellationToken token)
    {
        using var process = Start(file, arguments, Path.GetDirectoryName(file), token);
        if (process is null) return null;
        _setupRunning = true;
        try
        {
            process.WaitForExit();
        }
        finally
        {
            _setupRunning = false;
        }
        return process.ExitCode;
    }

    public int? RunNewerVersion(
        string file, IReadOnlyList<string> arguments, string fromVersion, string toVersion, Action onStarted,
        CancellationToken token)
    {
        Console.Out.Flush();
        // The console gives Ctrl+C only to the processes connected to it, so one that comes
        // as the newer version starts can reach this run alone. Where the newer version
        // takes the hand-over switches, this event is set on Ctrl+C from here until the
        // newer version ends and passed to it by name, and the newer version takes it as
        // its own Ctrl+C, one set before it opened the event included. Where there is no
        // event, the newer version answers Ctrl+C only once it has started.
        var takesSwitches = HandOverContract.TakesHandOverSwitches(toVersion);
        var cancelId = Guids.NewGuid();
        var startedId = Guids.NewGuid();
        using var cancelEvent = takesSwitches ? CreateCancelEvent(cancelId) : null;
        // The event the newer version sets once its own code is running. Where it cannot be
        // made, the newer version is not started: one that cannot be seen to start is not
        // left in this exe's place.
        using var startedEvent = takesSwitches ? HandOverContract.CreateStartedEvent(startedId) : null;
        if (takesSwitches && startedEvent is null)
        {
            NewerVersionNotStarted();
            return null;
        }
        using var setOnCtrlC = cancelEvent is null
            ? default
            : token.Register(static e => ((EventWaitHandle)e!).Set(), cancelEvent);
        if (cancelEvent is not null) NewerVersionAnswersCtrlC(true);
        // The newer version is also told /noupdate, where it takes the switches, so it goes
        // straight to the command.
        var passed = HandOverContract.CommandLineArguments(
            arguments, toVersion,
            cancelEvent is not null ? cancelId : null,
            startedEvent is not null ? startedId : null);
        Process? started;
        try
        {
            started = Start(file, passed, workingDirectory: null, token);
        }
        catch
        {
            NewerVersionNotStarted();
            throw;
        }
        if (started is null)
        {
            NewerVersionNotStarted();
            return null;
        }
        using var process = started;
        var seen = startedEvent is null ? StartSeen.Started : NewerVersionSaidItStarted(process, startedEvent, token);
        if (seen == StartSeen.NotStarted) return null;
        // The newer version is running from the file at this exe's path from here, or
        // has run from it, so nothing leaves this method as an exception: the run reads a
        // throw as the newer version not having started and puts this exe back, which
        // would rename that file under it. A throw is written to crash.log. One from the
        // wait itself is answered with the error code, so this run ends with nothing more
        // run, and the newer version writes its own summary. This run then leaves the
        // console's code page to the newer version, which may still be writing to it.
        if (cancelEvent is null) NewerVersionAnswersCtrlC(true);
        // Said before the Application-log write, which can take a moment the first time.
        try
        {
            onStarted();
        }
        catch (Exception ex)
        {
            CrashLog.TryWrite(ex);
        }
        try
        {
            MachineContract.WriteEventLog(CliEventClass.UpdatedNotice,
                () => Program.UpdatedEventLogLine(arg, fromVersion, toVersion, arguments));
        }
        catch (Exception ex)
        {
            CrashLog.TryWrite(ex);
        }
        // A newer version this run could neither end nor wait for answers the error code, and
        // may still be running in this console.
        if (seen == StartSeen.NotWaitedFor)
        {
            Program.LeaveConsoleToNewerVersion();
            return CliExitCode.Error;
        }
        try
        {
            process.WaitForExit();
        }
        catch (Exception ex)
        {
            CrashLog.TryWrite(ex);
            Program.LeaveConsoleToNewerVersion();
            return CliExitCode.Error;
        }
        try
        {
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            CrashLog.TryWrite(ex);
            return CliExitCode.Error;
        }
    }

    /// <summary>
    /// Waits for the newer version <paramref name="process"/> to set
    /// <paramref name="startedEvent"/>, for up to <see cref="HandOverContract.StartBound"/>,
    /// and answers <see cref="StartSeen.Started"/> where it does. Where it ends first, the
    /// bound runs out, Windows refuses the wait or Ctrl+C comes first, the newer version has
    /// run none of the command: it is ended and waited for, what happened is written to
    /// crash.log, and the answer is <see cref="StartSeen.NotStarted"/>, with the line saying
    /// the run is cancelling printed where one was held back. Ctrl+C, or a newer version
    /// that Ctrl+C ended before it had a handler for it, throws
    /// <see cref="OperationCanceledException"/> instead, so the run answers it as Ctrl+C
    /// before the newer version started.
    /// </summary>
    /// <remarks>
    /// A newer version Windows would not end is still running its own code, so it is waited
    /// for until it ends, Ctrl+C or not, and the file it runs from is never renamed under it.
    /// It answers <see cref="StartSeen.NotStarted"/> only where a look at the event shows it
    /// unset once it has ended. Where it set the event, or the look fails, it may have run
    /// the command, so it answers <see cref="StartSeen.Started"/> and the run hands back its
    /// exit code; where the wait for it to end fails, <see cref="StartSeen.NotWaitedFor"/>.
    /// Either way the run never puts this exe back and runs the command a second time.
    /// </remarks>
    private StartSeen NewerVersionSaidItStarted(Process process, EventWaitHandle startedEvent, CancellationToken token)
    {
        var answer = SelfReplacement.AwaitStart(
            process, startedEvent, HandOverContract.StartBound, token.WaitHandle, out var waitError);
        if (answer == NewerVersionStart.Started) return StartSeen.Started;
        var failure = SelfReplacement.NotStartedFailure(answer, waitError, process, HandOverContract.StartBound);
        if (!SelfReplacement.EndNewerVersion(process))
        {
            try
            {
                process.WaitForExit();
            }
            catch (Exception ex)
            {
                CrashLog.TryWrite(ex);
                return StartSeen.NotWaitedFor;
            }
            switch (SelfReplacement.LookAt(startedEvent, out var lookError))
            {
                case HandleLook.Signalled:
                    return StartSeen.Started;
                case HandleLook.Failed:
                    CrashLog.TryWrite(SelfReplacement.LookFailure(lookError));
                    return StartSeen.Started;
            }
        }
        CrashLog.TryWrite(failure);
        if (answer == NewerVersionStart.Ended && EndedForCtrlC(process))
            token.WaitHandle.WaitOne(CommandLineUpdate.ControlCBound);
        NewerVersionNotStarted();
        token.ThrowIfCancellationRequested();
        return StartSeen.NotStarted;
    }

    /// <summary>What <see cref="NewerVersionSaidItStarted"/> found.</summary>
    private enum StartSeen
    {
        /// <summary>The newer version set the event, or may have and has ended: it runs or ran the command.</summary>
        Started,

        /// <summary>The newer version ran none of the command and has been ended.</summary>
        NotStarted,

        /// <summary>Windows would neither end the newer version nor let this run wait for it to end.</summary>
        NotWaitedFor,
    }

    private static bool EndedForCtrlC(Process process)
    {
        try
        {
            return process.ExitCode == CommandLineUpdate.ControlCExit;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Starts <paramref name="file"/> sharing this console, its standard handles and, where
    /// <paramref name="workingDirectory"/> is null, its working folder, so a relative path
    /// given to the newer version means what it meant to this run. A start that fails is
    /// tried again through <see cref="ProcessStartRetry"/>, and Ctrl+C ends the wait between
    /// tries. Answers null where every try failed, each written
    /// to crash.log. Throws <see cref="OperationCanceledException"/> where
    /// <paramref name="token"/> is cancelled and no try has started a process.
    /// </summary>
    private static Process? Start(
        string file, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken token)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false };
        if (workingDirectory is not null) info.WorkingDirectory = workingDirectory;
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        token.ThrowIfCancellationRequested();
        try
        {
            return ProcessStartRetry.Start(
                info,
                IsStartFailure,
                interval =>
                {
                    token.WaitHandle.WaitOne(interval);
                    token.ThrowIfCancellationRequested();
                });
        }
        catch (Exception ex) when (IsStartFailure(ex))
        {
            CrashLog.TryWrite(ex);
            token.ThrowIfCancellationRequested();
            return null;
        }
    }

    /// <summary>
    /// A start Windows refused. The wait between tries reads the token's wait handle, which
    /// throws <see cref="ObjectDisposedException"/> where the token's source has been
    /// disposed; that derives from InvalidOperationException but is not a refused start, so
    /// it reaches the caller.
    /// </summary>
    private static bool IsStartFailure(Exception ex) =>
        ex is System.ComponentModel.Win32Exception or IOException
            or (InvalidOperationException and not ObjectDisposedException);

    internal void NewerVersionAnswersCtrlC(bool answers)
    {
        lock (_ctrlCGate) _newerVersionAnswersCtrlC = answers;
    }

    /// <summary>
    /// The newer version did not start, so this run answers Ctrl+C again, and prints the
    /// line saying it is cancelling where it held one back meanwhile.
    /// </summary>
    internal void NewerVersionNotStarted()
    {
        NewerVersionAnswersCtrlC(false);
        SayHeldBackCancelling();
    }

    /// <summary>
    /// Prints the line saying the run is cancelling, where it was held back: the run calls
    /// it where Ctrl+C ended the newer version before it could answer it.
    /// </summary>
    internal void SayHeldBackCancelling()
    {
        bool heldBack;
        lock (_ctrlCGate)
        {
            heldBack = _cancellingHeldBack;
            _cancellingHeldBack = false;
        }
        if (heldBack) Program.SayCancelling(Strings.Cli_Cancelling);
    }

    /// <summary>
    /// Creates the event <paramref name="id"/> names, or answers null where it cannot or
    /// where an event of that name exists already, which this run did not make. Each is
    /// written to crash.log.
    /// </summary>
    private static EventWaitHandle? CreateCancelEvent(Guid id) =>
        HandOverContract.CreateEvent(CliContract.CancelEventName(id));
}
