using System.Diagnostics;
using InstallerClean.Helpers;
using InstallerClean.Services;

namespace InstallerClean.Tests.Services.Integration;

/// <summary>
/// A newer version putting itself at an older copy's path once that copy has ended
/// (<see cref="ExeReplacement"/>), against real files and a real process standing in for
/// the older copy: a copy of Windows PowerShell's exe, run from the path being replaced and
/// sleeping until the test ends it. The newer version is a file of known bytes, given in
/// place of the test process's own exe. Starting the exe at the path afterwards is a seam,
/// answering as a newer version that started, did not, or would not end.
/// </summary>
public sealed class ExeReplacementTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ic-replace-").FullName;
    private readonly List<Process> _started = [];
    private readonly List<IDisposable> _held = [];
    private readonly IDisposable _crashLog = TestCrashLog.OwnFolder();

    private static readonly string PowerShell =
        Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    private static readonly byte[] NewerBytes = Enumerable.Range(0, 64 * 1024).Select(i => (byte)(i * 7)).ToArray();

    public void Dispose()
    {
        try
        {
            foreach (var held in _held) held.Dispose();
            foreach (var process in _started)
            {
                try { if (!process.HasExited) process.Kill(); process.WaitForExit(); } catch { }
                process.Dispose();
            }
            try
            {
                foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(_root, recursive: true);
            }
            catch { }
        }
        finally
        {
            _crashLog.Dispose();
        }
    }

    private string Exe => Path.Combine(_root, "copy", "InstallerClean.exe");
    private string Replacing => Exe + ExeReplacement.ReplacingSuffix;
    private string Backup => Exe + ExeReplacement.BackupSuffix;
    private string Lock => Exe + ExeReplacement.LockSuffix;

    /// <summary>The older copy: PowerShell's exe copied to <see cref="Exe"/>, started from there and sleeping.</summary>
    private Process StartOldCopy()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Exe)!);
        File.Copy(PowerShell, Exe);
        var info = new ProcessStartInfo(Exe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 300" })
            info.ArgumentList.Add(argument);
        var process = Process.Start(info)!;
        _started.Add(process);
        return process;
    }

    private string NewerFile()
    {
        var path = Path.Combine(_root, "newer.exe");
        File.WriteAllBytes(path, NewerBytes);
        return path;
    }

    private (ReplaceRequest Request, EventWaitHandle Started) RequestFor(Process oldCopy, string? exePath = null)
    {
        var id = Guid.NewGuid();
        var started = HandOverContract.CreateStartedEvent(id)!;
        return (new ReplaceRequest(exePath ?? Exe, oldCopy.Id, id.ToString("N"), []), started);
    }

    /// <summary>Ends <paramref name="oldCopy"/> once <paramref name="started"/> is set, as an older copy closes once the event is.</summary>
    private static Task EndOnceSet(Process oldCopy, EventWaitHandle started) => Task.Run(() =>
    {
        if (!started.WaitOne(TimeSpan.FromSeconds(60))) return;
        oldCopy.Kill();
        oldCopy.WaitForExit();
    });

    /// <summary>Opens <paramref name="path"/> sharing reading alone, which refuses its replacement, until the answer is disposed.</summary>
    private FileStream Hold(string path)
    {
        var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        _held.Add(held);
        return held;
    }

    [Fact]
    public void The_newer_version_replaces_the_copy_once_it_has_ended_and_nothing_is_left_beside_it()
    {
        var oldCopy = StartOldCopy();
        var (request, started) = RequestFor(oldCopy);
        using var _ = started;
        var ending = EndOnceSet(oldCopy, started);

        var outcome = ExeReplacement.Run(request, startAtPath: false, new ReplaceSeams(ThisExe: NewerFile()));
        ending.Wait();

        Assert.Equal(ReplaceOutcome.Replaced, outcome);
        Assert.Equal(NewerBytes, File.ReadAllBytes(Exe));
        Assert.False(File.Exists(Replacing));
        Assert.False(File.Exists(Backup));
    }

    [Fact]
    public void The_newer_version_is_written_beside_the_copy_before_the_event_is_set()
    {
        var oldCopy = StartOldCopy();
        var (request, started) = RequestFor(oldCopy);
        using var _ = started;
        byte[]? besideWhenSet = null;
        var ending = Task.Run(() =>
        {
            if (!started.WaitOne(TimeSpan.FromSeconds(60))) return;
            besideWhenSet = File.ReadAllBytes(Replacing);
            oldCopy.Kill();
            oldCopy.WaitForExit();
        });

        ExeReplacement.Run(request, startAtPath: false, new ReplaceSeams(ThisExe: NewerFile()));
        ending.Wait();

        Assert.Equal(NewerBytes, besideWhenSet);
    }

    [Fact]
    public void A_path_that_is_not_the_copys_own_exe_is_refused_with_nothing_written_and_no_event()
    {
        var oldCopy = StartOldCopy();
        var other = Path.Combine(_root, "copy", "Other.exe");
        File.Copy(PowerShell, other);
        var (request, started) = RequestFor(oldCopy, other);
        using var _ = started;

        var outcome = ExeReplacement.Run(request, startAtPath: false, new ReplaceSeams(ThisExe: NewerFile()));

        Assert.Equal(ReplaceOutcome.NotHandedOver, outcome);
        Assert.False(started.WaitOne(0));
        Assert.Equal(File.ReadAllBytes(PowerShell), File.ReadAllBytes(other));
        Assert.False(File.Exists(other + ExeReplacement.ReplacingSuffix));
        Assert.Contains("is not the one process", TestCrashLog.Text());
    }

    [Fact]
    public void A_copy_still_closing_past_the_notice_is_waited_for_and_replaced_once_it_has()
    {
        var oldCopy = StartOldCopy();
        var (request, started) = RequestFor(oldCopy);
        using var _ = started;
        var ending = Task.Run(async () =>
        {
            if (!started.WaitOne(TimeSpan.FromSeconds(60))) return;
            await Task.Delay(TimeSpan.FromSeconds(3));
            oldCopy.Kill();
            await oldCopy.WaitForExitAsync();
        });

        var outcome = ExeReplacement.Run(request, startAtPath: false,
            new ReplaceSeams(ThisExe: NewerFile(), OldCopyNotice: TimeSpan.FromSeconds(1)));
        ending.Wait();

        Assert.Equal(ReplaceOutcome.Replaced, outcome);
        Assert.Equal(NewerBytes, File.ReadAllBytes(Exe));
        Assert.Contains("the replace waits on for it", TestCrashLog.Text());
    }

    [Fact]
    public void A_link_at_the_path_is_refused_with_nothing_written_and_no_event()
    {
        var oldCopy = StartOldCopy();
        var link = Path.Combine(_root, "copy", "Link.exe");
        try
        {
            File.CreateSymbolicLink(link, Exe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // this host cannot create symlinks; CI's runner can
        }
        var (request, started) = RequestFor(oldCopy, link);
        using var _ = started;

        var outcome = ExeReplacement.Run(request, startAtPath: false, new ReplaceSeams(ThisExe: NewerFile()));

        Assert.Equal(ReplaceOutcome.NotHandedOver, outcome);
        Assert.False(started.WaitOne(0));
        Assert.True(new FileInfo(link).Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.Equal(File.ReadAllBytes(PowerShell), File.ReadAllBytes(Exe));
        Assert.False(File.Exists(link + ExeReplacement.ReplacingSuffix));
        Assert.Contains("is a link", TestCrashLog.Text());
    }

    [Fact]
    public void A_read_only_exe_is_replaced_and_the_newer_version_is_read_only_too()
    {
        var oldCopy = StartOldCopy();
        File.SetAttributes(Exe, File.GetAttributes(Exe) | FileAttributes.ReadOnly);
        var (request, started) = RequestFor(oldCopy);
        using var _ = started;
        var ending = EndOnceSet(oldCopy, started);

        var outcome = ExeReplacement.Run(request, startAtPath: false,
            new ReplaceSeams(ThisExe: NewerFile(), MoveRetryBound: TimeSpan.FromSeconds(1)));
        ending.Wait();

        Assert.Equal(ReplaceOutcome.Replaced, outcome);
        Assert.Equal(NewerBytes, File.ReadAllBytes(Exe));
        Assert.True(File.GetAttributes(Exe).HasFlag(FileAttributes.ReadOnly));
        Assert.False(File.Exists(Backup));
    }

    [Fact]
    public void A_read_only_exe_put_back_is_read_only_again()
    {
        var oldCopy = StartOldCopy();
        File.SetAttributes(Exe, File.GetAttributes(Exe) | FileAttributes.ReadOnly);
        var (request, started) = RequestFor(oldCopy);
        using var _ = started;
        var ending = EndOnceSet(oldCopy, started);

        var outcome = ExeReplacement.Run(request, startAtPath: true, new ReplaceSeams(
            ThisExe: NewerFile(), MoveRetryBound: TimeSpan.FromSeconds(1), StartAndAwait: _ => NewerAtPath.NotStarted));
        ending.Wait();

        Assert.Equal(ReplaceOutcome.RolledBack, outcome);
        Assert.Equal(File.ReadAllBytes(PowerShell), File.ReadAllBytes(Exe));
        Assert.True(File.GetAttributes(Exe).HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public void A_read_only_exe_is_read_only_again_where_the_older_version_cannot_be_put_back()
    {
        var oldCopy = StartOldCopy();
        File.SetAttributes(Exe, File.GetAttributes(Exe) | FileAttributes.ReadOnly);
        var (request, started) = RequestFor(oldCopy);
        using var _ = started;
        var ending = EndOnceSet(oldCopy, started);

        var outcome = ExeReplacement.Run(request, startAtPath: true, new ReplaceSeams(
            ThisExe: NewerFile(), MoveRetryBound: TimeSpan.FromSeconds(1), StartAndAwait: path =>
            {
                Hold(path);
                return NewerAtPath.NotStarted;
            }));
        ending.Wait();

        Assert.Equal(ReplaceOutcome.NotRolledBack, outcome);
        foreach (var held in _held) held.Dispose();
        Assert.Equal(NewerBytes, File.ReadAllBytes(Exe));
        Assert.True(File.GetAttributes(Exe).HasFlag(FileAttributes.ReadOnly));
        Assert.True(File.Exists(Backup));
    }

    [Fact]
    public void A_second_replace_of_the_same_exe_is_refused_while_the_first_holds_its_file()
    {
        var oldCopy = StartOldCopy();
        using var first = new FileStream(Lock, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        var (request, started) = RequestFor(oldCopy);
        using var _ = started;

        var outcome = ExeReplacement.Run(request, startAtPath: false, new ReplaceSeams(ThisExe: NewerFile()));

        Assert.Equal(ReplaceOutcome.NotHandedOver, outcome);
        Assert.False(started.WaitOne(0));
        Assert.False(File.Exists(Replacing));
    }

    [Fact]
    public void A_replace_holds_its_file_while_it_runs_and_it_is_gone_once_it_ends()
    {
        var oldCopy = StartOldCopy();
        var (request, started) = RequestFor(oldCopy);
        using var _ = started;
        bool? removedWhileRunning = null;
        var ending = Task.Run(() =>
        {
            if (!started.WaitOne(TimeSpan.FromSeconds(60))) return;
            removedWhileRunning = ExeReplacement.TryRemoveLeftovers(Exe);
            oldCopy.Kill();
            oldCopy.WaitForExit();
        });

        var outcome = ExeReplacement.Run(request, startAtPath: false, new ReplaceSeams(ThisExe: NewerFile()));
        ending.Wait();

        Assert.Equal(ReplaceOutcome.Replaced, outcome);
        Assert.False(removedWhileRunning);
        Assert.Equal(NewerBytes, File.ReadAllBytes(Exe));
        Assert.False(File.Exists(Lock));
    }

    [Fact]
    public void An_exe_held_past_the_retries_is_left_in_place_with_nothing_beside_it()
    {
        var oldCopy = StartOldCopy();
        var (request, started) = RequestFor(oldCopy);
        using var _ = started;
        Hold(Exe);
        var ending = EndOnceSet(oldCopy, started);

        var outcome = ExeReplacement.Run(request, startAtPath: false,
            new ReplaceSeams(ThisExe: NewerFile(), MoveRetryBound: TimeSpan.FromSeconds(1)));
        ending.Wait();

        Assert.Equal(ReplaceOutcome.Refused, outcome);
        foreach (var held in _held) held.Dispose();
        Assert.Equal(File.ReadAllBytes(PowerShell), File.ReadAllBytes(Exe));
        Assert.False(File.Exists(Replacing));
        // Windows answers a move over a file held open with a sharing violation or access denied.
        var log = TestCrashLog.Text();
        Assert.True(
            log.Contains($"failed with error {Interop.Native.Kernel32.ERROR_SHARING_VIOLATION}.")
                || log.Contains($"failed with error {Interop.Native.Kernel32.ERROR_ACCESS_DENIED}."),
            log);
        // The second name is the exe's own file where the volume links it, so the hold that
        // refused the move can refuse its delete as well. A clean-up made with nothing
        // holding the file and nothing running from it takes it.
        Assert.True(ExeReplacement.TryRemoveLeftovers(Exe));
        Assert.False(File.Exists(Backup));
    }

    [Fact]
    public void An_exe_let_go_of_inside_the_retries_is_replaced()
    {
        var oldCopy = StartOldCopy();
        var (request, started) = RequestFor(oldCopy);
        using var _ = started;
        var held = Hold(Exe);
        var ending = Task.Run(async () =>
        {
            if (!started.WaitOne(TimeSpan.FromSeconds(60))) return;
            oldCopy.Kill();
            await oldCopy.WaitForExitAsync();
            await Task.Delay(TimeSpan.FromSeconds(1));
            held.Dispose();
        });

        var outcome = ExeReplacement.Run(request, startAtPath: false,
            new ReplaceSeams(ThisExe: NewerFile(), MoveRetryBound: TimeSpan.FromSeconds(20)));
        ending.Wait();

        Assert.Equal(ReplaceOutcome.Replaced, outcome);
        Assert.Equal(NewerBytes, File.ReadAllBytes(Exe));
    }

    [Fact]
    public void A_newer_version_that_does_not_start_at_the_path_is_replaced_by_the_older_one_again()
    {
        var oldCopy = StartOldCopy();
        var (request, started) = RequestFor(oldCopy);
        using var _ = started;
        var ending = EndOnceSet(oldCopy, started);
        byte[]? atPathWhenStarted = null;

        var outcome = ExeReplacement.Run(request, startAtPath: true, new ReplaceSeams(
            ThisExe: NewerFile(),
            StartAndAwait: path =>
            {
                atPathWhenStarted = File.ReadAllBytes(path);
                return NewerAtPath.NotStarted;
            }));
        ending.Wait();

        Assert.Equal(ReplaceOutcome.RolledBack, outcome);
        Assert.Equal(NewerBytes, atPathWhenStarted);
        Assert.Equal(File.ReadAllBytes(PowerShell), File.ReadAllBytes(Exe));
        Assert.False(File.Exists(Replacing));
        Assert.False(File.Exists(Backup));
    }

    [Fact]
    public void A_newer_version_that_starts_at_the_path_stays_and_the_older_one_goes()
    {
        var oldCopy = StartOldCopy();
        var (request, started) = RequestFor(oldCopy);
        using var _ = started;
        var ending = EndOnceSet(oldCopy, started);

        var outcome = ExeReplacement.Run(request, startAtPath: true,
            new ReplaceSeams(ThisExe: NewerFile(), StartAndAwait: _ => NewerAtPath.Started));
        ending.Wait();

        Assert.Equal(ReplaceOutcome.Replaced, outcome);
        Assert.Equal(NewerBytes, File.ReadAllBytes(Exe));
        Assert.False(File.Exists(Backup));
    }

    [Fact]
    public void A_newer_version_that_will_not_end_is_left_at_the_path_with_the_older_one_kept_beside_it()
    {
        var oldCopy = StartOldCopy();
        var (request, started) = RequestFor(oldCopy);
        using var _ = started;
        var ending = EndOnceSet(oldCopy, started);

        var outcome = ExeReplacement.Run(request, startAtPath: true,
            new ReplaceSeams(ThisExe: NewerFile(), StartAndAwait: _ => NewerAtPath.StillRunning));
        ending.Wait();

        Assert.Equal(ReplaceOutcome.NewerLeftRunning, outcome);
        Assert.Equal(NewerBytes, File.ReadAllBytes(Exe));
        Assert.Equal(File.ReadAllBytes(PowerShell), File.ReadAllBytes(Backup));
        var log = TestCrashLog.Text();
        Assert.Contains("still running", log);
        Assert.Contains(ExeReplacement.BackupSuffix, log);
    }

    [Fact]
    public void Where_the_volume_will_not_link_the_exe_a_copy_of_it_is_what_is_put_back()
    {
        var oldCopy = StartOldCopy();
        var (request, started) = RequestFor(oldCopy);
        using var _ = started;
        var ending = EndOnceSet(oldCopy, started);

        var outcome = ExeReplacement.Run(request, startAtPath: true, new ReplaceSeams(
            ThisExe: NewerFile(),
            HardLink: (_, _) => Interop.Native.Kernel32.ERROR_INVALID_FUNCTION,
            StartAndAwait: _ => NewerAtPath.NotStarted));
        ending.Wait();

        Assert.Equal(ReplaceOutcome.RolledBack, outcome);
        Assert.Equal(File.ReadAllBytes(PowerShell), File.ReadAllBytes(Exe));
        Assert.False(File.Exists(Backup));
        Assert.Contains($"The volume refused the exe a second name with error {Interop.Native.Kernel32.ERROR_INVALID_FUNCTION}.", TestCrashLog.Text());
    }

    [Fact]
    public void A_plain_file_left_at_the_newer_versions_name_is_written_over()
    {
        var oldCopy = StartOldCopy();
        File.WriteAllText(Replacing, "left by an earlier update");
        var (request, started) = RequestFor(oldCopy);
        using var _ = started;
        var ending = EndOnceSet(oldCopy, started);

        var outcome = ExeReplacement.Run(request, startAtPath: false, new ReplaceSeams(ThisExe: NewerFile()));
        ending.Wait();

        Assert.Equal(ReplaceOutcome.Replaced, outcome);
        Assert.Equal(NewerBytes, File.ReadAllBytes(Exe));
    }

    [Fact]
    public void A_link_at_the_newer_versions_name_is_refused_and_never_followed()
    {
        var oldCopy = StartOldCopy();
        var decoy = Path.Combine(_root, "decoy.txt");
        File.WriteAllText(decoy, "decoy");
        try
        {
            File.CreateSymbolicLink(Replacing, decoy);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // this host cannot create symlinks; CI's runner can
        }
        var (request, started) = RequestFor(oldCopy);
        using var _ = started;

        var outcome = ExeReplacement.Run(request, startAtPath: false, new ReplaceSeams(ThisExe: NewerFile()));

        Assert.Equal(ReplaceOutcome.NotHandedOver, outcome);
        Assert.False(started.WaitOne(0));
        Assert.Equal("decoy", File.ReadAllText(decoy));
        Assert.True(new FileInfo(Replacing).Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.Equal(File.ReadAllBytes(PowerShell), File.ReadAllBytes(Exe));
    }

    [Fact]
    public void Leftovers_beside_the_exe_go_while_a_file_is_at_the_path()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Exe)!);
        File.WriteAllText(Exe, "exe");
        File.WriteAllText(Replacing, "replacing");
        File.WriteAllText(Backup, "backup");

        Assert.True(ExeReplacement.TryRemoveLeftovers(Exe));
        Assert.False(File.Exists(Replacing));
        Assert.False(File.Exists(Backup));
        Assert.True(File.Exists(Exe));
    }

    [Fact]
    public void Leftovers_beside_the_exe_stay_while_a_replace_holds_its_file()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Exe)!);
        File.WriteAllText(Exe, "exe");
        File.WriteAllText(Replacing, "replacing");
        using var held = new FileStream(Lock, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);

        Assert.False(ExeReplacement.TryRemoveLeftovers(Exe));
        Assert.True(File.Exists(Replacing));
    }

    [Fact]
    public void A_held_file_left_by_a_replace_that_ended_goes_with_the_rest()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Exe)!);
        File.WriteAllText(Exe, "exe");
        File.WriteAllText(Lock, string.Empty);
        File.WriteAllText(Backup, "backup");

        Assert.True(ExeReplacement.TryRemoveLeftovers(Exe));
        Assert.False(File.Exists(Lock));
        Assert.False(File.Exists(Backup));
    }

    [Fact]
    public void Leftovers_beside_the_exe_stay_while_nothing_is_at_the_path()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Exe)!);
        File.WriteAllText(Backup, "backup");

        Assert.False(ExeReplacement.TryRemoveLeftovers(Exe));
        Assert.True(File.Exists(Backup));
    }

    [Fact]
    public void A_link_left_beside_the_exe_is_not_removed_through()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Exe)!);
        File.WriteAllText(Exe, "exe");
        var decoy = Path.Combine(_root, "decoy.txt");
        File.WriteAllText(decoy, "decoy");
        try
        {
            File.CreateSymbolicLink(Backup, decoy);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // this host cannot create symlinks; CI's runner can
        }

        Assert.False(ExeReplacement.TryRemoveLeftovers(Exe));
        Assert.True(File.Exists(Backup));
        Assert.Equal("decoy", File.ReadAllText(decoy));
    }
}
