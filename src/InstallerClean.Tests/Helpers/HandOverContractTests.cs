using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The switch a copy updating itself passes the newer version it starts, the event it
/// names, and the first version that takes it, which every later version of the window
/// and the command line is bound to.
/// </summary>
public sealed class HandOverContractTests : IDisposable
{
    private const string EventId = "6f1c2a3b000040008000000000000001";
    private const string OtherEventId = "6f1c2a3b000040008000000000000002";

    private readonly IDisposable _crashLog = TestCrashLog.OwnFolder();

    public void Dispose() => _crashLog.Dispose();

    [Theory]
    [InlineData("3.5.0", true)]
    [InlineData("3.5", true)]
    [InlineData("3.5.0.0", true)]
    [InlineData("3.5.1", true)]
    [InlineData("3.10.0", true)]
    [InlineData("4.0", true)]
    [InlineData("3.4.0", false)]
    [InlineData("3.4.9", false)]
    [InlineData("3.4.0.9", false)]
    [InlineData("", false)]
    [InlineData("x", false)]
    [InlineData("3.5.0-beta", false)]
    public void A_version_takes_the_hand_over_switches_from_3_5_0_on(string version, bool takes)
    {
        Assert.Equal(takes, HandOverContract.TakesHandOverSwitches(version));
    }

    [Theory]
    [InlineData(new[] { "/startedevent:" + EventId, "/d" }, new[] { "/d" }, EventId)]
    [InlineData(new[] { "/m", "D:\\Backup", "/StartedEvent:" + EventId }, new[] { "/m", "D:\\Backup" }, EventId)]
    [InlineData(new[] { "/s", "/startedevent:" + EventId, "/startedevent:" + OtherEventId }, new[] { "/s" }, EventId)]
    [InlineData(new[] { "/s" }, new[] { "/s" }, null)]
    [InlineData(new[] { "/s", "/startedevent:x" }, new[] { "/s", "/startedevent:x" }, null)]
    [InlineData(new[] { "/languagechanged", "/startedevent:" + EventId }, new[] { "/languagechanged" }, EventId)]
    public void TakeStartedEvent_takes_the_switch_out_wherever_it_stands(
        string[] args, string[] rest, string? startedEvent)
    {
        Assert.Equal(rest, HandOverContract.TakeStartedEvent(args, out var value));
        Assert.Equal(startedEvent, value);
    }

    [Fact]
    public void Both_hand_over_switches_come_out_and_leave_the_command_as_given()
    {
        var args = CliContract.TakeCancelEvent(
            ["/cancelevent:" + EventId, "/d", "/startedevent:" + OtherEventId], out var cancelEvent);
        args = HandOverContract.TakeStartedEvent(args, out var startedEvent);

        Assert.Equal(new[] { "/d" }, args);
        Assert.Equal(EventId, cancelEvent);
        Assert.Equal(OtherEventId, startedEvent);
    }

    [Theory]
    [InlineData("/d", "/startedevent:x")]
    [InlineData("/m", "/startedevent:x")]
    [InlineData("/startedevent:x", "/s")]
    public void A_started_event_switch_carrying_no_id_is_refused_as_an_argument_the_command_does_not_know(
        string first, string second)
    {
        var invocation = CliContract.ParseArguments(HandOverContract.TakeStartedEvent([first, second], out _));

        Assert.Equal(CliCommand.UnknownArgument, invocation.Command);
        Assert.Equal("/startedevent:x", invocation.OffendingArgument);
    }

    [Theory]
    [InlineData("3.5.0", true)]
    [InlineData("3.5.1", true)]
    [InlineData("9.9.9", true)]
    [InlineData("3.4.0", false)]
    [InlineData("3.4.9", false)]
    [InlineData("x", false)]
    public void A_command_line_starts_the_newer_version_with_the_no_update_switch_only_where_it_takes_the_switches(
        string toVersion, bool passed)
    {
        var list = HandOverContract.CommandLineArguments(["/s"], toVersion, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(passed ? 1 : 0, list.Count(a => a == CliContract.NoUpdateSwitch));
    }

    [Fact]
    public void The_list_a_command_line_starts_the_newer_version_with_keeps_the_arguments_in_order_and_ends_with_the_events_and_the_switch()
    {
        var cancel = Guid.NewGuid();
        var started = Guid.NewGuid();

        var list = HandOverContract.CommandLineArguments(["/m", @"D:\Backup"], "9.9.9", cancel, started);

        Assert.Equal(
            ["/m", @"D:\Backup", CliContract.CancelEventArgument(cancel), HandOverContract.StartedEventArgument(started),
                CliContract.NoUpdateSwitch],
            list);
    }

    [Theory]
    [InlineData("3.5.0", new[] { "/s", "/noupdate" })]
    [InlineData("3.4.0", new[] { "/s" })]
    public void An_event_given_no_id_is_not_in_the_list(string toVersion, string[] expected)
    {
        Assert.Equal(expected, HandOverContract.CommandLineArguments(["/s"], toVersion, null, null));
    }

    [Theory]
    [InlineData("/s", null)]
    [InlineData("/d", null)]
    [InlineData("/m", null)]
    [InlineData("/m", @"D:\Backup")]
    public void A_newer_version_given_that_list_runs_the_same_command_without_the_update_check(
        string command, string? destination)
    {
        string[] original = destination is null ? [command] : [command, destination];
        var list = HandOverContract.CommandLineArguments(
            original, "9.9.9", Guid.NewGuid(), Guid.NewGuid()).ToArray();

        var rest = CliContract.TakeCancelEvent(list, out var cancelEvent);
        rest = HandOverContract.TakeStartedEvent(rest, out var startedEvent);
        var invocation = CliContract.ParseArguments(rest);
        var asTyped = CliContract.ParseArguments(original);

        Assert.NotNull(cancelEvent);
        Assert.NotNull(startedEvent);
        Assert.True(invocation.SkipUpdate);
        Assert.False(asTyped.SkipUpdate);
        Assert.Equal(asTyped.Command, invocation.Command);
        Assert.Equal(asTyped.MoveDestination, invocation.MoveDestination);
        Assert.Null(invocation.OffendingArgument);
    }

    [Fact]
    public void The_started_event_switch_names_the_event_it_was_written_with()
    {
        var id = Guid.NewGuid();

        HandOverContract.TakeStartedEvent([HandOverContract.StartedEventArgument(id), "/s"], out var taken);

        Assert.Equal(id.ToString("N"), taken);
        Assert.StartsWith(@"Local\", HandOverContract.StartedEventName(id));
        Assert.NotEqual(CliContract.CancelEventName(id), HandOverContract.StartedEventName(id));
    }

    [Fact]
    public void Signalling_sets_the_event_the_switch_names()
    {
        var id = Guid.NewGuid();
        using var started = HandOverContract.CreateStartedEvent(id);
        Assert.NotNull(started);
        Assert.False(started.WaitOne(0));

        HandOverContract.TakeStartedEvent([HandOverContract.StartedEventArgument(id)], out var taken);

        Assert.True(HandOverContract.SignalStarted(taken));
        Assert.True(started.WaitOne(0));
    }

    [Fact]
    public void A_start_given_no_started_event_goes_on_which_is_every_start_that_is_not_an_update()
    {
        Assert.True(HandOverContract.SignalStarted(null));
    }

    [Theory]
    [InlineData("x", "is not an event's id")]
    [InlineData(EventId, "does not exist")]
    public void A_start_whose_started_event_cannot_be_set_does_not_go_on_and_says_why(string id, string why)
    {
        Assert.False(HandOverContract.SignalStarted(id));
        Assert.Equal(1, Occurrences(TestCrashLog.Text(), why));
    }

    [Fact]
    public void An_event_of_that_name_made_elsewhere_is_refused_and_the_crash_log_names_it()
    {
        var id = Guid.NewGuid();
        using var first = HandOverContract.CreateStartedEvent(id);
        Assert.NotNull(first);

        Assert.DoesNotContain(HandOverContract.StartedEventName(id), TestCrashLog.Text());

        Assert.Null(HandOverContract.CreateStartedEvent(id));
        Assert.Contains(HandOverContract.StartedEventName(id), TestCrashLog.Text());
    }

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + 1, StringComparison.Ordinal))
            count++;
        return count;
    }
}
