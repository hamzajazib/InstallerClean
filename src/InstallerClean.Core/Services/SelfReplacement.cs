namespace InstallerClean.Services;

/// <summary>
/// Puts a newer version at the running exe's own path, for the copies that update by
/// replacing their own file: the portable, the slim and Scoop's.
/// </summary>
/// <remarks>
/// Windows keeps a running exe's file open, and the open lets the file be renamed but
/// not replaced or deleted. So the running exe is renamed to its own name with
/// <see cref="OldSuffix"/> on the end, the newer version is moved to the name it had,
/// and the newer version, once started, deletes the renamed one. The path stays the
/// same, so shortcuts, taskbar pins and Scoop's shortcut all start the newer version.
/// Neither move overwrites anything, so a file that appears at either name in between
/// fails the move rather than being replaced.
/// </remarks>
public static class SelfReplacement
{
    /// <summary>What the running exe's name gains while the newer version takes its place.</summary>
    public const string OldSuffix = ".old";

    /// <summary>
    /// Moves <paramref name="newFile"/> to <paramref name="exePath"/> with the running
    /// exe renamed aside, and answers with the step that puts everything back. Throws
    /// <see cref="UpdateFailedException"/> where a step fails, having put back what it
    /// had done.
    /// </summary>
    public static Action Replace(string exePath, string newFile)
    {
        var oldPath = exePath + OldSuffix;
        try
        {
            // One left by an earlier update whose newer version could not delete it.
            if (File.Exists(oldPath)) DeletePlainFile(oldPath);
            File.Move(exePath, oldPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UpdateFailedException(UpdateFailureStage.Install,
                "The running copy could not be moved aside.", ex);
        }

        try
        {
            File.Move(newFile, exePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryMoveBack(oldPath, exePath);
            throw new UpdateFailedException(UpdateFailureStage.Install,
                "The newer version could not be moved into place.", ex);
        }

        return () =>
        {
            try
            {
                File.Delete(exePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Helpers.CrashLog.TryWrite(ex);
                return;
            }
            TryMoveBack(oldPath, exePath);
        };
    }

    /// <summary>
    /// Deletes the copy an update renamed aside, where there is one and it is a plain
    /// file. Answers whether nothing is left there. The copy that renamed it can still
    /// be closing when the newer version starts, and Windows refuses to delete a file
    /// a running program was started from, so a false is ordinary and the caller tries
    /// again.
    /// </summary>
    public static bool TryRemoveOldCopy(string exePath)
    {
        var oldPath = exePath + OldSuffix;
        try
        {
            if (!File.Exists(oldPath)) return true;
            DeletePlainFile(oldPath);
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

    private static void TryMoveBack(string oldPath, string exePath)
    {
        try
        {
            File.Move(oldPath, exePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Helpers.CrashLog.TryWrite(ex);
        }
    }
}
