using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using InstallerClean.Helpers;
using InstallerClean.Interop.Native;
using InstallerClean.Resources;
using Microsoft.Win32.SafeHandles;

namespace InstallerClean.Services;

/// <summary>
/// Puts a newer version at the running exe's own path, for the copies that update by
/// replacing their own file: the portable, the slim and the command line's own download.
/// The command line's copy in the setup's folder moves itself aside here too, before the
/// setup writes the newer version in its place.
/// </summary>
/// <remarks>
/// <para>
/// Windows keeps a running exe's file open, and the open lets the file be renamed but
/// not replaced or deleted. So the newer version is first written beside the running
/// exe under its name with <see cref="NewSuffix"/> on the end, then the running exe is
/// renamed to its name with <see cref="OldSuffix"/> on the end, and the newer version is
/// renamed to the name it had. The newer version, once started, deletes the renamed
/// one. The path stays the same, so shortcuts and taskbar pins start the newer version.
/// No rename overwrites anything, so a file that appears at either name in between fails
/// the rename rather than being replaced.
/// </para>
/// <para>
/// ONCE THE RUNNING EXE IS RENAMED, IT CAN LOAD NO ASSEMBLY IT HAS NOT LOADED ALREADY. A
/// single-file build opens its own file by path for each assembly the first time one is
/// needed, and after the rename that path holds the newer version or nothing. So
/// <see cref="PrepareToMoveAside"/> runs before every rename, and the code that runs
/// after one is short and reaches nothing outside what it loaded. Code added between a
/// rename and the end of the process, or the putting back that undoes it, has to reach
/// only assemblies that walk loads, or it fails on a published build and passes on a
/// build from source, whose assemblies are loose files beside the exe.
/// </para>
/// <para>
/// Once the running exe is renamed, Windows can answer a question about its original
/// path from the running copy rather than the file now there, and a version read is
/// one such question. So code after a rename asks about the exe's path only through the
/// file system:
/// renaming and checking for it here, starting it, and reading its version from its
/// bytes with <see cref="FileVersionResource"/>.
/// </para>
/// </remarks>
public static class SelfReplacement
{
    /// <summary>What the running exe's name gains while the newer version takes its place.</summary>
    public const string OldSuffix = ".old";

    /// <summary>What the newer version's name carries while it waits beside the running exe.</summary>
    public const string NewSuffix = ".new";

    /// <summary>
    /// Writes <paramref name="downloaded"/> beside <paramref name="exePath"/> as a new
    /// file under the exe's name with <see cref="NewSuffix"/> on the end, and answers with
    /// its path. Nothing running is renamed. Throws <see cref="UpdateFailedException"/>
    /// where it cannot be written, having left nothing of its own behind.
    /// </summary>
    /// <remarks>
    /// The file is created new in the exe's folder, never moved in from the download's
    /// folder and never written over an existing file, so it takes that folder's inherited
    /// permissions and is opened by every account the folder lets in. A file moved in
    /// keeps the permissions it was given where it was made, and the download's folder
    /// admits only Administrators and SYSTEM. One left by an earlier update is deleted
    /// first where it is a plain file; a reparse point there is refused and never
    /// followed.
    /// </remarks>
    public static async Task<string> StageAsync(string exePath, string downloaded, CancellationToken cancellationToken)
    {
        var newPath = exePath + NewSuffix;
        var created = false;
        try
        {
            if (File.Exists(newPath)) DeletePlainFile(newPath);
            await using var input = File.OpenRead(downloaded);
            using var handle = StorageHelpers.OpenAtomic(newPath, FileAccess.Write, StorageHelpers.AtomicOpenMode.CreateNew)
                ?? throw new IOException("The file beside the running copy could not be created.");
            created = true;
            await using var output = new FileStream(handle, FileAccess.Write, bufferSize: 0);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
            return newPath;
        }
        catch (Exception ex)
        {
            if (created) StorageHelpers.TryDeleteTempFile(newPath);
            if (ex is IOException or UnauthorizedAccessException)
                throw new UpdateFailedException(UpdateFailureStage.Install,
                    "The newer version could not be written beside the running copy.", ex);
            throw;
        }
    }

    /// <summary>
    /// Loads every assembly the code after a rename can reach, while the running exe's
    /// file is still whole: this assembly, System.Diagnostics.Process,
    /// System.Diagnostics.StackTrace and <paramref name="moreRoots"/>, then every assembly
    /// each loaded one names, until nothing new loads. Also looks up one string, so the
    /// satellite for the display language is loaded. Never throws; an assembly that will
    /// not load is written to crash.log and the walk goes on.
    /// </summary>
    /// <remarks>
    /// An assembly's code reaches other assemblies only through its AssemblyRef table,
    /// and a facade reaches the assembly that holds a type through its forwarded types,
    /// so the walk follows both and loads everything that code can reach without
    /// reflection. System.Diagnostics.StackTrace is named by itself because nothing
    /// references it: the runtime loads it by name when an exception is formatted with
    /// its source lines, which crash.log does. A load made by name anywhere else after
    /// the rename has to be added here the same way.
    /// </remarks>
    public static void PrepareToMoveAside(params Assembly[] moreRoots)
    {
        try
        {
            var roots = new List<Assembly> { typeof(SelfReplacement).Assembly, typeof(Process).Assembly };
            roots.AddRange(moreRoots);
            if (TryLoad(new AssemblyName("System.Diagnostics.StackTrace")) is { } stackTrace)
                roots.Add(stackTrace);
            LoadReachable(roots);
            _ = Strings.UpdateNotice_InstallFailed;
        }
        catch (Exception ex)
        {
            CrashLog.TryWrite(ex);
        }
    }

    private static void LoadReachable(IEnumerable<Assembly> roots)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<Assembly>();
        void Add(Assembly assembly)
        {
            if (assembly.GetName().Name is { } name && seen.Add(name)) pending.Enqueue(assembly);
        }

        foreach (var root in roots) Add(root);
        while (pending.TryDequeue(out var assembly))
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name is null || seen.Contains(reference.Name)) continue;
                if (TryLoad(reference) is { } loaded) Add(loaded);
                else seen.Add(reference.Name);
            }

            Type?[] forwarded;
            try
            {
                forwarded = assembly.GetForwardedTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                forwarded = ex.Types;
            }
            foreach (var type in forwarded)
                if (type is not null) Add(type.Assembly);
        }
    }

    private static Assembly? TryLoad(AssemblyName name)
    {
        try
        {
            return Assembly.Load(name);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            CrashLog.TryWrite(ex);
            return null;
        }
    }

    /// <summary>
    /// Opens <paramref name="path"/> for reading, sharing reading and writing but not
    /// delete, and answers the handle, or null where it cannot be opened. While the handle
    /// is open Windows refuses to rename the file, since a rename asks for delete access,
    /// so a run of the command line holds its own file this way for as long as it runs,
    /// and another run's update of that file cannot move it aside. Never throws.
    /// </summary>
    public static SafeFileHandle? HoldAgainstRenaming(string path)
    {
        try
        {
            return File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        catch (Exception ex)
        {
            CrashLog.TryWrite(ex);
            return null;
        }
    }

    /// <summary>
    /// Whether a process other than this one may be running from <paramref name="exePath"/>.
    /// Only processes whose image has the exe's file name are asked, Windows giving no path
    /// for its own (System, Registry and the like). Each one answers no only where it has
    /// ended since the list was taken, or where its image path opens to a file known to be
    /// another: by the two files' identities, or where a volume gives none, by the two
    /// files being on volumes with different serial numbers. Anything that cannot be read
    /// answers yes. Never throws.
    /// </summary>
    /// <remarks>
    /// A run of the command line holds its own file with <see cref="HoldAgainstRenaming"/>
    /// once it reaches its own code. This finds a run that is still starting, and a copy
    /// that holds nothing. It is asked after <see cref="PrepareToMoveAside"/>, so what it
    /// loads is loaded before the rename.
    /// </remarks>
    public static bool AnotherProcessMayRunFrom(string exePath)
    {
        Process[] named;
        try
        {
            named = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exePath));
        }
        catch (Exception ex)
        {
            CrashLog.TryWrite(ex);
            return true;
        }
        try
        {
            var self = Environment.ProcessId;
            var ours = FileOnDisk.At(exePath);
            var buffer = new char[MaxImagePathLength];
            foreach (var process in named)
            {
                if (process.Id != self && !RunsFromAnotherFile(process.Id, ours, buffer))
                    return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            CrashLog.TryWrite(ex);
            return true;
        }
        finally
        {
            foreach (var process in named) process.Dispose();
        }
    }

    /// <summary>
    /// Whether the process <paramref name="processId"/> has ended, or runs from a file
    /// known to be another than <paramref name="ours"/>. <paramref name="buffer"/> takes
    /// its image path.
    /// </summary>
    private static bool RunsFromAnotherFile(int processId, FileOnDisk ours, char[] buffer)
    {
        using var process = Kernel32.OpenProcess(
            Kernel32.PROCESS_QUERY_LIMITED_INFORMATION, bInheritHandle: false, (uint)processId);
        if (process.IsInvalid)
            return Marshal.GetLastPInvokeError() == Kernel32.ERROR_INVALID_PARAMETER;

        var length = (uint)buffer.Length;
        if (!Kernel32.QueryFullProcessImageName(process, 0, buffer, ref length)) return false;
        return FileOnDisk.At(new string(buffer, 0, (int)length)).IsKnownToBeAnotherThan(ours);
    }

    /// <summary>The longest path Windows gives for a process's image, in characters.</summary>
    private const int MaxImagePathLength = 32768;

    /// <summary>
    /// What tells two files apart: the file's identity, where its volume gives one, and
    /// otherwise the serial number of the volume it is on, read when a comparison needs it.
    /// </summary>
    private readonly record struct FileOnDisk(string Path, FileIdentity? Identity)
    {
        public static FileOnDisk At(string path) =>
            new(path, new FileIdentityReader().ReadOutcome(path, out var identity) == FileIdentityRead.Read
                ? identity
                : null);

        /// <summary>
        /// Whether this is known to be a different file from <paramref name="other"/>: both
        /// identities read and different, or, where either identity is missing, both volume
        /// serial numbers read and different.
        /// </summary>
        public bool IsKnownToBeAnotherThan(FileOnDisk other)
        {
            if (Identity is { } mine && other.Identity is { } theirs) return mine != theirs;
            return VolumeSerialOf(Path) is { } myVolume
                && VolumeSerialOf(other.Path) is { } theirVolume
                && myVolume != theirVolume;
        }

        private static uint? VolumeSerialOf(string path)
        {
            using var handle = StorageHelpers.OpenForMetadata(path, out _);
            return !handle.IsInvalid && Kernel32.GetVolumeSerialNumberByHandle(
                    handle, IntPtr.Zero, 0, out var serial, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0)
                ? serial
                : null;
        }
    }

    /// <summary>
    /// Renames the running exe aside and the file <see cref="StageAsync"/> wrote to the
    /// name it had, and answers with <see cref="PutBack"/> for it. Throws
    /// <see cref="UpdateFailedException"/> where either rename fails, having put back
    /// what it had done. <see cref="PrepareToMoveAside"/> comes first. Ctrl+C on
    /// <paramref name="token"/> ends the wait for the first rename, as in
    /// <see cref="MoveAside"/>, and never the second: stopping between the two would leave
    /// this exe renamed aside with nothing at its path.
    /// </summary>
    public static Func<bool> SwapIn(string exePath, CancellationToken token = default)
    {
        MoveAside(exePath, token);
        try
        {
            var newPath = exePath + NewSuffix;
            WaitOutHold(newPath, () => File.Move(newPath, exePath), CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryRename(exePath + OldSuffix, exePath);
            throw new UpdateFailedException(UpdateFailureStage.Install,
                "The newer version could not be moved into place.", ex);
        }
        return () => PutBack(exePath);
    }

    /// <summary>
    /// Renames the running exe to its own name with <see cref="OldSuffix"/> on the end,
    /// so the newer version can be written at its path, by <see cref="SwapIn"/> or by
    /// the setup, and answers with <see cref="PutBack"/> for it. Throws
    /// <see cref="UpdateFailedException"/> where the rename fails, having deleted at most
    /// a copy an earlier update left under that name.
    /// <see cref="PrepareToMoveAside"/> comes first. Ctrl+C on <paramref name="token"/>
    /// while it waits for a file another process holds open (<see cref="WaitOutHold"/>)
    /// throws <see cref="OperationCanceledException"/>, with this exe at its path.
    /// </summary>
    public static Func<bool> MoveAside(string exePath, CancellationToken token = default)
    {
        var oldPath = exePath + OldSuffix;
        try
        {
            // One left by an earlier update whose newer version could not delete it.
            if (File.Exists(oldPath)) WaitOutHold(oldPath, () => DeleteIfPlainFile(oldPath), token);
            WaitOutHold(exePath, () => File.Move(exePath, oldPath), token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UpdateFailedException(UpdateFailureStage.Install,
                "The running copy could not be moved aside.", ex);
        }
        return () => PutBack(exePath);
    }

    /// <summary>
    /// Renames the running exe back to its own path, first renaming whatever is at that
    /// path to the name with <see cref="NewSuffix"/> on the end, where it waits for
    /// <see cref="TryRemoveOldCopy"/>. Answers whether the running exe is at its own path
    /// again, the one state in which its process can go on loading what it needs. Never
    /// throws.
    /// </summary>
    private static bool PutBack(string exePath)
    {
        var oldPath = exePath + OldSuffix;
        var newPath = exePath + NewSuffix;
        if (File.Exists(exePath))
        {
            if (!TryDeletePlainFile(newPath, waitOutHold: true)) return false;
            if (!TryRename(exePath, newPath)) return false;
        }
        return TryRename(oldPath, exePath);
    }

    /// <summary>
    /// Runs <paramref name="change"/>, a rename or delete of <paramref name="path"/>, and
    /// runs it again every <see cref="HoldRetryInterval"/> while it fails with a sharing
    /// violation, for up to <see cref="HoldWait"/>. An antivirus can hold a file it has
    /// just seen written or started for a moment, and a sharing violation is what a
    /// rename or delete meets then. Where the wait runs out, throws
    /// <see cref="FileHeldOpenException"/>, naming what holds the file. Any other failure
    /// is thrown at once. Ctrl+C on <paramref name="token"/> ends the wait with
    /// <see cref="OperationCanceledException"/>; a change whose wait must not end early is
    /// given <see cref="CancellationToken.None"/>.
    /// </summary>
    /// <remarks>
    /// A rename of a file a run of the command line holds as its own meets a sharing
    /// violation too, so the update that meets one waits the whole of
    /// <see cref="HoldWait"/> before it says InstallerClean is in use. The window's
    /// hand-over waits on the window's own thread, so each wait stays well short of the
    /// five seconds or so after which Windows marks a window that has drawn nothing as not
    /// responding.
    /// </remarks>
    private static void WaitOutHold(string path, Action change, CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            try
            {
                change();
                return;
            }
            catch (IOException ex) when (WindowsErrors.Of(ex) == Kernel32.ERROR_SHARING_VIOLATION)
            {
                if (Stopwatch.GetElapsedTime(started) >= HoldWait) throw new FileHeldOpenException(path, ex);
                // A token that cannot be cancelled has a handle that is never set.
                if (token.WaitHandle.WaitOne(HoldRetryInterval)) token.ThrowIfCancellationRequested();
            }
        }
    }

    /// <summary>How long <see cref="WaitOutHold"/> goes on trying a change another process holds up.</summary>
    private static readonly TimeSpan HoldWait = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan HoldRetryInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// The window's hand-over to the newer version a copy that replaces its own file has
    /// written beside itself: renames it into place, starts it, waits for it to say its
    /// own code is running, and ends this process without running any more of its code.
    /// Answers false where the newer version could not be put in place, could not be
    /// started, or did not say so (<see cref="AwaitStart"/>), with this exe back at its
    /// own path and the newer version back under its waiting name. Where Windows would not
    /// end a newer version that did not say so, this exe cannot be put back, or another
    /// process runs from the file now at the path, the newer version stays there and this
    /// process ends all the same, so the next start opens the newer version.
    /// </summary>
    /// <param name="exePath">This exe's own path.</param>
    /// <param name="toVersion">
    /// The version being handed over to. One <see cref="HandOverContract.TakesHandOverSwitches"/>
    /// answers false for is started without the event, and this process ends once it has
    /// started.
    /// </param>
    /// <param name="startBound">Test seam: how long to wait for the event, <see cref="HandOverContract.StartBound"/> where null.</param>
    /// <remarks>
    /// Synchronous on purpose, with <see cref="PrepareToMoveAside"/> done before it: on a
    /// window's thread an await would let the dispatcher run the window's own code
    /// between the renames and the end, and every wait here dispatches nothing. The start
    /// uses CreateProcess rather than the shell, which can wait on a DDE conversation and
    /// pump messages; the app runs elevated, so CreateProcess starts the newer version,
    /// whose manifest asks for elevation too, without a prompt. An antivirus can hold a
    /// program it has just seen written for a moment, so a start that fails is tried
    /// again a few times, half a second apart. What stopped the hand-over, a wait Windows
    /// refused included, goes to crash.log once this exe is back in place, or as this
    /// process ends where it is not; a look at the event Windows refuses after the bound,
    /// and a newer version that will not end, are written as they happen. The event is
    /// created before anything is renamed, and where it cannot be, nothing is: a newer
    /// version that cannot be seen to start is not put in this exe's place.
    /// </remarks>
    public static bool HandOverAndEnd(string exePath, string toVersion, TimeSpan? startBound = null)
    {
        var startedId = Guid.NewGuid();
        var takesSwitch = HandOverContract.TakesHandOverSwitches(toVersion);
        using var startedEvent = takesSwitch ? HandOverContract.CreateStartedEvent(startedId) : null;
        if (takesSwitch && startedEvent is null) return false;

        Func<bool> putBack;
        try
        {
            putBack = SwapIn(exePath);
        }
        catch (UpdateFailedException ex)
        {
            CrashLog.TryWrite(ex);
            return false;
        }

        var info = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty,
        };
        if (startedEvent is not null) info.ArgumentList.Add(HandOverContract.StartedEventArgument(startedId));
        Exception? failure = null;
        var newerRan = false;
        var newerWouldNotEnd = false;
        for (var attempt = 1; attempt <= StartAttempts; attempt++)
        {
            Process? newer;
            try
            {
                newer = Process.Start(info);
            }
            catch (Exception ex)
            {
                failure = ex;
                if (attempt < StartAttempts) Kernel32.Sleep((uint)StartRetryInterval.TotalMilliseconds);
                continue;
            }
            if (newer is null)
            {
                failure = new UpdateFailedException(UpdateFailureStage.Install,
                    "Windows started no process for the newer version.");
                if (attempt < StartAttempts) Kernel32.Sleep((uint)StartRetryInterval.TotalMilliseconds);
                continue;
            }
            newerRan = true;
            using (newer)
            {
                if (startedEvent is null) EndThisProcess();
                var answer = AwaitStart(
                    newer, startedEvent!, startBound ?? HandOverContract.StartBound, cancel: null, out var waitError);
                if (answer == NewerVersionStart.Started) EndThisProcess();
                failure = NotStartedFailure(answer, waitError, newer, startBound ?? HandOverContract.StartBound);
                // A newer version not seen to end is not renamed under.
                newerWouldNotEnd = !EndNewerVersion(newer);
            }
            break;
        }

        // A person can start the file at the path while the wait runs, and renaming a file a
        // process runs from takes from it the assemblies it has still to load.
        var newerStays = newerWouldNotEnd || (newerRan && AnotherProcessMayRunFrom(exePath)) || !putBack();
        if (failure is not null) CrashLog.TryWrite(failure);
        if (newerStays) EndThisProcess();
        return false;
    }

    private const int StartAttempts = 4;
    private static readonly TimeSpan StartRetryInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Waits for the newer version <paramref name="newer"/> to set
    /// <paramref name="started"/>, for up to <paramref name="bound"/>, and answers what
    /// came first: the event, the newer version ending, the bound, <paramref name="cancel"/>
    /// being set, or Windows refusing the wait, whose error code is
    /// <paramref name="waitError"/> (0 for every other answer). An event set by a newer
    /// version that has also ended answers <see cref="NewerVersionStart.Started"/>, and so
    /// does one found set by a look at the event alone, taken once more after the bound,
    /// <paramref name="cancel"/> or a refused wait: Windows refuses a wait on several
    /// handles where any one of them cannot be waited on, so the event can be readable
    /// where the wait was not. A look Windows refuses after the bound or
    /// <paramref name="cancel"/> is written to crash.log and the answer stands; after a
    /// refused wait it is not, the refusal being the answer already. Nothing else is
    /// written: the caller writes <see cref="NotStartedFailure"/>. The wait dispatches nothing, so on a window's
    /// thread no window code runs during it. Never throws.
    /// </summary>
    internal static NewerVersionStart AwaitStart(
        Process newer, WaitHandle started, TimeSpan bound, WaitHandle? cancel, out int waitError)
    {
        // In the order of StartedIndex, EndedIndex and CancelIndex.
        List<SafeHandle> handles = cancel is null
            ? [started.SafeWaitHandle, newer.SafeHandle]
            : [started.SafeWaitHandle, newer.SafeHandle, cancel.SafeWaitHandle];
        var answer = Wait(handles, bound, out waitError) switch
        {
            StartedIndex => NewerVersionStart.Started,
            EndedIndex => NewerVersionStart.Ended,
            CancelIndex => NewerVersionStart.Cancelled,
            TimedOutWait => NewerVersionStart.TimedOut,
            _ => NewerVersionStart.WaitFailed,
        };
        if (answer is NewerVersionStart.Started or NewerVersionStart.Ended) return answer;

        switch (LookAt(started, out var lookError))
        {
            case HandleLook.Signalled:
                waitError = 0;
                return NewerVersionStart.Started;
            case HandleLook.NotSignalled:
                return answer;
            default:
                if (answer != NewerVersionStart.WaitFailed) CrashLog.TryWrite(LookFailure(lookError));
                return answer;
        }
    }

    /// <summary>Where <see cref="AwaitStart"/> puts the started event in the handles it waits on.</summary>
    private const int StartedIndex = 0;

    /// <summary>Where <see cref="AwaitStart"/> puts the newer version's process.</summary>
    private const int EndedIndex = 1;

    /// <summary>Where <see cref="AwaitStart"/> puts the Ctrl+C handle, where there is one.</summary>
    private const int CancelIndex = 2;

    /// <summary>
    /// Looks at <paramref name="handle"/> without waiting and answers whether it is
    /// signalled, or that Windows refused the look, whose error code is
    /// <paramref name="error"/>. Writes nothing. Never throws.
    /// </summary>
    internal static HandleLook LookAt(WaitHandle handle, out int error) => LookAt(handle.SafeWaitHandle, out error);

    private static HandleLook LookAt(SafeHandle handle, out int error) => Wait([handle], TimeSpan.Zero, out error) switch
    {
        0 => HandleLook.Signalled,
        TimedOutWait => HandleLook.NotSignalled,
        _ => HandleLook.Failed,
    };

    /// <summary>What is written to crash.log where Windows refuses a look at the started event with <paramref name="error"/>.</summary>
    internal static UpdateFailedException LookFailure(int error) => new(UpdateFailureStage.Install,
        $"Looking at whether the newer version had said it started failed with error {error}.");

    /// <summary>
    /// Ends <paramref name="newer"/>, a newer version that has not said its own code is
    /// running, and waits up to <see cref="EndBound"/> for it to have ended. Answers
    /// whether it runs no more of its code: it has ended, or Windows has accepted the
    /// request to end it, which no process can refuse once it is accepted, and which a
    /// process stuck in the middle of a call into Windows can take a while to complete.
    /// Answers false where Windows refused the request and the process is still running,
    /// and the caller then never renames the file it runs from. Any answer from
    /// TerminateProcess but success counts as refused. Where the newer version has not
    /// been seen to end, one line goes to crash.log: the request refused, with its error
    /// code; the newer version still running after the bound; or the wait for it refused,
    /// with its error code. Never throws.
    /// </summary>
    internal static bool EndNewerVersion(Process newer)
    {
        try
        {
            var handle = newer.SafeHandle;
            // A look Windows refuses goes on to the request to end it, as for a newer
            // version still running.
            if (LookAt(handle, out _) == HandleLook.Signalled) return true;
            var added = false;
            bool accepted;
            var terminateError = 0;
            try
            {
                handle.DangerousAddRef(ref added);
                accepted = Kernel32.TerminateProcess(handle.DangerousGetHandle(), NotStartedExitCode);
                if (!accepted) terminateError = Marshal.GetLastPInvokeError();
            }
            finally
            {
                if (added) handle.DangerousRelease();
            }
            var ended = Wait([handle], EndBound, out var waitError);
            if (ended == 0) return true;
            var waitFailed = ended == FailedWait ? $" Waiting for it to end failed with error {waitError}." : string.Empty;
            CrashLog.TryWrite(new UpdateFailedException(UpdateFailureStage.Install,
                !accepted ? $"Windows refused the request to end the newer version, with error {terminateError}.{waitFailed}"
                : ended == FailedWait ? $"Waiting for the newer version to end, after Windows accepted the request to end it, failed with error {waitError}."
                : "The newer version had not ended a few seconds after Windows accepted the request to end it."));
            return accepted;
        }
        catch (Exception ex)
        {
            CrashLog.TryWrite(ex);
            return false;
        }
    }

    /// <summary>How long <see cref="EndNewerVersion"/> waits for the newer version to have ended.</summary>
    private static readonly TimeSpan EndBound = TimeSpan.FromSeconds(5);

    /// <summary>The exit code <see cref="EndNewerVersion"/> ends the newer version with.</summary>
    private const uint NotStartedExitCode = 1;

    /// <summary>
    /// What a newer version that did not say its own code is running is written to
    /// crash.log as, for <paramref name="answer"/>: the exit code it ended with, how long
    /// it was waited for, or the error code <paramref name="waitError"/> Windows refused the
    /// wait with. Never throws.
    /// </summary>
    internal static UpdateFailedException NotStartedFailure(
        NewerVersionStart answer, int waitError, Process newer, TimeSpan bound)
    {
        string message;
        try
        {
            message = answer switch
            {
                NewerVersionStart.Ended =>
                    $"The newer version ended with exit code {newer.ExitCode} before it said it had started.",
                NewerVersionStart.Cancelled => "Ctrl+C came before the newer version said it had started.",
                NewerVersionStart.WaitFailed =>
                    $"Waiting for the newer version to say it had started failed with error {waitError}.",
                _ => $"The newer version did not say it had started within {bound.TotalSeconds:0} seconds.",
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            message = "The newer version ended before it said it had started.";
        }
        return new UpdateFailedException(UpdateFailureStage.Install, message);
    }

    /// <summary>What <see cref="Wait"/> answers where the bound ran out.</summary>
    private const int TimedOutWait = -1;

    /// <summary>What <see cref="Wait"/> answers where Windows refused the wait.</summary>
    private const int FailedWait = -2;

    /// <summary>
    /// Waits on <paramref name="handles"/> for up to <paramref name="bound"/>, dispatching
    /// nothing, and answers the index of the first signalled, <see cref="TimedOutWait"/>
    /// or <see cref="FailedWait"/>, with the error code Windows refused it with in
    /// <paramref name="error"/> (0 for every other answer). Writes nothing: each caller
    /// writes a refusal once.
    /// </summary>
    private static int Wait(List<SafeHandle> handles, TimeSpan bound, out int error)
    {
        error = 0;
        var raw = new IntPtr[handles.Count];
        var added = new bool[handles.Count];
        try
        {
            for (var i = 0; i < handles.Count; i++)
            {
                handles[i].DangerousAddRef(ref added[i]);
                raw[i] = handles[i].DangerousGetHandle();
            }
            var milliseconds = (uint)Math.Clamp(bound.TotalMilliseconds, 0, uint.MaxValue - 1);
            var result = Kernel32.WaitForMultipleObjects((uint)raw.Length, raw, bWaitAll: false, milliseconds);
            if (result < Kernel32.WAIT_OBJECT_0 + (uint)raw.Length) return (int)(result - Kernel32.WAIT_OBJECT_0);
            if (result == Kernel32.WAIT_TIMEOUT) return TimedOutWait;
            error = Marshal.GetLastPInvokeError();
            return FailedWait;
        }
        finally
        {
            for (var i = 0; i < handles.Count; i++)
                if (added[i]) handles[i].DangerousRelease();
        }
    }

    /// <summary>
    /// Ends this process at once, running none of its code: no exit handler, no
    /// finaliser and nothing WPF does on shutdown, any of which could need an assembly
    /// this exe can no longer load once it has been renamed.
    /// </summary>
    private static void EndThisProcess() => Kernel32.TerminateProcess(Kernel32.GetCurrentProcess(), 0);

    /// <summary>
    /// Deletes what an update left beside this exe: the copy it renamed aside, and a
    /// newer version that waited beside it and was never put in place. Each only where
    /// it is a plain file. Answers whether nothing is left there. The copy that renamed
    /// itself can still be closing when the newer version starts, and Windows refuses to
    /// delete a file a running program was started from, so a false is ordinary and the
    /// caller tries again.
    /// </summary>
    public static bool TryRemoveOldCopy(string exePath) =>
        TryDeletePlainFile(exePath + OldSuffix) & TryDeletePlainFile(exePath + NewSuffix);

    /// <summary>
    /// Deletes <paramref name="path"/> where it is a plain file, and answers whether
    /// nothing is left there. With <paramref name="waitOutHold"/>, waits for another
    /// process holding the file open, as <see cref="WaitOutHold"/> does.
    /// </summary>
    private static bool TryDeletePlainFile(string path, bool waitOutHold = false)
    {
        try
        {
            if (!File.Exists(path)) return true;
            if (waitOutHold) WaitOutHold(path, () => DeleteIfPlainFile(path), CancellationToken.None);
            else DeletePlainFile(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// <see cref="DeletePlainFile"/> where anything is still at <paramref name="path"/>,
    /// so a file that goes while <see cref="WaitOutHold"/> waits is not a failure.
    /// </summary>
    private static void DeleteIfPlainFile(string path)
    {
        if (File.Exists(path)) DeletePlainFile(path);
    }

    /// <summary>
    /// Deletes <paramref name="path"/> where it is a plain file, and refuses with an
    /// <see cref="IOException"/> where it is a reparse point, which this never follows.
    /// </summary>
    private static void DeletePlainFile(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The file in the way is a reparse point.");
        File.Delete(path);
    }

    /// <summary>
    /// Renames <paramref name="from"/> to <paramref name="to"/>, waiting for another
    /// process holding it open as <see cref="WaitOutHold"/> does, and answers whether it
    /// did. A failure is written to crash.log. Never throws.
    /// </summary>
    private static bool TryRename(string from, string to)
    {
        try
        {
            WaitOutHold(from, () => File.Move(from, to), CancellationToken.None);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.TryWrite(ex);
            return false;
        }
    }
}

/// <summary>What came first while a copy waited for the newer version it started (<see cref="SelfReplacement.AwaitStart"/>).</summary>
internal enum NewerVersionStart
{
    /// <summary>The newer version set the event: its own code is running.</summary>
    Started,

    /// <summary>The newer version ended without setting it.</summary>
    Ended,

    /// <summary>The wait's bound ran out.</summary>
    TimedOut,

    /// <summary>Ctrl+C came first.</summary>
    Cancelled,

    /// <summary>Windows refused the wait.</summary>
    WaitFailed,
}

/// <summary>What a look at a handle without waiting found (<see cref="SelfReplacement.LookAt(WaitHandle, out int)"/>).</summary>
internal enum HandleLook
{
    /// <summary>It is signalled.</summary>
    Signalled,

    /// <summary>It is not signalled.</summary>
    NotSignalled,

    /// <summary>Windows refused the look.</summary>
    Failed,
}
