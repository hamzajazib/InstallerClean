namespace InstallerClean.Cli;

/// <summary>
/// The command line's one handler for Ctrl+C, hooked on the first line of
/// <see cref="Program.Main"/> and kept for the whole process. Windows ends a console
/// process that has no handler for Ctrl+C at once, with no summary entry and the
/// console's code page not handed back, so every Ctrl+C is taken here and keeps the
/// process running. The first one cancels <see cref="Pressed"/>, and a run answers it
/// through <see cref="Answer"/>, which calls back at once where the Ctrl+C came first. A
/// run that ends without answering, such as one printing its help, finishes as it would
/// have.
/// </summary>
internal sealed class CancelKey
{
    private readonly CancellationTokenSource _pressed = new();
    private int _hooked;

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
        try
        {
            _pressed.Cancel();
        }
        catch (Exception ex)
        {
            Helpers.CrashLog.TryWrite(ex);
        }
    }
}
