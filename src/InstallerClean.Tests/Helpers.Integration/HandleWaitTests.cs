using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers.Integration;

/// <summary>
/// <see cref="HandleWait"/> against real kernel objects. Each mutex is a fresh <c>Local\</c> one.
/// </summary>
public class HandleWaitTests
{
    private readonly string _name = $@"Local\InstallerClean_HandleWaitTest_{Guid.NewGuid():N}";

    [Fact]
    public void A_wait_does_not_go_through_a_synchronisation_context_that_asks_to_be_told_of_waits()
    {
        var (handleWaits, waitOneWaits) = WaitRecordingContext.RunOnThreadWithIt(context =>
        {
            using var signalled = new ManualResetEvent(initialState: true);
            Assert.Equal(HandleWaitResult.Signalled, HandleWait.One(signalled.SafeWaitHandle, TimeSpan.FromSeconds(5)));
            var afterHandleWait = context.Waits;

            // The control: a WaitOne on the same thread is handed to the context.
            signalled.WaitOne(TimeSpan.FromSeconds(5));
            return (afterHandleWait, context.Waits - afterHandleWait);
        });

        Assert.Equal(0, handleWaits);
        Assert.Equal(1, waitOneWaits);
    }

    [Fact]
    public void A_wait_on_an_unsignalled_handle_runs_out()
    {
        using var unsignalled = new ManualResetEvent(initialState: false);

        Assert.Equal(HandleWaitResult.TimedOut, HandleWait.One(unsignalled.SafeWaitHandle, TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public void A_mutex_whose_owner_ended_without_releasing_it_answers_abandoned_and_is_owned_by_the_waiter()
    {
        using var mutex = new Mutex(initiallyOwned: false, _name);
        var holder = new Thread(() => mutex.WaitOne(0)) { IsBackground = true };
        holder.Start();
        holder.Join();

        Assert.Equal(HandleWaitResult.Abandoned, HandleWait.One(mutex.SafeWaitHandle, TimeSpan.Zero));
        mutex.ReleaseMutex();
    }

    [Fact]
    public void A_wait_on_a_disposed_mutex_throws_rather_than_waiting()
    {
        var mutex = new Mutex(initiallyOwned: false, _name);
        mutex.Dispose();

        Assert.Throws<ObjectDisposedException>(() => HandleWait.One(mutex.SafeWaitHandle, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void A_wait_on_a_handle_of_minus_one_throws_rather_than_waiting_on_this_process()
    {
        using var minusOne = new Microsoft.Win32.SafeHandles.SafeWaitHandle(new IntPtr(-1), ownsHandle: false);

        Assert.Throws<ObjectDisposedException>(() => HandleWait.One(minusOne, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void An_infinite_wait_is_taken_and_a_wait_below_it_or_past_the_range_is_refused()
    {
        using var signalled = new ManualResetEvent(initialState: true);

        Assert.Equal(HandleWaitResult.Signalled, HandleWait.One(signalled.SafeWaitHandle, Timeout.InfiniteTimeSpan));
        Assert.Throws<ArgumentOutOfRangeException>(() => HandleWait.One(signalled.SafeWaitHandle, TimeSpan.FromMilliseconds(-2)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HandleWait.One(signalled.SafeWaitHandle, TimeSpan.FromMilliseconds(int.MaxValue + 1L)));
    }
}
