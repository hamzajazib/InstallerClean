using InstallerClean.Cli;
using InstallerClean.Helpers;
using InstallerClean.Tests.Helpers;

namespace InstallerClean.Tests.Helpers.Integration;

/// <summary>
/// How the command line starts the newer version it downloaded
/// (<see cref="ConsoleUpdateHost.RunNewerVersion"/>), on a real process: the stand-in
/// records what it was started with and where, and ends without setting the started event,
/// so the start answers that it did not happen.
/// </summary>
public sealed class ConsoleUpdateHostStartTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("ic-host-start-").FullName;
    private readonly IDisposable _crashLog = TestCrashLog.OwnFolder();

    public void Dispose()
    {
        try
        {
            TestCrashLog.TryDelete(_folder);
        }
        finally
        {
            _crashLog.Dispose();
        }
    }

    [Fact]
    public void The_newer_version_runs_in_the_folder_the_command_was_typed_in_with_the_replace_switches_once()
    {
        var typedIn = Directory.CreateDirectory(Path.Combine(_folder, "typed in")).FullName;
        var written = Directory.CreateDirectory(Path.Combine(_folder, "written")).FullName;
        var replaces = Path.Combine(_folder, "app", "installerclean-cli.exe");
        var before = Environment.CurrentDirectory;
        int? answer;
        try
        {
            Environment.CurrentDirectory = typedIn;
            answer = new ConsoleUpdateHost("/m").RunNewerVersion(
                DetachedStandIn.Exe, [.. DetachedStandIn.Before(written, "exit"), "/m", "kept"], replaces,
                "3.5.0", "3.6.0", () => throw new InvalidOperationException("It did not start."), CancellationToken.None);
        }
        finally
        {
            Environment.CurrentDirectory = before;
        }

        // The stand-in ends without setting the started event, so nothing counts as started.
        Assert.Null(answer);
        Assert.Equal(typedIn.TrimEnd(Path.DirectorySeparatorChar),
            File.ReadAllText(Path.Combine(written, "folder.txt")).Trim().TrimEnd(Path.DirectorySeparatorChar),
            ignoreCase: true);
        var passed = File.ReadAllLines(Path.Combine(written, "arguments.txt"));
        Assert.Equal(ReplaceRead.Asked, HandOverContract.ReadReplaceRequest(passed, out var request));
        Assert.Equal(replaces, request!.ExePath);
        Assert.Equal(Environment.ProcessId, request.ProcessId);
        Assert.Equal("/m", request.Command[0]);
        Assert.Equal("kept", request.Command[1]);
        Assert.StartsWith(CliContract.CancelEventSwitch, request.Command[2], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CliContract.NoUpdateSwitch, request.Command[3]);
        Assert.Equal(4, request.Command.Count);
    }
}
