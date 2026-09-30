using InstallerClean.Models;

namespace InstallerClean.Helpers;

/// <summary>
/// Passes on the waits a scan reports (<see cref="ScanProgressUpdate.IsWait"/>) and nothing
/// else it reports, for a host that shows the waits alone. An update is tested on the
/// thread that reports it. So in front of a reporter that crosses to the dispatcher, the
/// scan's milestones and its ticker do not cross: the ticker is one update per installed
/// program, and more while the cached files are counted.
/// </summary>
internal sealed class WaitsOnly(IProgress<ScanProgressUpdate> inner) : IProgress<ScanProgressUpdate>
{
    public void Report(ScanProgressUpdate value)
    {
        if (value.IsWait) inner.Report(value);
    }
}
