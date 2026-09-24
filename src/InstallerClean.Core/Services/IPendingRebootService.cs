namespace InstallerClean.Services;

/// <summary>Detects whether the MSI cache is at risk from a Windows Installer operation in flight or queued for next reboot.</summary>
public interface IPendingRebootService
{
    /// <summary>
    /// Probes the four signals and returns a result. Reads only.
    ///
    /// IT THROWS IN ONE CASE AND NO OTHER: a read of Windows Installer's in-progress
    /// file that failed in a way none of <see cref="InstallerInProgressMarkerReading"/>'s
    /// members names. Nothing is established by such a read and none of this gate's
    /// sentences is true of it, so it reaches the caller's own error path instead.
    ///
    /// A REGISTRY READ THAT COULD NOT BE MADE IS NOT A READ THAT CAME BACK CLEAR.
    /// Where either registry read does not answer, whether the cache is at risk is
    /// not established, and this returns a Block naming that rather than the verdict
    /// a quiet machine would have produced.
    ///
    /// The mutex answers on its own terms, stated on <see cref="IMutexProbe.Sample"/>:
    /// a held mutex blocks as <see cref="PendingRebootReason.MsiExecuteMutexHeld"/>, an
    /// existing mutex whose security refuses the probe blocks as
    /// <see cref="PendingRebootReason.MsiExecuteMutexAccessRefused"/>, and a mutex that
    /// is not there and a probe that fails for any other reason count as not held.
    /// </summary>
    PendingRebootResult Check();
}
