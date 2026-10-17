using InstallerClean.Helpers;

namespace InstallerClean.Cli;

/// <summary>
/// The command line's one handler for Ctrl+C, hooked on the first line of
/// <see cref="Program.Main"/> and kept for the whole process. Windows ends a console
/// process that has no handler for Ctrl+C at once, with no summary entry and the
/// console's code page not handed back, so every Ctrl+C is taken here and keeps the
/// process running. The first one cancels <see cref="Pressed"/>, as does the event
/// <see cref="Watch"/> is given, and a run answers it
/// through <see cref="Answer"/>, which calls back at once where the Ctrl+C came first. A
/// run that ends without answering, such as one printing its help, finishes as it would
/// have.
/// </summary>
internal sealed class CancelKey
{
    private readonly CancellationTokenSource _pressed = new();
    private readonly Func<string, WaitHandle?> _openEvent;
    private int _hooked;

    public CancelKey() : this(OpenEvent) { }

    /// <summary>Test seam: how <see cref="Watch"/> opens the event a name names.</summary>
    internal CancelKey(Func<string, WaitHandle?> openEvent) => _openEvent = openEvent;

    /// <summary>The handler the command line hooks.</summary>
    public static CancelKey Process { get; } = new();

    /// <summary>Cancelled by the first Ctrl+C; later ones change nothing.</summary>
    public CancellationToken Pressed => _pressed.Token;

    /// <summary>
    /// Hooks the handler. A second call does nothing, since <see cref="Program.Main"/>
    /// can run more than once in one process. Never throws.
    /// </summary>
    public void Hook()
    {
        if (Interlocked.Exchange(ref _hooked, 1) == 1) return;
        try
        {
            Console.CancelKeyPress += Take;
        }
        catch (Exception ex)
        {
            Helpers.CrashLog.TryWrite(ex);
        }
    }

    /// <summary>
    /// Runs <paramref name="answer"/> once, on the first Ctrl+C, or at once where it has
    /// already come. Disposing what this returns releases it without waiting for a call
    /// already running, so a run can return while its answer is held up on a console
    /// write.
    /// </summary>
    public Registration Answer(Action answer) => new(Pressed.Register(answer));

    /// <summary>A run's answer to Ctrl+C, released on <see cref="Dispose"/>.</summary>
    public readonly struct Registration(CancellationTokenRegistration registration) : IDisposable
    {
        public void Dispose() => registration.Unregister();
    }

    /// <summary>
    /// What Ctrl+C does: keeps the process running and cancels <see cref="Pressed"/>,
    /// running the answer registered on it. It runs on the console's control-handler
    /// thread, where a throw is unhandled, so it never throws.
    /// </summary>
    internal void Take(object? sender, ConsoleCancelEventArgs cancelArgs)
    {
        cancelArgs.Cancel = true;
        Press();
    }

    /// <summary>
    /// Also cancels <see cref="Pressed"/> once the event <paramref name="id"/> stands for
    /// is set. The version that started this one creates it and sets it on Ctrl+C, so a
    /// Ctrl+C the console gave that version before this process was connected to it still
    /// stops this one, and one set before this process opened it is seen as it opens it.
    /// The event is waited on by a thread of its own for the life of the process. An id
    /// naming no event this process can open is written to crash.log, and Ctrl+C alone
    /// stops the run. Never throws.
    /// </summary>
    public void Watch(string id)
    {
        try
        {
            if (!CliContract.TryReadCancelEvent(id, out var name))
                throw new FormatException("The value passed with the cancel-event switch is not an event's id.");
            var watched = _openEvent(name)
                ?? throw new InvalidOperationException("The event passed with the cancel-event switch does not exist.");
            // One set already is answered here, before the run reads Pressed.
            if (watched.WaitOne(0))
            {
                Press();
                return;
            }
            new Thread(() =>
            {
                try
                {
                    if (watched.WaitOne()) Press();
                }
                catch (Exception ex)
                {
                    Helpers.CrashLog.TryWrite(ex);
                }
            }) { IsBackground = true, Name = "Ctrl+C from the starting version" }.Start();
        }
        catch (Exception ex)
        {
            Helpers.CrashLog.TryWrite(ex);
        }
    }

    /// <summary>Cancels <see cref="Pressed"/>. Never throws.</summary>
    private void Press()
    {
        try
        {
            _pressed.Cancel();
        }
        catch (Exception ex)
        {
            Helpers.CrashLog.TryWrite(ex);
        }
    }

    /// <summary>Opens the event <paramref name="name"/> names, or answers null where there is none.</summary>
    private static WaitHandle? OpenEvent(string name) =>
        EventWaitHandle.TryOpenExisting(name, out var opened) ? opened : null;
}
