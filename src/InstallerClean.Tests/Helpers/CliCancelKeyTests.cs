using System.Reflection;
using InstallerClean.Cli;
using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The command line's one Ctrl+C handler: it keeps the process running, and a run
/// answers the first Ctrl+C once, whether it came before the run registered for it or
/// after. Each test makes its own <see cref="CancelKey"/>, since the process's own is
/// never reset.
/// </summary>
public class CliCancelKeyTests
{
    /// <summary>
    /// The arguments the console passes the handler. Their constructor is not public, so
    /// it is reached the way the runtime reaches it.
    /// </summary>
    private static ConsoleCancelEventArgs CtrlC() =>
        (ConsoleCancelEventArgs)Activator.CreateInstance(
            typeof(ConsoleCancelEventArgs), BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null, args: [ConsoleSpecialKey.ControlC], culture: null)!;

    [Fact]
    public void A_Ctrl_C_keeps_the_process_running()
    {
        var ctrlC = CtrlC();

        new CancelKey().Take(null, ctrlC);

        Assert.True(ctrlC.Cancel);
    }

    [Fact]
    public void A_Ctrl_C_before_the_run_answers_is_answered_as_it_does()
    {
        var key = new CancelKey();
        var answered = 0;

        key.Take(null, CtrlC());
        using var answer = key.Answer(() => answered++);

        Assert.Equal(1, answered);
    }

    [Fact]
    public void The_run_answers_the_first_Ctrl_C_and_no_other()
    {
        var key = new CancelKey();
        var answered = 0;
        using var answer = key.Answer(() => answered++);

        key.Take(null, CtrlC());
        key.Take(null, CtrlC());

        Assert.Equal(1, answered);
    }

    [Fact]
    public void The_event_the_starting_version_sets_is_answered_as_a_Ctrl_C()
    {
        var id = Guid.NewGuid();
        using var signal = new EventWaitHandle(false, EventResetMode.ManualReset);
        var key = new CancelKey(name => name == CliContract.CancelEventName(id) ? signal : null);
        using var answered = new ManualResetEventSlim();
        using var answer = key.Answer(answered.Set);

        key.Watch(id.ToString("N"));
        Assert.False(key.Pressed.IsCancellationRequested);
        signal.Set();

        Assert.True(answered.Wait(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void The_event_set_before_this_version_watches_it_is_answered_as_a_Ctrl_C()
    {
        var id = Guid.NewGuid();
        using var signal = new EventWaitHandle(true, EventResetMode.ManualReset);
        var key = new CancelKey(name => name == CliContract.CancelEventName(id) ? signal : null);
        using var answered = new ManualResetEventSlim();

        key.Watch(id.ToString("N"));
        // Answered before the run reads Pressed, which it does straight after.
        Assert.True(key.Pressed.IsCancellationRequested);
        using var answer = key.Answer(answered.Set);

        Assert.True(answered.IsSet);
    }

    [Fact]
    public void The_event_is_opened_by_its_name_in_the_session()
    {
        // Named events are a Windows object, so this test needs Windows.
        var id = Guid.NewGuid();
        using var signal = new EventWaitHandle(false, EventResetMode.ManualReset, CliContract.CancelEventName(id));
        var key = new CancelKey();
        using var answered = new ManualResetEventSlim();
        using var answer = key.Answer(answered.Set);

        key.Watch(id.ToString("N"));
        signal.Set();

        Assert.True(answered.Wait(TimeSpan.FromSeconds(30)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("1234")]
    [InlineData("{6f1c2a3b-0000-4000-8000-000000000000}")]
    public void A_value_that_is_no_events_id_leaves_Ctrl_C_to_stop_the_run(string value)
    {
        var opened = 0;
        var key = new CancelKey(_ =>
        {
            opened++;
            return null;
        });
        var answered = 0;
        using var answer = key.Answer(() => answered++);

        key.Watch(value);
        Assert.Equal(0, opened);
        Assert.Equal(0, answered);
        key.Take(null, CtrlC());

        Assert.Equal(1, answered);
    }

    [Fact]
    public void An_id_naming_no_event_leaves_Ctrl_C_to_stop_the_run()
    {
        var key = new CancelKey(_ => null);
        var answered = 0;
        using var answer = key.Answer(() => answered++);

        key.Watch(Guid.NewGuid().ToString("N"));
        Assert.Equal(0, answered);
        key.Take(null, CtrlC());

        Assert.Equal(1, answered);
    }

    [Fact]
    public void An_event_the_watch_cannot_open_leaves_Ctrl_C_to_stop_the_run()
    {
        var key = new CancelKey(_ => throw new UnauthorizedAccessException("refused"));
        var answered = 0;
        using var answer = key.Answer(() => answered++);

        key.Watch(Guid.NewGuid().ToString("N"));
        Assert.Equal(0, answered);
        key.Take(null, CtrlC());

        Assert.Equal(1, answered);
    }

    [Fact]
    public void A_Ctrl_C_after_the_run_has_returned_reaches_nothing_and_still_keeps_the_process_running()
    {
        var key = new CancelKey();
        var answered = 0;
        key.Answer(() => answered++).Dispose();
        var ctrlC = CtrlC();

        key.Take(null, ctrlC);

        Assert.Equal(0, answered);
        Assert.True(ctrlC.Cancel);
    }
}
