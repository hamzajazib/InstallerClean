using InstallerClean.Cli;
using InstallerClean.Resources;
using InstallerClean.Services;

namespace InstallerClean.Tests.Services;

/// <summary>
/// The Application-log notice the command line's replace writes for each way it can end
/// (<see cref="CommandLineReplacement"/>): none where this version is in place, the
/// replace's own where nothing was at the path, and otherwise the line for what happened,
/// every member of both outcomes answered.
/// </summary>
public sealed class CommandLineReplacementTests
{
    /// <summary>The words of <paramref name="line"/> up to its first placeholder.</summary>
    private static string Opening(string line) => line[..line.IndexOf('{')];

    [Theory]
    [InlineData(nameof(ReplaceOutcome.NotHandedOver))]
    [InlineData(nameof(ReplaceOutcome.OldCopyNotSeenToEnd))]
    [InlineData(nameof(ReplaceOutcome.Refused))]
    public void A_replace_that_left_the_copy_as_it_was_says_so(string name)
    {
        var outcome = Enum.Parse<ReplaceOutcome>(name);
        Assert.Equal(
            MachineContract.English(() => string.Format(Strings.Cli_EventLogUpdateNotInstalled, SetupReplacement.ThisVersion())),
            MachineContract.English(CommandLineReplacement.NoticeFor(outcome)!));
    }

    [Theory]
    [InlineData(nameof(ReplaceOutcome.Failed))]
    [InlineData(nameof(ReplaceOutcome.RolledBack))]
    [InlineData(nameof(ReplaceOutcome.NewerLeftRunning))]
    [InlineData(nameof(ReplaceOutcome.NotRolledBack))]
    public void A_replace_that_stopped_after_its_move_began_says_so_and_never_that_the_copy_is_as_it_was(string name)
    {
        var outcome = Enum.Parse<ReplaceOutcome>(name);
        var notice = MachineContract.English(CommandLineReplacement.NoticeFor(outcome)!);

        Assert.StartsWith(MachineContract.English(() => Opening(Strings.Cli_EventLogUpdateStoppedPartWay)), notice);
        Assert.DoesNotContain("left as it was", notice);
    }

    [Theory]
    [InlineData(nameof(ReplaceOutcome.Replaced))]
    [InlineData(nameof(ReplaceOutcome.NothingAtPath))]
    public void A_replace_that_put_this_version_in_place_or_wrote_its_own_notice_writes_none(string name)
    {
        var outcome = Enum.Parse<ReplaceOutcome>(name);
        Assert.Null(CommandLineReplacement.NoticeFor(outcome));
    }

    [Fact]
    public void Every_way_a_replace_can_end_is_answered_by_the_theories_above()
    {
        Assert.Equal(9, Enum.GetValues<ReplaceOutcome>().Length);
        Assert.Equal(8, Enum.GetValues<SetupReplaceOutcome>().Length);
    }

    [Theory]
    [InlineData(nameof(SetupReplaceOutcome.NotHandedOver))]
    [InlineData(nameof(SetupReplaceOutcome.OldCopyNotSeenToEnd))]
    [InlineData(nameof(SetupReplaceOutcome.NotStarted))]
    public void A_setup_that_was_never_run_says_the_copy_was_left_as_it_was(string name)
    {
        var outcome = Enum.Parse<SetupReplaceOutcome>(name);
        Assert.Equal(
            MachineContract.English(() => string.Format(Strings.Cli_EventLogUpdateNotInstalled, SetupReplacement.ThisVersion())),
            MachineContract.English(CommandLineReplacement.NoticeFor(outcome, exitCode: null)!));
    }

    [Fact]
    public void Each_other_way_the_setup_ends_has_its_own_line()
    {
        var version = SetupReplacement.ThisVersion();

        Assert.Null(CommandLineReplacement.NoticeFor(SetupReplaceOutcome.Installed, 0));
        Assert.Equal(MachineContract.English(() => string.Format(Strings.Cli_EventLogUpdateSetupNotThisVersion, version)),
            MachineContract.English(CommandLineReplacement.NoticeFor(SetupReplaceOutcome.NotThisVersion, null)!));
        Assert.Equal(MachineContract.English(() => string.Format(Strings.Cli_EventLogUpdateInUseAfterWait, version)),
            MachineContract.English(CommandLineReplacement.NoticeFor(SetupReplaceOutcome.InUse, null)!));
        Assert.Equal(MachineContract.English(() => string.Format(Strings.Cli_EventLogUpdateSetupExitCode, version, 5)),
            MachineContract.English(CommandLineReplacement.NoticeFor(SetupReplaceOutcome.SetupFailed, 5)!));
        Assert.Equal(MachineContract.English(() => string.Format(Strings.Cli_EventLogUpdateStoppedPartWay, version)),
            MachineContract.English(CommandLineReplacement.NoticeFor(SetupReplaceOutcome.SetupFailed, null)!));
        Assert.Equal(MachineContract.English(() => string.Format(Strings.Cli_EventLogUpdateSetupNotInPlace, version)),
            MachineContract.English(CommandLineReplacement.NoticeFor(SetupReplaceOutcome.NotInPlace, 0)!));
    }
}
