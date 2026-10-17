using System.Reflection;
using InstallerClean.Cli;

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
