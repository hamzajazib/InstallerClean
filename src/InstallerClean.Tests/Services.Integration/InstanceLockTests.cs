using InstallerClean.Services;
using InstallerClean.Tests.Helpers;

namespace InstallerClean.Tests.Services.Integration;

/// <summary>
/// <see cref="InstanceLock"/> against real named mutexes. Each test names a fresh
/// <c>Local\</c> mutex, never <see cref="InstanceLock.Name"/>, so a copy of InstallerClean
/// running on the machine neither fails a test nor is kept out by one.
/// </summary>
public class InstanceLockTests
{
    private readonly string _name = $@"Local\InstallerClean_InstanceLockTest_{Guid.NewGuid():N}";

    [Fact]
    public void A_free_mutex_is_taken_and_a_second_lock_on_the_name_is_refused_while_it_is_held()
    {
        using var first = new InstanceLock(_name);
        Assert.True(first.TryTake());
        Assert.True(first.IsHeld);

        // A mutex is re-entrant for the thread holding it, so the second lock asks from another.
        var secondTook = OnOtherThread(() =>
        {
            using var second = new InstanceLock(_name);
            return second.TryTake();
        });

        Assert.False(secondTook);
    }

    [Fact]
    public void A_released_mutex_can_be_taken_by_another_lock()
    {
        using var first = new InstanceLock(_name);
        first.TryTake();
        Assert.True(first.Release());
        Assert.False(first.IsHeld);

        var secondTook = OnOtherThread(() =>
        {
            using var second = new InstanceLock(_name);
            var took = second.TryTake();
            if (took) second.Release();
            return took;
        });

        Assert.True(secondTook);
    }

    [Fact]
    public void Release_on_a_lock_that_does_not_hold_the_mutex_answers_false()
    {
        using var lockNotTaken = new InstanceLock(_name);
        Assert.False(lockNotTaken.Release());
    }

    [Fact]
    public void Taking_a_held_lock_again_leaves_one_release_to_free_it()
    {
        using var first = new InstanceLock(_name);
        first.TryTake();
        Assert.True(first.TryTake());
        first.Release();

        var secondTook = OnOtherThread(() =>
        {
            using var second = new InstanceLock(_name);
            var took = second.TryTake();
            if (took) second.Release();
            return took;
        });

        Assert.True(secondTook);
    }

    [Fact]
    public void A_mutex_whose_owner_ended_without_releasing_it_is_taken()
    {
        // The holder's handle stays open past the thread, so the mutex is the abandoned one
        // and not a fresh one made after the last handle closed.
        using var held = new Mutex(initiallyOwned: false, _name);
        var holder = new Thread(() => held.WaitOne(0)) { IsBackground = true };
        holder.Start();
        holder.Join();

        using var instanceLock = new InstanceLock(_name);

        Assert.True(instanceLock.TryTake());
        Assert.True(instanceLock.IsHeld);
        instanceLock.Release();
    }

    [Fact]
    public void A_lock_waiting_for_the_mutex_takes_it_as_its_holder_lets_go()
    {
        using var holding = new ManualResetEventSlim();
        using var letGo = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            using var first = new InstanceLock(_name);
            first.TryTake();
            holding.Set();
            letGo.Wait();
            first.Release();
        }) { IsBackground = true };
        holder.Start();
        Assert.True(holding.Wait(TimeSpan.FromSeconds(10)));

        using var second = new InstanceLock(_name);
        Assert.False(second.TryTake());

        var waited = System.Diagnostics.Stopwatch.StartNew();
        letGo.Set();
        Assert.True(second.TryTake(TimeSpan.FromSeconds(30)));
        Assert.True(waited.Elapsed < TimeSpan.FromSeconds(20));
        Assert.True(second.IsHeld);
        second.Release();
        holder.Join();
    }

    [Fact]
    public void A_wait_for_a_mutex_held_throughout_answers_that_it_is_not_taken()
    {
        using var first = new InstanceLock(_name);
        first.TryTake();

        var waited = System.Diagnostics.Stopwatch.StartNew();
        var secondTook = OnOtherThread(() =>
        {
            using var second = new InstanceLock(_name);
            return second.TryTake(TimeSpan.FromMilliseconds(300));
        });

        Assert.False(secondTook);
        Assert.True(waited.Elapsed >= TimeSpan.FromMilliseconds(250));
        first.Release();
    }

    [Fact]
    public void Taking_the_lock_with_a_wait_hands_no_wait_to_the_threads_synchronisation_context()
    {
        var waits = WaitRecordingContext.RunOnThreadWithIt(context =>
        {
            using var instanceLock = new InstanceLock(_name);
            Assert.True(instanceLock.TryTake(TimeSpan.FromSeconds(5)));
            instanceLock.Release();
            return context.Waits;
        });

        Assert.Equal(0, waits);
    }

    [Fact]
    public void The_lock_names_the_single_instance_mutex()
    {
        Assert.Equal(@"Global\InstallerClean_SingleInstance", InstanceLock.Name);
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
