using InstallerClean.Models;

namespace InstallerClean.Helpers;

/// <summary>
/// Shows the waits a scan reports (<see cref="ScanProgressUpdate.IsWait"/>) on one line of
/// text, and puts back what the line said when each wait ends. The scan's overlay, the
/// splash and the heading over a Move or Delete each keep one for the line a screen reader
/// speaks there.
/// </summary>
internal sealed class WaitLine(Func<string> read, Action<string> write)
{
    /// <summary>What the line said when a wait took it, or null while no wait is shown.</summary>
    private string? _before;

    /// <summary>
    /// Shows the line <paramref name="update"/> carries, or, for the empty update that ends
    /// a wait, puts back what the line said before the wait. An end with no wait shown
    /// changes nothing.
    /// </summary>
    internal void Show(ScanProgressUpdate update)
    {
        if (update.Message.Length > 0)
        {
            _before ??= read();
            write(update.Message);
        }
        else if (_before is { } before)
        {
            write(before);
            _before = null;
        }
    }

    /// <summary>
    /// Forgets a wait being shown, for a host that has put something else on the line, which
    /// the end of that wait must not overwrite.
    /// </summary>
    internal void Forget() => _before = null;
}
