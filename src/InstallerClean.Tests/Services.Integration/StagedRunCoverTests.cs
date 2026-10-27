using InstallerClean.Cli;
using InstallerClean.Services;

namespace InstallerClean.Tests.Services.Integration;

/// <summary>
/// <see cref="StagedRunCover"/> against real named mutexes, with an event standing for the
/// older copy's process: unsignalled while it runs, signalled once it has ended. Each test
/// names a fresh <c>Local\</c> mutex, never <see cref="InstanceLock.Name"/>, so a copy of
/// InstallerClean running on the machine neither fails a test nor is kept out by one.
/// </summary>
public class StagedRunCoverTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);
    private readonly string _name = $@"Local\InstallerClean_CoverTest_{Guid.NewGuid():N}";

    [Fact]
    public void A_run_is_covered_while_the_older_copy_runs_holding_the_mutex()
    {
        using var olderCopy = new ManualResetEvent(false);
        using var holder = new Holder(_name);

        using var cover = new StagedRunCover(_name, olderCopy.SafeWaitHandle);

        Assert.True(cover.Covers());
        Assert.False(cover.Holds);
        Assert.False(cover.Lost.IsCancellationRequested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_cover_takes_the_mutex_as_the_older_copy_lets_go_of_it_or_ends_holding_it(bool abandoned)
    {
        using var olderCopy = new ManualResetEvent(false);
        var holder = new Holder(_name);
        using var cover = new StagedRunCover(_name, olderCopy.SafeWaitHandle);

        holder.End(release: !abandoned);
        olderCopy.Set();

        Assert.True(SpinWait.SpinUntil(() => cover.Holds, Bound));
        Assert.True(cover.Covers());
        Assert.False(cover.Lost.IsCancellationRequested);
    }

    [Fact]
    public void A_run_whose_older_copy_ended_with_the_mutex_held_elsewhere_is_not_covered()
    {
        using var olderCopy = new ManualResetEvent(false);
        using var elsewhere = new Holder(_name);
        using var cover = new StagedRunCover(_name, olderCopy.SafeWaitHandle);

        olderCopy.Set();

        // Not covered from the moment the older copy has ended, before the cover's thread has
        // found the mutex taken as well as after.
        Assert.False(cover.Covers());
        Assert.True(SpinWait.SpinUntil(() => cover.Lost.IsCancellationRequested, Bound));
        Assert.False(cover.Covers());
        Assert.False(cover.Holds);
    }

    [Fact]
    public void A_cover_whose_wait_fails_does_not_cover_the_run()
    {
        var olderCopy = new ManualResetEvent(false);
        var handle = olderCopy.SafeWaitHandle;
        olderCopy.Dispose();
        using var cover = new StagedRunCover(_name, handle);

        Assert.False(cover.Covers());
        Assert.True(SpinWait.SpinUntil(() => cover.Lost.IsCancellationRequested, Bound));
        Assert.False(cover.Holds);
    }

    [Fact]
    public void Dispose_releases_the_mutex_the_cover_holds()
    {
        using var olderCopy = new ManualResetEvent(true);
        var cover = new StagedRunCover(_name, olderCopy.SafeWaitHandle);
        Assert.True(SpinWait.SpinUntil(() => cover.Holds, Bound));

        cover.Dispose();

        Assert.True(OnOtherThread(() =>
        {
            using var next = new InstanceLock(_name);
            var took = next.TryTake();
            if (took) next.Release();
            return took;
        }));
    }

    [Fact]
    public async Task Dispose_ends_a_cover_still_waiting_for_the_mutex()
    {
        using var olderCopy = new ManualResetEvent(false);
        using var holder = new Holder(_name);
        var cover = new StagedRunCover(_name, olderCopy.SafeWaitHandle);

        var disposed = Task.Run(cover.Dispose);

        Assert.Same(disposed, await Task.WhenAny(disposed, Task.Delay(Bound)));
        Assert.False(cover.Holds);
    }

    /// <summary>A thread of its own holding the mutex until it is told to end.</summary>
    private sealed class Holder : IDisposable
    {
        private readonly ManualResetEventSlim _end = new();
        private readonly Thread _thread;
        private bool _release = true;

        internal Holder(string name)
        {
            using var holding = new ManualResetEventSlim();
            _thread = new Thread(() =>
            {
                var mutex = new Mutex(initiallyOwned: false, name);
                mutex.WaitOne();
                holding.Set();
                _end.Wait();
                if (_release) mutex.ReleaseMutex();
                // Not disposed where it is left abandoned, so the mutex lives on past the
                // thread and the cover finds the abandoned one, not a fresh one.
                if (_release) mutex.Dispose();
                else GC.KeepAlive(s_abandoned = mutex);
            }) { IsBackground = true };
            _thread.Start();
            Assert.True(holding.Wait(Bound));
        }

        private static Mutex? s_abandoned;

        internal void End(bool release)
        {
            _release = release;
            _end.Set();
            _thread.Join();
        }

        public void Dispose()
        {
            if (_thread.IsAlive) End(release: true);
        }
    }

    private static bool OnOtherThread(Func<bool> work)
    {
        var result = false;
        var thread = new Thread(() => result = work()) { IsBackground = true };
        thread.Start();
        thread.Join();
        return result;
    }
}
