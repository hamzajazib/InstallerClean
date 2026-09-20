namespace InstallerClean.Services;

/// <summary>Detects whether the MSI cache is at risk from a Windows Installer operation in flight or queued for next reboot.</summary>
public interface IPendingRebootService
{
    /// <summary>
    /// Probes the three signals and returns a result. Reads only. Never throws.
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
