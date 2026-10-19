using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The switch a copy updating itself passes the newer version it starts, the event it
/// names, and the first version that takes it, which every later version of the window
/// and the command line is bound to.
/// </summary>
public class HandOverContractTests
{
    private const string EventId = "6f1c2a3b000040008000000000000001";
    private const string OtherEventId = "6f1c2a3b000040008000000000000002";

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
        HandOverContract.SignalStarted(taken);

        Assert.True(started.WaitOne(0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("x")]
    [InlineData(EventId)]
    public void Signalling_an_event_that_does_not_exist_or_no_event_at_all_does_not_throw(string? id)
    {
        HandOverContract.SignalStarted(id);
    }

    [Fact]
    public void An_event_of_that_name_made_elsewhere_is_refused()
    {
        var id = Guid.NewGuid();
        using var first = HandOverContract.CreateStartedEvent(id);
        Assert.NotNull(first);

        Assert.Null(HandOverContract.CreateStartedEvent(id));
    }
}
