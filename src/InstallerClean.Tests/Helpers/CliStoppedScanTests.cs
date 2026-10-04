using InstallerClean.Cli;
using InstallerClean.Helpers;
using InstallerClean.Models;
using InstallerClean.Resources;
using InstallerClean.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// What a scan that stops on its own leaves the command line doing, for each of the
/// three commands, driven through the real work method with a scan that throws. Every
/// such stop reaches the same catch, so the one for a listing of the Installer folder
/// the scan could not take whole stands for all of them here.
///
/// STDOUT IS READ BACK THROUGH <c>Console.SetOut</c>, WHICH IS PROCESS-GLOBAL. The
/// assembly disables test parallelisation for a reason of its own, in
/// AssemblyInfo.cs, and that is what makes the capture safe here.
/// </summary>
public class CliStoppedScanTests
{
    /// <summary>
    /// Temp is fully qualified on either host and outside both forbidden sets, so the
    /// destination gates pass it and a Move reaches the scan. Nothing is created and
    /// nothing is written.
    /// </summary>
    private static readonly string Destination =
        Path.Combine(Path.GetTempPath(), "installerclean-cli-stopped-scan-test");

    [Theory]
    [InlineData("/s")]
    [InlineData("/d")]
    [InlineData("/m")]
    public async Task A_scan_that_stops_prints_its_account_and_exits_with_the_error_code(string arg)
    {
        var scan = Substitute.For<IFileSystemScanService>();
        scan.ScanAsync(Arg.Any<IProgress<ScanProgressUpdate>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new LocalisedInvalidOperationException(
                Strings.Error_ScanInstallerFolderListFailed, new UnauthorizedAccessException()));
        var delete = Substitute.For<IDeleteFilesService>();
        var move = Substitute.For<IMoveFilesService>();

        var (exitCode, stdout) = await Run(arg, Services(scan, delete, move));

        Assert.Equal(CliExitCode.Error, exitCode);
        Assert.Contains(Strings.Error_ScanInstallerFolderListFailed, stdout);
        // Beside the account, so the absence answers for the stop and not for a run
        // that printed nothing at all.
        Assert.DoesNotContain(Strings.Cli_FoundNoOrphans, stdout);
        Assert.Empty(delete.ReceivedCalls());
        Assert.Empty(move.ReceivedCalls());
    }

    // ---- fixtures ----

    private static async Task<(int ExitCode, string Stdout)> Run(string arg, IServiceProvider services)
    {
        var invocation = arg switch
        {
            "/m" => new CliInvocation(CliCommand.Move, null, Destination),
            "/d" => new CliInvocation(CliCommand.Delete, null, null),
            _ => new CliInvocation(CliCommand.ScanOnly, null, null),
        };
        var original = Console.Out;
        using var buffer = new StringWriter();
        try
        {
            Console.SetOut(buffer);
            var exitCode = await Program.RunWorkAsync(arg, invocation, CancellationToken.None, services);
            return (exitCode, buffer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static IServiceProvider Services(
        IFileSystemScanService scan, IDeleteFilesService delete, IMoveFilesService move)
    {
        var reboot = Substitute.For<IPendingRebootService>();
        reboot.Check().Returns(PendingRebootResult.Clean);

        return new ServiceCollection()
            .AddSingleton(scan)
            .AddSingleton(reboot)
            .AddSingleton(Substitute.For<IRemovableReverifier>())
            .AddSingleton(delete)
            .AddSingleton(move)
            .AddSingleton(Substitute.For<ISettingsService>())
            .AddSingleton(Substitute.For<IFirstRunMark>())
            .BuildServiceProvider();
    }
}
