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
    private bool _updateWriting;

    /// <summary>The gate this process's clean-up and its updates share.</summary>
    public static UpdateLeftoverGate ForThisProcess { get; } = new();

    /// <summary>
    /// Runs <paramref name="pass"/>, one pass of the clean-up, and answers true, where no
    /// update in this process is about to write or writing; otherwise runs nothing and
    /// answers false, and the caller stops. <see cref="MarkUpdateWriting"/> waits for a
    /// pass already running, on whichever thread the update has reached, so keep the pass
    /// short and synchronous.
    /// </summary>
    public bool TryRunPass(Action pass)
    {
        lock (_gate)
        {
            if (_updateWriting) return false;
            pass();
            return true;
        }
    }

    /// <summary>
    /// Marks that an update is about to write its first file, once any pass already
    /// running has ended. No pass runs after this in this process.
    /// </summary>
    internal void MarkUpdateWriting()
    {
        lock (_gate) _updateWriting = true;
    }
}
