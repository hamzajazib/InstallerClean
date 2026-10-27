using System.Globalization;
using InstallerClean.Cli;
using InstallerClean.Services;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The Application-log notices the command line's update writes: 3005 where it installed
/// the newer version, and 3006 where it could not, the newer version would not start, or
/// Ctrl+C came after it could not. They are machine-read and built in English on every
/// machine, so the whole sentence is the contract, asserted under an Italian thread
/// culture to show the build is English.
/// </summary>
public class CliUpdateNoticeTests
{
    private static string BuiltUnderItalian(Func<string> line)
    {
        // Safe to write the thread cultures because the assembly disables test
        // parallelisation.
        var ui = CultureInfo.CurrentUICulture;
        var format = CultureInfo.CurrentCulture;
        var italian = CultureInfo.GetCultureInfo("it-IT");
        try
        {
            CultureInfo.CurrentUICulture = italian;
            CultureInfo.CurrentCulture = italian;
            return line();
        }
        finally
        {
            CultureInfo.CurrentUICulture = ui;
            CultureInfo.CurrentCulture = format;
        }
    }

    [Fact]
    public void The_newer_version_started_names_both_versions_and_what_it_was_started_to_run()
    {
        var line = BuiltUnderItalian(() => MachineContract.English(
            () => Program.UpdatedEventLogLine("/d", "3.4.0", "3.5.0", ["/s"])));

        Assert.Equal("/d mode: InstallerClean 3.4.0 found 3.5.0 and is running /s with it.", line);
    }

    [Fact]
    public void The_newer_version_started_names_a_path_it_was_given_as_given()
    {
        var line = BuiltUnderItalian(() => MachineContract.English(
            () => Program.UpdatedEventLogLine("/m", "3.4.0", "3.5.0", ["/m", @"D:\Backup"])));

        Assert.Equal(@"/m mode: InstallerClean 3.4.0 found 3.5.0 and is running /m D:\Backup with it.", line);
    }

    [Theory]
    [InlineData(UpdateNotMade.DownloadFailed,
        @"/m mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the download failed).")]
    [InlineData(UpdateNotMade.NotRun,
        @"/m mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the newer version could not be run).")]
    [InlineData(UpdateNotMade.InUse,
        @"/m mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (InstallerClean was in use).")]
    [InlineData(UpdateNotMade.CopyCannotTell,
        @"/m mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (it can't tell whether its setup installed it).")]
    [InlineData(UpdateNotMade.FromSource,
        @"/m mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (it was built from source).")]
    [InlineData(UpdateNotMade.NeedsNetFramework48,
        @"/m mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the newer release needs .NET Framework 4.8).")]
    [InlineData(UpdateNotMade.CannotInstallAutomatically,
        @"/m mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the newer release can't be installed automatically).")]
    public void The_update_not_made_says_why(UpdateNotMade notMade, string expected)
    {
        var line = BuiltUnderItalian(() => MachineContract.English(
            () => Program.UpdateFailedEventLogLine("/m", "3.4.0", "3.5.0", notMade)));

        Assert.Equal(expected, line);
    }

    [Theory]
    [InlineData(UpdateNotMade.DownloadFailed,
        "/d mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the download failed), then the run was cancelled, so nothing ran.")]
    [InlineData(UpdateNotMade.NotRun,
        "/d mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the newer version could not be run), then the run was cancelled, so nothing ran.")]
    [InlineData(UpdateNotMade.InUse,
        "/d mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (InstallerClean was in use), then the run was cancelled, so nothing ran.")]
    [InlineData(UpdateNotMade.CopyCannotTell,
        "/d mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (it can't tell whether its setup installed it), then the run was cancelled, so nothing ran.")]
    [InlineData(UpdateNotMade.FromSource,
        "/d mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (it was built from source), then the run was cancelled, so nothing ran.")]
    [InlineData(UpdateNotMade.NeedsNetFramework48,
        "/d mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the newer release needs .NET Framework 4.8), then the run was cancelled, so nothing ran.")]
    [InlineData(UpdateNotMade.CannotInstallAutomatically,
        "/d mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the newer release can't be installed automatically), then the run was cancelled, so nothing ran.")]
    public void The_update_not_made_before_a_Ctrl_C_says_why_and_that_nothing_ran(UpdateNotMade notMade, string expected)
    {
        var line = BuiltUnderItalian(() => MachineContract.English(
            () => Program.UpdateFailedThenCancelledEventLogLine("/d", "3.4.0", "3.5.0", notMade)));

        Assert.Equal(expected, line);
    }
}
