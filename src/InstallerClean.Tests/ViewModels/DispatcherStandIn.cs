using System.Collections.Concurrent;
using InstallerClean.Models;

namespace InstallerClean.Tests.ViewModels;

/// <summary>
/// Stands in for the window's dispatcher. Runs each callback posted to it one at a time, in
/// the order posted, on the thread pool with itself current, so a reporter a view model makes
/// inside one captures it, as a reporter made on the window's thread captures the dispatcher,
/// and an await inside one comes back to it. One callback's writes, and the change
/// notifications they raise, are over before the next callback starts. Keeps, in order, every
/// scan update posted to it, and every exception a posted callback throws.
/// </summary>
internal sealed class DispatcherStandIn : SynchronizationContext
{
    private readonly object _gate = new();
    private Task _last = Task.CompletedTask;

    public ConcurrentQueue<ScanProgressUpdate> Posted { get; } = new();

    public ConcurrentQueue<Exception> Thrown { get; } = new();

    public override void Post(SendOrPostCallback d, object? state)
    {
        if (state is ScanProgressUpdate update) Posted.Enqueue(update);
        lock (_gate)
            _last = _last.ContinueWith(_ => Run(d, state), TaskScheduler.Default);
    }

    /// <summary>
    /// Runs <paramref name="action"/> here once everything posted before it has run, as the
    /// window runs a click, and completes when it has run, faulted with anything it threw.
    /// </summary>
    public Task RunAsync(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(_ =>
        {
            try
            {
                action();
                done.SetResult();
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        }, null);
        return done.Task;
    }

    /// <summary>Completes once every callback posted before it has run.</summary>
    public Task IdleAsync() => RunAsync(() => { });

    private void Run(SendOrPostCallback d, object? state)
    {
        var before = Current;
        SetSynchronizationContext(this);
        try
        {
            d(state);
        }
        catch (Exception ex)
        {
            Thrown.Enqueue(ex);
        }
        finally
        {
            SetSynchronizationContext(before);
        }
    }
}
