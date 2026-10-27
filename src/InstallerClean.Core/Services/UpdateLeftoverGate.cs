namespace InstallerClean.Services;

/// <summary>
/// Keeps the clean-up of what earlier updates left behind from running while an update
/// this process starts is under way. From just before <see cref="AppUpdater.PrepareAsync"/> or
/// <see cref="AppUpdater.PrepareStagedRunAsync"/> writes its first file until nothing it
/// wrote is waiting to be used, the update holds the gate (<see cref="HoldForUpdate"/>) and
/// no pass runs. A run from the staging folder that has started holds it until the process
/// ends.
/// </summary>
public sealed class UpdateLeftoverGate
{
    private readonly object _gate = new();
    private int _holds;
    private TaskCompletionSource? _free;

    /// <summary>The gate this process's clean-up and its updates share.</summary>
    public static UpdateLeftoverGate ForThisProcess { get; } = new();

    /// <summary>
    /// Runs <paramref name="pass"/>, one pass of the clean-up, and answers true, where no
    /// update in this process holds the gate; otherwise runs nothing and answers false.
    /// <see cref="HoldForUpdate"/> blocks the thread that calls it until a pass already
    /// running has ended, so keep the pass short and synchronous.
    /// </summary>
    public bool TryRunPass(Action pass)
    {
        lock (_gate)
        {
            if (_holds > 0) return false;
            pass();
            return true;
        }
    }

    /// <summary>
    /// Completes once no update holds the gate, at once where none does. A hold that is
    /// never released leaves it waiting for the rest of the process.
    /// </summary>
    public Task WhenFree()
    {
        lock (_gate)
        {
            if (_holds == 0) return Task.CompletedTask;
            _free ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _free.Task;
        }
    }

    /// <summary>
    /// Holds the gate for an update about to write its first file, once any pass already
    /// running has ended, blocking the calling thread until then. No pass runs until every
    /// hold taken has been released. An update takes it in AppUpdater's
    /// StagingUnderHold, on the pool thread <see cref="AppUpdater.PrepareAsync"/> or
    /// <see cref="AppUpdater.PrepareStagedRunAsync"/> moves to, never the window's.
    /// </summary>
    internal Hold HoldForUpdate()
    {
        lock (_gate) _holds++;
        return new Hold(this);
    }

    private void Release()
    {
        TaskCompletionSource? free = null;
        lock (_gate)
        {
            if (--_holds == 0)
            {
                free = _free;
                _free = null;
            }
        }
        free?.TrySetResult();
    }

    /// <summary>One update's hold on the gate.</summary>
    internal sealed class Hold
    {
        private UpdateLeftoverGate? _gate;

        internal Hold(UpdateLeftoverGate gate) => _gate = gate;

        /// <summary>
        /// Gives the hold back, so passes run again once no other update holds the gate.
        /// Releases once, however often it is called.
        /// </summary>
        public void Release() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
