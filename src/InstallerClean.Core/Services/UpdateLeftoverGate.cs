namespace InstallerClean.Services;

/// <summary>
/// Keeps the clean-up of what earlier updates left behind apart from an update this
/// process starts. The clean-up deletes the staging folders' downloads and the newer
/// version beside the exe by their names, and an update writes files under the same
/// names while it runs, opened with delete sharing, so a pass of the clean-up while it
/// runs would delete them. From just before <see cref="AppUpdater.PrepareAsync"/> writes
/// its first file until nothing it wrote is waiting to be used, the update holds the gate
/// (<see cref="HoldForUpdate"/>) and no pass runs.
/// </summary>
public sealed class UpdateLeftoverGate
{
    private readonly object _gate = new();
    private int _holds;

    /// <summary>The gate this process's clean-up and its updates share.</summary>
    public static UpdateLeftoverGate ForThisProcess { get; } = new();

    /// <summary>
    /// Runs <paramref name="pass"/>, one pass of the clean-up, and answers true, where no
    /// update in this process holds the gate; otherwise runs nothing and answers false.
    /// <see cref="HoldForUpdate"/> waits for a pass already running, on whichever thread
    /// the update has reached, the window's own among them, so keep the pass short and
    /// synchronous.
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
    /// Holds the gate for an update about to write its first file, once any pass already
    /// running has ended. No pass runs until every hold taken has been released.
    /// </summary>
    internal Hold HoldForUpdate()
    {
        lock (_gate) _holds++;
        return new Hold(this);
    }

    private void Release()
    {
        lock (_gate) _holds--;
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
