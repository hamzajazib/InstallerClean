using System.Diagnostics;
using System.Threading;
using InstallerClean.Helpers;
using InstallerClean.Resources;
using InstallerClean.Services;

namespace InstallerClean.Cli;

/// <summary>
/// The console, the single-instance mutex and the processes <see cref="CommandLineUpdate"/>
/// works with, for one run.
/// </summary>
internal sealed class ConsoleUpdateHost(string arg) : ICommandLineUpdateHost
{
    /// <summary>The mutex the window, a Move or Delete, and the setup all name.</summary>
    internal const string InstanceMutexName = @"Global\InstallerClean_SingleInstance";

    private Mutex? _mutex;
    private bool _holdsMutex;
    private volatile bool _newerVersionRunning;

    /// <summary>
    /// Whether the newer version is running in this console. The Ctrl+C handler reads it:
    /// the console sends Ctrl+C to both processes, and while this is true the newer
    /// version answers it, so this run prints nothing and keeps waiting for its exit code
    /// and its summary.
    /// </summary>
    public bool NewerVersionRunning => _newerVersionRunning;

    public void WriteLine(string line) => Console.WriteLine(line);

    public void Write(string text) => Console.Write(text);

    public string? ReadLine()
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
        _mutex = new Mutex(initiallyOwned: false, InstanceMutexName);
        try
        {
            _holdsMutex = _mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // Its last owner ended without releasing it; the runtime hands it to this thread.
            _holdsMutex = true;
        }
        if (!_holdsMutex)
        {
            _mutex.Dispose();
            _mutex = null;
        }
        return _holdsMutex;
    }

    public void ReleaseInstanceLock()
    {
        if (_holdsMutex) _mutex?.ReleaseMutex();
        _holdsMutex = false;
        _mutex?.Dispose();
        _mutex = null;
    }

    public Version? FileVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return info.FileMajorPart == 0 && info.FileMinorPart == 0 && info.FileBuildPart == 0
                ? null
                : new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Loads, before this exe is renamed aside, what the run reaches after it: starting
    /// the setup and the newer version, reading a file's version, the Application-log
    /// write, which also makes its source check here, and the English machine line, whose
    /// first lookup in en-GB probes for resources the console's own language never asks
    /// for.
    /// </summary>
    public void PrepareToMoveAside()
    {
        SelfReplacement.PrepareToMoveAside(
            typeof(FileVersionInfo).Assembly, typeof(EventLog).Assembly, typeof(ConsoleUpdateHost).Assembly);
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

    public int? RunSetup(string file, IReadOnlyList<string> arguments)
    {
        using var process = Start(file, arguments, Path.GetDirectoryName(file));
        if (process is null) return null;
        process.WaitForExit();
        return process.ExitCode;
    }

    public int? RunNewerVersion(string file, IReadOnlyList<string> arguments, string fromVersion, string toVersion)
    {
        Console.Out.Flush();
        _newerVersionRunning = true;
        using var process = Start(file, arguments, workingDirectory: null);
        if (process is null)
        {
            _newerVersionRunning = false;
            return null;
        }
        MachineContract.WriteEventLog(CliEventClass.UpdatedNotice,
            () => Program.UpdatedEventLogLine(arg, fromVersion, toVersion, arguments));
        process.WaitForExit();
        return process.ExitCode;
    }

    /// <summary>
    /// Starts <paramref name="file"/> sharing this console, its standard handles and, where
    /// <paramref name="workingDirectory"/> is null, its working folder, so a relative path
    /// given to the newer version means what it meant to this run. An antivirus can hold a
    /// program it has just seen written for a moment, so a start that fails is tried again
    /// a few times, half a second apart. Answers null where every try failed, each written
    /// to crash.log.
    /// </summary>
    private static Process? Start(string file, IReadOnlyList<string> arguments, string? workingDirectory)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false };
        if (workingDirectory is not null) info.WorkingDirectory = workingDirectory;
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return Process.Start(info);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
            {
                CrashLog.TryWrite(ex);
                if (attempt == StartAttempts) return null;
                Thread.Sleep(StartRetryInterval);
            }
        }
    }

    private const int StartAttempts = 4;
    private static readonly TimeSpan StartRetryInterval = TimeSpan.FromMilliseconds(500);
}
