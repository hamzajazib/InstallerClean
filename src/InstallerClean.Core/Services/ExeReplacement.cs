using System.Diagnostics;
using System.Runtime.InteropServices;
using InstallerClean.Helpers;
using InstallerClean.Interop.Native;
using Microsoft.Win32.SafeHandles;

namespace InstallerClean.Services;

/// <summary>
/// Puts this exe, a newer version a copy downloaded into the staging folder, at that
/// copy's path once the copy has ended (<see cref="HandOverContract.ReadReplaceRequest"/>).
/// </summary>
/// <remarks>
/// <para>
/// The newer version is written beside the copy's exe under the exe's name with
/// <see cref="ReplacingSuffix"/> on the end, and once the copy has ended it is moved over
/// the exe in one step, so the path holds the old file or the new one at every instant and
/// no file anything runs from is ever renamed. Before that move the old file is given a
/// second name, the exe's name with <see cref="BackupSuffix"/> on the end, which is what is
/// moved back where the newer version does not start.
/// </para>
/// <para>
/// Everything that can fail while the copy is still running, the check that the path is
/// the copy's own and the write beside it, comes before the started event is set. Where
/// one fails, the event stays unset, the copy is still running, and nothing at its path
/// has changed.
/// </para>
/// <para>
/// For as long as it runs, the replace holds a file named for the exe with
/// <see cref="LockSuffix"/> on the end, open and shared with nothing, and Windows deletes
/// it as the handle closes, however the process ends. <see cref="TryRemoveLeftovers"/>
/// leaves the names beside the exe alone while that file cannot be deleted.
/// </para>
/// <para>
/// The steps after the copy has ended run in this version's code, the newer one, so a
/// later release can change them for every copy that updates to it.
/// </para>
/// </remarks>
internal static class ExeReplacement
{
    /// <summary>What the newer version's name carries while it waits beside the exe it replaces.</summary>
    public const string ReplacingSuffix = ".replacing";

    /// <summary>What the exe's second name carries while the newer version is put in its place.</summary>
    public const string BackupSuffix = ".backup";

    /// <summary>What the name of the file a replace holds while it runs carries.</summary>
    public const string LockSuffix = ".updating";

    /// <summary>
    /// How long a move refused because the file is held is tried again. An antivirus can
    /// hold a program it has just seen written or ended while it looks at it, and a file
    /// held that way answers a move with a sharing violation, a lock violation or access denied.
    /// </summary>
    internal static readonly TimeSpan MoveRetryBound = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan FirstMoveRetryInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan LongestMoveRetryInterval = TimeSpan.FromSeconds(2);

    /// <summary>The longest path Windows gives for a process's image, in characters.</summary>
    private const int MaxImagePathLength = 32768;

    /// <summary>
    /// Carries out <paramref name="request"/> and answers how it ended. Where
    /// <paramref name="startAtPath"/>, the exe at the path is started once the newer
    /// version is there and waited for until it says its own code is running, and where it
    /// does not, the old file is put back. Every failure is written to crash.log. Never throws.
    /// A command <paramref name="request"/> carries is not run here: the caller runs it, or
    /// refuses the request.
    /// </summary>
    public static ReplaceOutcome Run(ReplaceRequest request, bool startAtPath) =>
        Run(request, startAtPath, new ReplaceSeams());

    /// <summary>Test seam: <see cref="Run(ReplaceRequest, bool)"/>, with <paramref name="seams"/> in place of the real parts.</summary>
    internal static ReplaceOutcome Run(ReplaceRequest request, bool startAtPath, ReplaceSeams seams)
    {
        try
        {
            return RunSteps(request, startAtPath, seams);
        }
        catch (Exception ex)
        {
            CrashLog.TryWrite(ex);
            return ReplaceOutcome.Failed;
        }
    }

    private static ReplaceOutcome RunSteps(ReplaceRequest request, bool startAtPath, ReplaceSeams seams)
    {
        var exePath = request.ExePath;
        var replacing = exePath + ReplacingSuffix;
        var backup = exePath + BackupSuffix;
        var backupNote = string.Empty;

        using var oldCopy = Kernel32.OpenProcess(
            Ntdll.SYNCHRONIZE | Kernel32.PROCESS_QUERY_LIMITED_INFORMATION, bInheritHandle: false, (uint)request.ProcessId);
        if (oldCopy.IsInvalid)
            return Fail(ReplaceOutcome.NotHandedOver,
                $"The copy to be replaced, process {request.ProcessId}, could not be opened: error {Marshal.GetLastPInvokeError()}.");
        // A link at the path would pass the check below as the file it leads to, and the move
        // replaces a link rather than following it, so the file it leads to would keep the
        // older version.
        if ((File.GetAttributes(exePath) & FileAttributes.ReparsePoint) != 0)
            return Fail(ReplaceOutcome.NotHandedOver, "The file to be replaced is a link.");
        if (!IsImageOf(oldCopy, exePath))
            return Fail(ReplaceOutcome.NotHandedOver,
                $"The file to be replaced is not the one process {request.ProcessId} runs from.");

        using var held = TryHoldLock(exePath + LockSuffix, out var lockFailure);
        if (held is null)
            return Fail(ReplaceOutcome.NotHandedOver, "Another replace of this exe is running, or its file could not be made.", lockFailure);

        if (!TryWriteNew(replacing, seams.ThisExe ?? Environment.ProcessPath, out var writeFailure))
            return Fail(ReplaceOutcome.NotHandedOver, "The newer version could not be written beside the exe.", writeFailure);

        if (!HandOverContract.SignalStarted(request.StartedEvent))
        {
            TryDeletePlainFile(replacing);
            return ReplaceOutcome.NotHandedOver;
        }

        // The copy has been told to close and closes whatever this process does next, so the
        // wait has no bound: a copy that takes long to close is still replaced once it has.
        if (!WaitForEnd(oldCopy, out var waitError))
        {
            TryDeletePlainFile(replacing);
            return Fail(ReplaceOutcome.OldCopyNotSeenToEnd,
                $"Waiting for the copy to be replaced to end failed with error {waitError}; nothing was replaced.");
        }

        // A move over a read-only file is refused, so the attribute comes off the exe for the
        // replace and goes back on whichever version ends at the path.
        var attributes = File.GetAttributes(exePath);
        var readOnly = (attributes & FileAttributes.ReadOnly) != 0;
        if (readOnly && !TrySetAttributes(exePath, attributes & ~FileAttributes.ReadOnly, out var attributeFailure))
        {
            TryDeletePlainFile(replacing);
            return Fail(ReplaceOutcome.Refused, "The exe's read-only attribute could not be taken off.", attributeFailure);
        }

        if (!TryMakeBackup(exePath, backup, seams.HardLink ?? HardLink, out var linkError, out var backupFailure))
        {
            TryDeletePlainFile(replacing);
            if (readOnly) TrySetAttributes(exePath, attributes, out _);
            return Fail(ReplaceOutcome.Refused, "The exe could not be kept before it was replaced.", backupFailure);
        }
        if (linkError != 0)
            backupNote = $" The older version beside it is a copy, the volume having refused a second name with error {linkError}.";

        var retryBound = seams.MoveRetryBound ?? MoveRetryBound;
        if (!TryMoveOver(replacing, exePath, retryBound, out var moveError))
        {
            TryDeletePlainFile(replacing);
            TryDeletePlainFile(backup);
            if (readOnly) TrySetAttributes(exePath, attributes, out _);
            return Fail(ReplaceOutcome.Refused,
                $"The newer version could not be moved over the exe; the last try failed with error {moveError}.");
        }
        if (readOnly) TrySetAttributes(exePath, File.GetAttributes(exePath) | FileAttributes.ReadOnly, out _);

        if (!startAtPath)
        {
            TryDeletePlainFile(backup);
            return ReplaceOutcome.Replaced;
        }

        switch ((seams.StartAndAwait ?? StartAndAwait)(exePath))
        {
            case NewerAtPath.Started:
                TryDeletePlainFile(backup);
                return ReplaceOutcome.Replaced;
            case NewerAtPath.StillRunning:
                return Fail(ReplaceOutcome.NewerLeftRunning,
                    "The newer version at the exe's path did not say it had started and could not be ended, so it was left "
                    + $"there, still running, and the older version was left beside it under the name ending {BackupSuffix}."
                    + backupNote);
        }

        if (readOnly) TrySetAttributes(exePath, File.GetAttributes(exePath) & ~FileAttributes.ReadOnly, out _);
        if (TryMoveOver(backup, exePath, retryBound, out var backError))
        {
            if (readOnly) TrySetAttributes(exePath, attributes, out _);
            return Fail(ReplaceOutcome.RolledBack,
                "The newer version at the exe's path did not start, so the older version was put back there." + backupNote);
        }
        return Fail(ReplaceOutcome.NotRolledBack,
            "The newer version at the exe's path did not start, and the older version could not be put back; the last "
            + $"try failed with error {backError}. The newer version is at the path, not running, and the older version "
            + $"is beside it under the name ending {BackupSuffix}." + backupNote);
    }

    /// <summary>
    /// Creates <paramref name="path"/>, open and shared with nothing, deleted by Windows as
    /// the handle closes, and answers the handle; null where it cannot, with what failed in
    /// <paramref name="failure"/>. A plain file left at the name, by a process that ended
    /// before Windows could delete it, is deleted first; one another replace holds cannot be,
    /// and a reparse point there is refused. The create asks for a file that does not exist,
    /// so it follows nothing at the name.
    /// </summary>
    private static FileStream? TryHoldLock(string path, out Exception? failure)
    {
        failure = null;
        try
        {
            if (File.Exists(path)) DeletePlainFile(path);
            return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failure = ex;
            return null;
        }
    }

    /// <summary>Sets <paramref name="path"/>'s attributes, and answers whether it did, with what failed in <paramref name="failure"/>.</summary>
    private static bool TrySetAttributes(string path, FileAttributes attributes, out Exception? failure)
    {
        failure = null;
        try
        {
            File.SetAttributes(path, attributes);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.TryWrite(ex);
            failure = ex;
            return false;
        }
    }

    /// <summary>Writes <paramref name="message"/> to crash.log, with <paramref name="inner"/> inside it, and answers <paramref name="outcome"/>.</summary>
    private static ReplaceOutcome Fail(ReplaceOutcome outcome, string message, Exception? inner = null)
    {
        CrashLog.TryWrite(new UpdateFailedException(UpdateFailureStage.Install, message, inner));
        return outcome;
    }

    /// <summary>
    /// Whether <paramref name="exePath"/> is the file the process <paramref name="process"/>
    /// runs from: the two files' identities, where both can be read, and otherwise the two
    /// full paths, compared ignoring case.
    /// </summary>
    private static bool IsImageOf(SafeProcessHandle process, string exePath)
    {
        var buffer = new char[MaxImagePathLength];
        var length = (uint)buffer.Length;
        if (!Kernel32.QueryFullProcessImageName(process, 0, buffer, ref length)) return false;
        var image = new string(buffer, 0, (int)length);

        var reader = new FileIdentityReader();
        if (reader.ReadOutcome(image, out var imageIdentity) == FileIdentityRead.Read
            && reader.ReadOutcome(exePath, out var exeIdentity) == FileIdentityRead.Read)
            return imageIdentity == exeIdentity;
        return string.Equals(Path.GetFullPath(image), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="path"/>, a file created new
    /// through <see cref="StorageHelpers.OpenAtomic"/> in the exe's folder, so it takes
    /// that folder's inherited permissions, and flushed to the disk. A plain file left at
    /// that name is deleted first; a reparse point there is refused and never followed.
    /// Answers whether it was written, with what failed in <paramref name="failure"/>,
    /// having left nothing of its own at the name.
    /// </summary>
    private static bool TryWriteNew(string path, string? source, out Exception? failure)
    {
        failure = null;
        var created = false;
        try
        {
            if (source is null) throw new IOException("This process has no path to copy.");
            if (File.Exists(path)) DeletePlainFile(path);
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var handle = StorageHelpers.OpenAtomic(path, FileAccess.Write, StorageHelpers.AtomicOpenMode.CreateNew)
                ?? throw new IOException($"The file ending {Path.GetExtension(path)} could not be created.");
            created = true;
            using var output = new FileStream(handle, FileAccess.Write, bufferSize: 0);
            input.CopyTo(output);
            output.Flush(flushToDisk: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (created) TryDeletePlainFile(path);
            failure = ex;
            return false;
        }
    }

    /// <summary>
    /// Gives the exe at <paramref name="exePath"/> the second name <paramref name="backup"/>
    /// through <paramref name="hardLink"/>, or where the volume will not link it, a copy
    /// written the way <see cref="TryWriteNew"/> writes one, with the link's error code in
    /// <paramref name="linkError"/> (0 where the link was made). A plain file left at that
    /// name is deleted first.
    /// </summary>
    private static bool TryMakeBackup(
        string exePath, string backup, Func<string, string, int> hardLink, out int linkError, out Exception? failure)
    {
        failure = null;
        linkError = 0;
        try
        {
            if (File.Exists(backup)) DeletePlainFile(backup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failure = ex;
            return false;
        }
        linkError = hardLink(backup, exePath);
        return linkError == 0 || TryWriteNew(backup, exePath, out failure);
    }

    /// <summary>Gives <paramref name="existing"/> the second name <paramref name="link"/>, and answers 0, or the error Windows refused it with.</summary>
    private static int HardLink(string link, string existing) =>
        Kernel32.CreateHardLink(link, existing, IntPtr.Zero) ? 0 : Marshal.GetLastPInvokeError();

    /// <summary>
    /// Moves <paramref name="from"/> over <paramref name="to"/> in one step, written through
    /// to the disk, and tries again for up to <paramref name="bound"/> while the move is
    /// refused with a sharing violation, a lock violation or access denied, waiting a little
    /// longer each time. Answers whether it moved, with the last refusal's error code in
    /// <paramref name="error"/>.
    /// </summary>
    private static bool TryMoveOver(string from, string to, TimeSpan bound, out int error)
    {
        var started = Stopwatch.GetTimestamp();
        var interval = FirstMoveRetryInterval;
        while (true)
        {
            if (Kernel32.MoveFileEx(from, to, Kernel32.MOVEFILE_REPLACE_EXISTING | Kernel32.MOVEFILE_WRITE_THROUGH))
            {
                error = 0;
                return true;
            }
            error = Marshal.GetLastPInvokeError();
            var left = bound - Stopwatch.GetElapsedTime(started);
            if (error is not (Kernel32.ERROR_ACCESS_DENIED or Kernel32.ERROR_SHARING_VIOLATION or Kernel32.ERROR_LOCK_VIOLATION)
                || left <= TimeSpan.Zero)
                return false;
            Thread.Sleep(interval < left ? interval : left);
            interval = interval * 2 < LongestMoveRetryInterval ? interval * 2 : LongestMoveRetryInterval;
        }
    }

    /// <summary>
    /// Waits for <paramref name="process"/> to end, for as long as it takes. Answers true
    /// once it has, and false where Windows refused the wait, with its error code in
    /// <paramref name="error"/>.
    /// </summary>
    private static bool WaitForEnd(SafeProcessHandle process, out int error)
    {
        error = 0;
        var added = false;
        try
        {
            process.DangerousAddRef(ref added);
            var result = Kernel32.WaitForMultipleObjects(1, [process.DangerousGetHandle()], bWaitAll: false, Kernel32.INFINITE);
            if (result == Kernel32.WAIT_OBJECT_0) return true;
            error = Marshal.GetLastPInvokeError();
            return false;
        }
        finally
        {
            if (added) process.DangerousRelease();
        }
    }

    /// <summary>
    /// Starts the exe at <paramref name="exePath"/> with a started event and waits up to
    /// <see cref="HandOverContract.StartBound"/> for it to say its own code is running. One
    /// that does not is ended. Answers <see cref="NewerAtPath.Started"/>, or
    /// <see cref="NewerAtPath.NotStarted"/> where it could not be started, did not say so
    /// and has been ended, or no event could be made to see it by, or
    /// <see cref="NewerAtPath.StillRunning"/> where it did not say so and Windows would not
    /// end it. What failed is written to crash.log.
    /// </summary>
    private static NewerAtPath StartAndAwait(string exePath)
    {
        var id = Guids.NewGuid();
        using var started = HandOverContract.CreateStartedEvent(id);
        if (started is null) return NewerAtPath.NotStarted;

        var info = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty,
        };
        info.ArgumentList.Add(HandOverContract.StartedEventArgument(id));
        Process newer;
        try
        {
            newer = ProcessStartRetry.Start(info, _ => true, interval => Thread.Sleep(interval));
        }
        catch (Exception ex)
        {
            CrashLog.TryWrite(ex);
            return NewerAtPath.NotStarted;
        }
        using (newer)
        {
            var answer = SelfReplacement.AwaitStart(newer, started, HandOverContract.StartBound, cancel: null, out var waitError);
            if (answer == NewerVersionStart.Started) return NewerAtPath.Started;
            CrashLog.TryWrite(SelfReplacement.NotStartedFailure(answer, waitError, newer, HandOverContract.StartBound));
            return SelfReplacement.EndNewerVersion(newer) ? NewerAtPath.NotStarted : NewerAtPath.StillRunning;
        }
    }

    /// <summary>
    /// Starts the exe at <paramref name="exePath"/>, the older version put back or left in
    /// place, with <see cref="HandOverContract.UpdateFailedSwitch"/>, so it opens saying the
    /// update couldn't be installed. A start that fails is written to crash.log. Never throws.
    /// </summary>
    public static void StartWithUpdateFailed(string exePath)
    {
        try
        {
            var info = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty,
            };
            info.ArgumentList.Add(HandOverContract.UpdateFailedSwitch);
            ProcessStartRetry.Start(info, _ => true, interval => Thread.Sleep(interval)).Dispose();
        }
        catch (Exception ex)
        {
            CrashLog.TryWrite(ex);
        }
    }

    /// <summary>
    /// Deletes what a replace left beside <paramref name="exePath"/>: the newer version that
    /// waited there and the older version's second name, each only where it is a plain file,
    /// and only while a file is at the path and no replace of it is running, which is while
    /// its held file (<see cref="LockSuffix"/>) is gone or can be deleted. Answers whether
    /// nothing is left. Never throws.
    /// </summary>
    public static bool TryRemoveLeftovers(string exePath)
    {
        try
        {
            if (!File.Exists(exePath) || !TryDeletePlainFile(exePath + LockSuffix)) return false;
            return TryDeletePlainFile(exePath + ReplacingSuffix) & TryDeletePlainFile(exePath + BackupSuffix);
        }
        catch (Exception ex)
        {
            CrashLog.TryWrite(ex);
            return false;
        }
    }

    /// <summary>Deletes <paramref name="path"/> where it is a plain file, and answers whether nothing is left there.</summary>
    private static bool TryDeletePlainFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return true;
            DeletePlainFile(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
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
}

/// <summary>How a replace (<see cref="ExeReplacement.Run(ReplaceRequest, bool)"/>) ended.</summary>
internal enum ReplaceOutcome
{
    /// <summary>
    /// The started event was not set, so the copy carries on, and nothing at its path changed.
    /// </summary>
    NotHandedOver,

    /// <summary>The event was set and Windows refused the wait for the copy to end. Nothing at its path changed.</summary>
    OldCopyNotSeenToEnd,

    /// <summary>The copy has ended, and its exe is at the path still: the newer version could not be put there.</summary>
    Refused,

    /// <summary>The newer version is at the path, and where it was started, said it had started.</summary>
    Replaced,

    /// <summary>The newer version at the path did not start, and the older version is back there.</summary>
    RolledBack,

    /// <summary>
    /// The newer version at the path did not say it had started and is still running; the
    /// older version is beside it under its second name.
    /// </summary>
    NewerLeftRunning,

    /// <summary>
    /// The newer version at the path did not start, and the older version, beside it under
    /// its second name, could not be put back.
    /// </summary>
    NotRolledBack,

    /// <summary>Something the steps did not expect threw. It is in crash.log.</summary>
    Failed,
}

/// <summary>What the exe at the path did once it was started after the replace.</summary>
internal enum NewerAtPath
{
    /// <summary>It said its own code is running.</summary>
    Started,

    /// <summary>It was not started, or it did not say so and has been ended.</summary>
    NotStarted,

    /// <summary>It did not say so, and Windows would not end it.</summary>
    StillRunning,
}

/// <summary>
/// Test seams for <see cref="ExeReplacement.Run(ReplaceRequest, bool, ReplaceSeams)"/>. Each null
/// member is the real part.
/// </summary>
/// <param name="ThisExe">The file copied as the newer version, in place of this process's own.</param>
/// <param name="MoveRetryBound">In place of <see cref="ExeReplacement.MoveRetryBound"/>.</param>
/// <param name="HardLink">Makes the second name, answering 0, or the error it was refused with.</param>
/// <param name="StartAndAwait">Starts the exe at the path and answers what it did.</param>
internal sealed record ReplaceSeams(
    string? ThisExe = null,
    TimeSpan? MoveRetryBound = null,
    Func<string, string, int>? HardLink = null,
    Func<string, NewerAtPath>? StartAndAwait = null);
