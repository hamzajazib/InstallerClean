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
    public void The_update_that_went_in_names_both_versions_and_what_the_newer_one_ran()
    {
        var line = BuiltUnderItalian(() => MachineContract.English(
            () => Program.UpdatedEventLogLine("/d", "3.4.0", "3.5.0", ["/s"])));

        Assert.Equal("/d mode: InstallerClean updated itself from 3.4.0 to 3.5.0 and ran /s with it.", line);
    }

    [Fact]
    public void The_update_that_went_in_names_a_path_it_was_given_as_given()
    {
        var line = BuiltUnderItalian(() => MachineContract.English(
            () => Program.UpdatedEventLogLine("/m", "3.4.0", "3.5.0", ["/m", @"D:\Backup"])));

        Assert.Equal(@"/m mode: InstallerClean updated itself from 3.4.0 to 3.5.0 and ran /m D:\Backup with it.", line);
    }

    [Theory]
    [InlineData(UpdateFailureStage.Download,
        @"/m mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the download failed) and ran /m D:\Backup with 3.4.0.")]
    [InlineData(UpdateFailureStage.Install,
        @"/m mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the install failed) and ran /m D:\Backup with 3.4.0.")]
    [InlineData(UpdateFailureStage.InUse,
        @"/m mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (InstallerClean was in use) and ran /m D:\Backup with 3.4.0.")]
    public void The_update_that_failed_says_which_part_failed_and_what_ran(UpdateFailureStage stage, string expected)
    {
        var line = BuiltUnderItalian(() => MachineContract.English(
            () => Program.UpdateFailedEventLogLine("/m", "3.4.0", "3.5.0", stage, ["/m", @"D:\Backup"])));

        Assert.Equal(expected, line);
    }

    [Theory]
    [InlineData(UpdateFailureStage.Download,
        "/d mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the download failed), then the run was cancelled, so nothing ran.")]
    [InlineData(UpdateFailureStage.Install,
        "/d mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the install failed), then the run was cancelled, so nothing ran.")]
    [InlineData(UpdateFailureStage.InUse,
        "/d mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (InstallerClean was in use), then the run was cancelled, so nothing ran.")]
    public void The_update_that_failed_before_a_Ctrl_C_says_which_part_failed_and_that_nothing_ran(
        UpdateFailureStage stage, string expected)
    {
        var line = BuiltUnderItalian(() => MachineContract.English(
            () => Program.UpdateFailedThenCancelledEventLogLine("/d", "3.4.0", "3.5.0", stage)));

        Assert.Equal(expected, line);
    }

    [Fact]
    public void The_update_that_went_in_and_would_not_start_says_nothing_ran()
    {
        var line = BuiltUnderItalian(() => MachineContract.English(
            () => Program.UpdateNotStartedEventLogLine("/s", "3.4.0", "3.5.0")));

        Assert.Equal("/s mode: InstallerClean updated itself from 3.4.0 to 3.5.0 but could not start it, so nothing ran.", line);
    }

    [Fact]
    public void The_update_that_went_in_before_a_Ctrl_C_says_nothing_ran()
    {
        var line = BuiltUnderItalian(() => MachineContract.English(
            () => Program.UpdatedThenCancelledEventLogLine("/s", "3.4.0", "3.5.0")));

        Assert.Equal("/s mode: InstallerClean updated itself from 3.4.0 to 3.5.0, then the run was cancelled, so nothing ran.", line);
    }
}
