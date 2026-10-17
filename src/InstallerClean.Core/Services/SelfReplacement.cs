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
    /// written beside itself: renames it into place, starts it, and ends this process
    /// without running any more of its code. Answers false only where the newer version
    /// could not be put in place or started and this exe is back at its own path, with
    /// the newer version back under its waiting name. Where this exe cannot be put back,
    /// the newer version stays at the path and this process ends all the same, so the
    /// next start opens the newer version.
    /// </summary>
    /// <remarks>
    /// Synchronous on purpose, with <see cref="PrepareToMoveAside"/> done before it: on a
    /// window's thread an await would let the dispatcher run the window's own code
    /// between the renames and the end. The start uses CreateProcess rather than the
    /// shell, which can wait on a DDE conversation and pump messages; the app runs
    /// elevated, so CreateProcess starts the newer version, whose manifest asks for
    /// elevation too, without a prompt. An antivirus can hold a program it has just seen
    /// written for a moment, so a start that fails is tried again a few times, half a
    /// second apart. What failed goes to crash.log only once this exe is back in place.
    /// </remarks>
    public static bool HandOverAndEnd(string exePath)
    {
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
        Exception? failure = null;
        for (var attempt = 1; attempt <= StartAttempts; attempt++)
        {
            try
            {
                using var started = Process.Start(info);
                EndThisProcess();
            }
            catch (Exception ex)
            {
                failure = ex;
                if (attempt < StartAttempts) Thread.Sleep(StartRetryInterval);
            }
        }

        if (!putBack()) EndThisProcess();
        if (failure is not null) CrashLog.TryWrite(failure);
        return false;
    }

    private const int StartAttempts = 4;
    private static readonly TimeSpan StartRetryInterval = TimeSpan.FromMilliseconds(500);

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
