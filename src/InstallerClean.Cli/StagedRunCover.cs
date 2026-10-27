using System.Runtime.InteropServices;
using InstallerClean.Helpers;

namespace InstallerClean.Cli;

/// <summary>
/// Whether a Move or Delete may act on a file. <see cref="Covers"/> is asked as the last thing
/// before each file is moved or deleted, inside the Windows Installer mutex, and never waits.
/// <see cref="Lost"/> stops the scan and the re-verify where the run is no longer covered.
/// </summary>
internal interface IRunCover
{
    /// <summary>Whether the older copy's hold or the run's own covers the run now, answered at once.</summary>
    bool Covers();

    /// <summary>Cancelled where the run is no longer covered.</summary>
    CancellationToken Lost { get; }
}

/// <summary>
/// The single-instance mutex's cover for a Move or Delete run standing for an older copy,
/// which holds the mutex until this run has ended (HandOverContract.ReadReplaceRequest). While
/// that copy's process is alive the run is covered by its hold. One thread of its own waits
/// for the mutex all the while, so where that copy ends first Windows hands the mutex to it,
/// and the run is covered by its own hold from then on. Where that copy has ended and the
/// thread does not hold the mutex, the run is not covered, and where the thread has found the
/// mutex taken by another process, or its wait failed, <see cref="Lost"/> is cancelled. The
/// thread's wait is the run's first try for the mutex, so no other process can take it
/// between a failed try and the wait.
/// </summary>
/// <remarks>
/// The mutex is taken and released on the cover's own thread, which a mutex requires, the
/// run itself acting from whatever thread its awaits resume on. Dispose ends that thread,
/// releasing the mutex where it holds it, so the run's mutex is free again before anything
/// that waits for it, such as the setup a replace runs.
/// </remarks>
internal sealed class StagedRunCover : IRunCover, IDisposable
{
    private readonly Mutex _mutex;
    private readonly SafeHandle _olderCopy;
    private readonly ManualResetEvent _stop = new(false);
    private readonly CancellationTokenSource _lost = new();
    private readonly Thread _thread;
    private volatile bool _held;

    /// <summary>
    /// Starts waiting for the mutex <paramref name="mutexName"/> names, and watches the older
    /// copy's process through <paramref name="olderCopy"/>, a handle the caller keeps open
    /// until after this is disposed.
    /// </summary>
    internal StagedRunCover(string mutexName, SafeHandle olderCopy)
    {
        _olderCopy = olderCopy;
        _mutex = new Mutex(initiallyOwned: false, mutexName);
        _thread = new Thread(Hold) { IsBackground = true, Name = "InstallerClean mutex cover" };
        try
        {
            _thread.Start();
        }
        catch
        {
            _mutex.Dispose();
            _stop.Dispose();
            _lost.Dispose();
            throw;
        }
    }

    /// <summary>Whether the cover's own thread holds the mutex.</summary>
    internal bool Holds => _held;

    public CancellationToken Lost => _lost.Token;

    // The older copy is asked first: where it releases the mutex and ends between the two
    // looks, the cover's thread may take the mutex meanwhile, and the later look at the
    // thread's hold then sees it.
    public bool Covers() => OlderCopyRunning() || _held;

    /// <summary>
    /// Whether the older copy's process is still running. A look Windows refuses answers no,
    /// so the run is then covered only by the cover's own hold.
    /// </summary>
    private bool OlderCopyRunning()
    {
        try
        {
            return HandleWait.One(_olderCopy, TimeSpan.Zero) is HandleWaitResult.TimedOut;
        }
        catch (Exception ex)
        {
            CrashLog.TryWrite(ex);
            return false;
        }
    }

    /// <summary>
    /// Cancels <see cref="Lost"/> from the thread pool, so what the cancellation runs inline,
    /// the run's own continuations among it, never runs on the cover's thread. A batch asks
    /// <see cref="Covers"/> before each file on its own thread, so the batch does not wait on
    /// this cancel to stop.
    /// </summary>
    private void MarkLost()
    {
        ThreadPool.UnsafeQueueUserWorkItem(_ =>
        {
            try
            {
                _lost.Cancel();
            }
            catch (Exception ex)
            {
                CrashLog.TryWrite(ex);
            }
        }, null);
    }

    private void Hold()
    {
        try
        {
            // The mutex comes first, so where it and the older copy's end are signalled
            // together the mutex is the one taken.
            var (index, _) = HandleWait.Any(
                [_mutex.SafeWaitHandle, _stop.SafeWaitHandle, _olderCopy], Timeout.InfiniteTimeSpan);
            if (index == 1) return;
            // The older copy has ended. Its hold went with it, so the mutex is free now unless
            // another process took it first.
            if (index == 2 && HandleWait.One(_mutex.SafeWaitHandle, TimeSpan.Zero) is HandleWaitResult.TimedOut)
            {
                MarkLost();
                return;
            }
            _held = true;
            _stop.WaitOne();
        }
        catch (Exception ex)
        {
            // A wait Windows refused leaves the thread without the mutex, so nothing will
            // cover the run once the older copy has ended, and the run stops.
            CrashLog.TryWrite(ex);
            MarkLost();
        }
        finally
        {
            if (_held)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (Exception ex)
                {
                    CrashLog.TryWrite(ex);
                }
                _held = false;
            }
        }
    }

    /// <summary>Ends the cover's thread, releasing the mutex on it where it holds it. Never throws.</summary>
    public void Dispose()
    {
        try
        {
            _stop.Set();
            _thread.Join();
            _mutex.Dispose();
            _stop.Dispose();
            // The source behind Lost is left undisposed: a cancel MarkLost queued may still
            // run after this, and a source with no timer holds nothing to release.
        }
        catch (Exception ex)
        {
            CrashLog.TryWrite(ex);
        }
    }
}
