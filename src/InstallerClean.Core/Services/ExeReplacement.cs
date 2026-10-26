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
/// holds the same file while it deletes, so the two never run at once.
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
    /// How long the copy is waited for before crash.log says the replace is still waiting
    /// for it to close. The wait goes on after that.
    /// </summary>
    internal static readonly TimeSpan OldCopyNotice = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long a move refused because the file is held is tried again. An antivirus can
    /// hold a program it has just seen written or ended while it looks at it, and a file
    /// held that way answers a move with a sharing violation, a lock violation or access denied.
    /// </summary>
    internal static readonly TimeSpan MoveRetryBound = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a replace tries again to hold the file named with <see cref="LockSuffix"/>
    /// where it cannot. <see cref="TryRemoveLeftovers"/> holds that file for as long as its
    /// deletes take, and a replace starting meanwhile takes it once they are done.
    /// </summary>
    internal static readonly TimeSpan HoldRetryBound = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan FirstRetryInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan LongestRetryInterval = TimeSpan.FromSeconds(2);

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

        using var oldCopy = Kernel32.OpenProcess(
            Ntdll.SYNCHRONIZE | Kernel32.PROCESS_QUERY_LIMITED_INFORMATION, bInheritHandle: false, (uint)request.ProcessId);
        if (oldCopy.IsInvalid)
            return Fail(ReplaceOutcome.NotHandedOver,
                $"The copy to be replaced, process {request.ProcessId}, could not be opened: error {Marshal.GetLastPInvokeError()}.");
        if (!TryReadAttributes(exePath, out var attributes, out var readFailure))
            return Fail(ReplaceOutcome.NotHandedOver, "The file to be replaced could not be read.", readFailure);
        // A link at the path would pass the check below as the file it leads to, and the move
        // replaces a link rather than following it, so the file it leads to would keep the
        // older version.
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            return Fail(ReplaceOutcome.NotHandedOver, "The file to be replaced is a link.");
        if (!IsImageOf(oldCopy, exePath))
            return Fail(ReplaceOutcome.NotHandedOver,
                $"The file to be replaced is not the one process {request.ProcessId} runs from.");

        using var held = TryHold(exePath, seams.HoldRetryBound ?? HoldRetryBound, out var holdFailure);
        if (held is null)
            return Fail(ReplaceOutcome.NotHandedOver,
                $"The file ending {LockSuffix}, which a replace or a clean-up of this exe holds while it runs, could not be held.",
                holdFailure);

        if (!TryWriteNew(replacing, seams.ThisExe ?? Environment.ProcessPath, out var writeFailure))
            return Fail(ReplaceOutcome.NotHandedOver, "The newer version could not be written beside the exe.", writeFailure);

        var state = new ReplaceState();
        try
        {
            return HandOverAndReplace(request, oldCopy, startAtPath, seams, state);
        }
        catch when (!state.Moved)
        {
            TakeBackBeforeTheMove(exePath, state.ReadOnlyTakenOff);
            throw;
        }
        catch when (state.ReadOnlyTakenOff)
        {
            PutReadOnlyBack(exePath);
            throw;
        }
    }

    /// <summary>How far a replace has gone, for what is taken back where a step throws.</summary>
    private sealed class ReplaceState
    {
        /// <summary>The exe's read-only attribute has been taken off for the replace.</summary>
        public bool ReadOnlyTakenOff;

        /// <summary>The newer version has been moved over the exe.</summary>
        public bool Moved;
    }

    /// <summary>
    /// The steps of <see cref="Run(ReplaceRequest, bool, ReplaceSeams)"/> once the newer
    /// version is beside the exe: the started event, the wait for <paramref name="oldCopy"/>,
    /// the second name, the move, and where <paramref name="startAtPath"/>, the start and the
    /// putting back, with how far it has gone in <paramref name="state"/>.
    /// </summary>
    private static ReplaceOutcome HandOverAndReplace(
        ReplaceRequest request, SafeProcessHandle oldCopy, bool startAtPath, ReplaceSeams seams, ReplaceState state)
    {
        var exePath = request.ExePath;
        var backup = exePath + BackupSuffix;

        if (!HandOverContract.SignalStarted(request.StartedEvent))
        {
            TryDeletePlainFile(exePath + ReplacingSuffix);
            return ReplaceOutcome.NotHandedOver;
        }

        // The copy has been told to close and closes whatever this process does next, so the
        // wait has no bound: a copy that takes long to close is still replaced once it has.
        if (!WaitForEnd(oldCopy, seams.OldCopyNotice ?? OldCopyNotice, out var waitError))
        {
            TryDeletePlainFile(exePath + ReplacingSuffix);
            return Fail(ReplaceOutcome.OldCopyNotSeenToEnd,
                $"Waiting for the copy to be replaced to end failed with error {waitError}; nothing was replaced.");
        }

        if (!TryReadAttributes(exePath, out var attributes, out var readFailure))
            return NotMoved("The exe could not be read once its copy had ended.", readFailure);
        // A move over a read-only file is refused, so the attribute comes off the exe for the
        // replace. It goes back on whichever version is at the path straight after each move,
        // so the path is read-only again before anything is started from it.
        var readOnly = (attributes & FileAttributes.ReadOnly) != 0;
        if (readOnly)
        {
            if (!TrySetReadOnly(exePath, on: false, out var attributeFailure))
                return NotMoved("The exe's read-only attribute could not be taken off.", attributeFailure);
            state.ReadOnlyTakenOff = true;
        }

        var kept = TryMakeBackup(exePath, backup, seams.HardLink ?? HardLink, out var linkError, out var backupFailure);
        var linkNote = linkError == 0 ? string.Empty : $" The volume refused the exe a second name with error {linkError}.";
        if (!kept)
            return NotMoved("The exe could not be kept before it was replaced." + linkNote, backupFailure);

        var retryBound = seams.MoveRetryBound ?? MoveRetryBound;
        if (!TryMoveOver(exePath + ReplacingSuffix, exePath, retryBound, out var moveError))
            return NotMoved(
                $"The newer version could not be moved over the exe; the last try failed with error {moveError}." + linkNote, null);
        state.Moved = true;
        if (readOnly) PutReadOnlyBack(exePath);

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
                    + linkNote);
        }

        const string notPutBack = "The newer version at the exe's path did not start, and the older version could not be put back; ";
        string leftAsItIs = "The newer version is at the path, not running, and the older version is beside it under the name "
            + $"ending {BackupSuffix}." + linkNote;
        if (readOnly && !TrySetReadOnly(exePath, on: false, out var newerAttributeFailure))
            return Fail(ReplaceOutcome.NotRolledBack,
                notPutBack + "the newer version's read-only attribute could not be taken off. " + leftAsItIs, newerAttributeFailure);
        if (TryMoveOver(backup, exePath, retryBound, out var backError))
        {
            if (readOnly) PutReadOnlyBack(exePath);
            return Fail(ReplaceOutcome.RolledBack,
                "The newer version at the exe's path did not start, so the older version was put back there." + linkNote);
        }
        if (readOnly) PutReadOnlyBack(exePath);
        return Fail(ReplaceOutcome.NotRolledBack, notPutBack + $"the last try failed with error {backError}. " + leftAsItIs);

        // A stop once the copy has ended and before the move. The exe is where it was, or
        // Windows says nothing is at the path. Which of the two is read from the exe itself,
        // the failure in hand being able to name another file, the second name among them.
        ReplaceOutcome NotMoved(string message, Exception? failure)
        {
            TakeBackBeforeTheMove(exePath, state.ReadOnlyTakenOff);
            if (IsNothingAt(exePath, out var gone))
                return Fail(ReplaceOutcome.NothingAtPath, $"{NothingAtPathMessage} The step that stopped the replace: {message}",
                    failure is null ? gone : new AggregateException(gone!, failure));
            return Fail(ReplaceOutcome.Refused, message, failure);
        }
    }

    /// <summary>
    /// Deletes the newer version waiting beside <paramref name="exePath"/> and the exe's
    /// second name, then, where <paramref name="readOnly"/> and the exe is still at the path,
    /// puts the read-only attribute back on it. Until the newer version is moved over the
    /// exe, the second name, where it is a link, is the exe's own file and carries its
    /// attributes, which is why it goes first; where it cannot be deleted and may be that
    /// link, read-only stays off, as on it would keep the name from ever being deleted, and
    /// crash.log says so.
    /// </summary>
    private static void TakeBackBeforeTheMove(string exePath, bool readOnly)
    {
        var backup = exePath + BackupSuffix;
        TryDeletePlainFile(exePath + ReplacingSuffix);
        var backupLeft = !TryDeletePlainFile(backup);
        if (!readOnly || IsNothingAt(exePath, out _)) return;
        if (backupLeft && MayBeOneFile(exePath, backup))
        {
            CrashLog.TryWrite(new UpdateFailedException(UpdateFailureStage.Install,
                $"The exe's second name, the name ending {BackupSuffix}, could not be deleted, so the exe was left without its "
                + "read-only attribute, which that name shares where it is a link."));
            return;
        }
        PutReadOnlyBack(exePath);
    }

    /// <summary>
    /// Whether <paramref name="first"/> and <paramref name="second"/> may be one file under
    /// two names: false only where both identities can be read and differ.
    /// </summary>
    private static bool MayBeOneFile(string first, string second)
    {
        var reader = new FileIdentityReader();
        return reader.ReadOutcome(first, out var firstIdentity) != FileIdentityRead.Read
            || reader.ReadOutcome(second, out var secondIdentity) != FileIdentityRead.Read
            || firstIdentity == secondIdentity;
    }

    /// <summary>What crash.log says where nothing is at the exe's path once its copy has ended.</summary>
    private const string NothingAtPathMessage =
        "Nothing was at the exe's path once its copy had ended, so the newer version was not put there.";

    /// <summary>
    /// Whether <paramref name="failure"/> is Windows answering that a file, its folder or its
    /// drive is not there: errors 2, 3 and 15.
    /// </summary>
    private static bool IsNothingThere(Exception? failure) =>
        failure is FileNotFoundException or DirectoryNotFoundException or DriveNotFoundException;

    /// <summary>
    /// Whether Windows answers that nothing is at <paramref name="exePath"/>
    /// (<see cref="IsNothingThere"/>), with its answer in <paramref name="failure"/>.
    /// </summary>
    private static bool IsNothingAt(string exePath, out Exception? failure) =>
        !TryReadAttributes(exePath, out _, out failure) && IsNothingThere(failure);

    /// <summary>
    /// Holds the file named for <paramref name="exePath"/> with <see cref="LockSuffix"/> on
    /// the end (<see cref="StorageHelpers.CreateHeld"/>), and answers its handle; null where
    /// it cannot, with the last failure in <paramref name="failure"/>. A plain file at the
    /// name is one Windows had no chance to delete, as where the PC lost power, and is deleted
    /// first; one somebody holds cannot be. Each failure is tried again, a little later each
    /// time, for up to <paramref name="bound"/>, except a folder or a reparse point at the
    /// name, which is refused at once.
    /// </summary>
    private static SafeFileHandle? TryHold(string exePath, TimeSpan bound, out Exception? failure)
    {
        var path = exePath + LockSuffix;
        SafeFileHandle? held = null;
        Exception? last = null;
        var taken = Retry(bound, () =>
        {
            try
            {
                // A folder or a link at the name is refused at once; a plain file is deleted.
                if (TryReadAttributes(path, out var found, out _))
                {
                    if ((found & FileAttributes.ReparsePoint) != 0) throw new NotAPlainFileException(path, "a reparse point");
                    if ((found & FileAttributes.Directory) != 0) throw new NotAPlainFileException(path, "a folder");
                    File.Delete(path);
                }
                held = StorageHelpers.CreateHeld(path, out var error);
                if (held is not null) return RetryStep.Done;
                if (error == 0) throw new NotAPlainFileException(path, "a reparse point");
                last = new IOException($"The file ending {LockSuffix} could not be created: error {error}.");
                return RetryStep.Again;
            }
            catch (NotAPlainFileException ex)
            {
                last = ex;
                return RetryStep.Stop;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                last = ex;
                return RetryStep.Again;
            }
        });
        failure = taken ? null : last;
        return taken ? held : null;
    }

    /// <summary>What one try of <see cref="Retry"/> answered.</summary>
    private enum RetryStep
    {
        /// <summary>It succeeded.</summary>
        Done,

        /// <summary>It failed, and may be tried again.</summary>
        Again,

        /// <summary>It failed, and trying again cannot change that.</summary>
        Stop,
    }

    /// <summary>
    /// Calls <paramref name="attempt"/> until it answers <see cref="RetryStep.Done"/> or
    /// <see cref="RetryStep.Stop"/>, or until <paramref name="bound"/> has passed since the
    /// first call, waiting a little longer between calls each time. Answers whether it
    /// answered <see cref="RetryStep.Done"/>.
    /// </summary>
    private static bool Retry(TimeSpan bound, Func<RetryStep> attempt)
    {
        var started = Stopwatch.GetTimestamp();
        var interval = FirstRetryInterval;
        while (true)
        {
            var step = attempt();
            if (step != RetryStep.Again) return step == RetryStep.Done;
            var left = bound - Stopwatch.GetElapsedTime(started);
            if (left <= TimeSpan.Zero) return false;
            Thread.Sleep(interval < left ? interval : left);
            interval = interval * 2 < LongestRetryInterval ? interval * 2 : LongestRetryInterval;
        }
    }

    /// <summary>Reads <paramref name="path"/>'s attributes, and answers whether it could, with what failed in <paramref name="failure"/>.</summary>
    private static bool TryReadAttributes(string path, out FileAttributes attributes, out Exception? failure)
    {
        failure = null;
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            attributes = default;
            failure = ex;
            return false;
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
            failure = ex;
            return false;
        }
    }

    /// <summary>
    /// Puts the read-only attribute on <paramref name="path"/> where <paramref name="on"/>,
    /// and takes it off otherwise, and answers whether it did, with what failed in
    /// <paramref name="failure"/>.
    /// </summary>
    private static bool TrySetReadOnly(string path, bool on, out Exception? failure) =>
        TryReadAttributes(path, out var attributes, out failure)
        && TrySetAttributes(
            path, on ? attributes | FileAttributes.ReadOnly : attributes & ~FileAttributes.ReadOnly, out failure);

    /// <summary>
    /// Puts the read-only attribute on whatever is at <paramref name="exePath"/>. A failure
    /// is written to crash.log.
    /// </summary>
    private static void PutReadOnlyBack(string exePath)
    {
        if (!TrySetReadOnly(exePath, on: true, out var failure))
            CrashLog.TryWrite(new UpdateFailedException(UpdateFailureStage.Install,
                "The read-only attribute could not be put back on the exe.", failure));
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
        var last = 0;
        var moved = Retry(bound, () =>
        {
            if (Kernel32.MoveFileEx(from, to, Kernel32.MOVEFILE_REPLACE_EXISTING | Kernel32.MOVEFILE_WRITE_THROUGH))
                return RetryStep.Done;
            last = Marshal.GetLastPInvokeError();
            return last is Kernel32.ERROR_ACCESS_DENIED or Kernel32.ERROR_SHARING_VIOLATION or Kernel32.ERROR_LOCK_VIOLATION
                ? RetryStep.Again
                : RetryStep.Stop;
        });
        error = moved ? 0 : last;
        return moved;
    }

    /// <summary>
    /// Waits for <paramref name="process"/> to end, for as long as it takes, writing to
    /// crash.log once <paramref name="notice"/> has passed that it is still waiting. Answers
    /// true once it has ended, and false where Windows refused the wait, with its error code
    /// in <paramref name="error"/>.
    /// </summary>
    private static bool WaitForEnd(SafeProcessHandle process, TimeSpan notice, out int error)
    {
        var milliseconds = (uint)Math.Clamp(notice.TotalMilliseconds, 0, uint.MaxValue - 1);
        var result = WaitOn(process, milliseconds, out error);
        if (result == Kernel32.WAIT_TIMEOUT)
        {
            CrashLog.TryWrite(new TimeoutException(
                $"The copy to be replaced had not closed {notice.TotalSeconds:0} seconds after it was told to; the replace waits on for it."));
            result = WaitOn(process, Kernel32.INFINITE, out error);
        }
        return result == Kernel32.WAIT_OBJECT_0;
    }

    /// <summary>
    /// Waits up to <paramref name="milliseconds"/> for <paramref name="process"/> and answers
    /// what WaitForMultipleObjects answered, with the error code in <paramref name="error"/>
    /// where it refused the wait.
    /// </summary>
    private static uint WaitOn(SafeProcessHandle process, uint milliseconds, out int error)
    {
        error = 0;
        var added = false;
        try
        {
            process.DangerousAddRef(ref added);
            var result = Kernel32.WaitForMultipleObjects(1, [process.DangerousGetHandle()], bWaitAll: false, milliseconds);
            if (result == Kernel32.WAIT_FAILED) error = Marshal.GetLastPInvokeError();
            return result;
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
    /// and only while a file is at the path. Where any of those or the file a replace holds
    /// (<see cref="LockSuffix"/>) is beside the exe, it holds that file while it deletes, so
    /// it runs only where no replace of the exe is running, and no replace starts until it
    /// is done. It tries once, so a caller that is to ride out a replace's hold tries again.
    /// Answers whether nothing is left. Never throws.
    /// </summary>
    public static bool TryRemoveLeftovers(string exePath) => TryRemoveLeftovers(exePath, whileHeld: null);

    /// <summary>
    /// Test seam: <see cref="TryRemoveLeftovers(string)"/>, running <paramref name="whileHeld"/>
    /// while the file is held and before anything is deleted.
    /// </summary>
    internal static bool TryRemoveLeftovers(string exePath, Action? whileHeld)
    {
        try
        {
            if (!File.Exists(exePath)) return false;
            var replacing = exePath + ReplacingSuffix;
            var backup = exePath + BackupSuffix;
            if (!File.Exists(replacing) && !File.Exists(backup) && !File.Exists(exePath + LockSuffix)) return true;
            using var held = TryHold(exePath, TimeSpan.Zero, out _);
            if (held is null) return false;
            whileHeld?.Invoke();
            return TryDeletePlainFile(replacing) & TryDeletePlainFile(backup);
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
            throw new NotAPlainFileException(path, "a reparse point");
        File.Delete(path);
    }

    /// <summary>
    /// Something other than a plain file at a name a replace writes, holds or deletes: a
    /// folder, or a reparse point, which it never follows.
    /// </summary>
    private sealed class NotAPlainFileException(string path, string found)
        : IOException($"The name ending {Path.GetExtension(path)} is {found}, not a plain file.");
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

    /// <summary>
    /// The copy has ended and nothing is at its path. The newer version was not put there,
    /// and nothing was started.
    /// </summary>
    NothingAtPath,

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
/// <param name="OldCopyNotice">In place of <see cref="ExeReplacement.OldCopyNotice"/>.</param>
/// <param name="MoveRetryBound">In place of <see cref="ExeReplacement.MoveRetryBound"/>.</param>
/// <param name="HoldRetryBound">In place of <see cref="ExeReplacement.HoldRetryBound"/>.</param>
/// <param name="HardLink">Makes the second name, answering 0, or the error it was refused with.</param>
/// <param name="StartAndAwait">Starts the exe at the path and answers what it did.</param>
internal sealed record ReplaceSeams(
    string? ThisExe = null,
    TimeSpan? OldCopyNotice = null,
    TimeSpan? MoveRetryBound = null,
    TimeSpan? HoldRetryBound = null,
    Func<string, string, int>? HardLink = null,
    Func<string, NewerAtPath>? StartAndAwait = null);
