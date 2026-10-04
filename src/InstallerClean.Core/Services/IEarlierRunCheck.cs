namespace InstallerClean.Services;

/// <summary>
/// Whether this PC has already had its first InstallerClean run, settled once per
/// start of the window. The PC-wide mark (<see cref="IFirstRunMark"/>) answers where
/// it is set or cannot be read. Where it is not set, an earlier run shows in this
/// Windows account's own files (a report already sent, or <c>last-run.json</c>
/// present) or in the command line's record of a Move or Delete that moved or deleted
/// files (<see cref="ICommandLineRunRecord"/>), and finding one sets the mark.
/// </summary>
public interface IEarlierRunCheck
{
    /// <summary>
    /// The answer: true where the PC has already had its first run. The first call
    /// starts the check on a thread-pool thread and every later call returns the same
    /// task, which always completes with an answer and never faults. A log that cannot
    /// be read answers true, and so does anything else that stops the check finishing.
    /// The task completes once the check has finished reading, with the answer for this
    /// start, which is true where a bound passed first in
    /// <see cref="ShowsAnEarlierRunWithinAsync"/>, whatever the check then found.
    /// </summary>
    Task<bool> ShowsAnEarlierRunAsync();

    /// <summary>
    /// The same answer, waited for no longer than <paramref name="bound"/>. Where the
    /// bound passes before the check has answered, the answer is true and the mark is
    /// set, so no later screen on the PC is taken for its first either. That answer
    /// holds for the rest of this start: every later call returns it at once.
    /// </summary>
    Task<bool> ShowsAnEarlierRunWithinAsync(TimeSpan bound);
}
