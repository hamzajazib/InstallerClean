namespace InstallerClean.Models;

/// <summary>
/// A wait the declared-product check is making on the drive or share holding a program's
/// source folder, told to the pass's caller once a read there has waited about a second
/// (<see cref="Services.IDeclaredProductCheck.Screen"/>), and carried to a window on the
/// update naming it (<see cref="ScanProgressUpdate.Wait"/>).
///
/// <see cref="StopWaiting"/> ends the wait at once where it is still in progress. It also
/// gives <see cref="Root"/> up for the rest of the pass the wait belongs to, as the pass
/// does itself with a drive or share whose read does not answer within its time limit: no
/// further read is made there in that pass, and every file whose check still needed a read
/// there is kept. Once a read there sees the stop, the pass lists the root among those it
/// gave up, as stopped (<see cref="SourceRootGiveUpRoute.StoppedWaiting"/>).
///
/// It compares by reference, so an update carrying it equals only an update carrying the
/// same wait. Equality on <see cref="Root"/> would make a wait from a pass that has ended
/// equal to one from the pass running now, which it does not stop.
/// </summary>
public sealed class SourceFolderWait
{
    private readonly Action _stop;

    internal SourceFolderWait(string root, Action stop)
    {
        Root = root;
        _stop = stop;
    }

    /// <summary>
    /// The drive letter and its colon, the share, or the path of another form the read is
    /// under.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Stops waiting for <see cref="Root"/> for the rest of the pass this wait belongs to,
    /// from any thread. A wait there in progress ends at once, and no read there is started
    /// after. Called once this wait has ended, it gives <see cref="Root"/> up for the rest of
    /// the pass all the same; called once the pass has ended, it changes nothing. A second
    /// call is the same as the first.
    /// </summary>
    public void StopWaiting() => _stop();
}
