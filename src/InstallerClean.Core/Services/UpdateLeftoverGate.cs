namespace InstallerClean.Services;

/// <summary>
/// Keeps the clean-up of what earlier updates left behind apart from an update this
/// process starts. The clean-up deletes the staging folders' downloads and the newer
/// version beside the exe by their names, and an update writes files under the same
/// names while it runs, opened with delete sharing, so a pass of the clean-up while it
/// runs would delete them. Once <see cref="AppUpdater.PrepareAsync"/> is about to write
/// its first file, no pass runs again in this process, and whatever was left behind waits
/// for the next start.
/// </summary>
public sealed class UpdateLeftoverGate
{
    private readonly object _gate = new();
    private bool _updateStarted;

    /// <summary>The gate this process's clean-up and its updates share.</summary>
    public static UpdateLeftoverGate ForThisProcess { get; } = new();

    /// <summary>Whether an update has begun writing in this process.</summary>
    internal bool UpdateStarted
    {
        get { lock (_gate) return _updateStarted; }
    }

    /// <summary>
    /// Runs <paramref name="pass"/>, one pass of the clean-up, and answers true, where no
    /// update has begun; otherwise runs nothing and answers false, and the caller stops.
    /// An update cannot begin while a pass runs, so keep the pass short and synchronous:
    /// the update waits on the thread it starts from, the window's own among them.
    /// </summary>
    public bool TryRunPass(Action pass)
    {
        lock (_gate)
        {
            if (_updateStarted) return false;
            pass();
            return true;
        }
    }

    /// <summary>
    /// Marks that an update has begun, once any pass already running has ended. Every
    /// pass after this one does nothing.
    /// </summary>
    internal void MarkUpdateStarted()
    {
        lock (_gate) _updateStarted = true;
    }
}
