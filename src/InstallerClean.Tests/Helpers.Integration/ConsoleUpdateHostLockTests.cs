using InstallerClean.Cli;
using InstallerClean.Services;

namespace InstallerClean.Tests.Helpers.Integration;

/// <summary>
/// The single-instance mutex the command line's update holds for a Move or Delete
/// (<see cref="ConsoleUpdateHost.TryTakeInstanceLock"/>), against real named mutexes: released
/// as the update returns, except where the run stopped waiting for a newer version that may
/// still be running (<see cref="ConsoleUpdateHost.LeaveInstanceLockToProcessEnd"/>), which
/// keeps it until the process ends. Each test names a fresh <c>Local\</c> mutex, never
/// <see cref="InstanceLock.Name"/>, so a copy of InstallerClean running on the machine
/// neither fails a test nor is kept out by one.
/// </summary>
public class ConsoleUpdateHostLockTests
{
    private readonly string _name = $@"Local\InstallerClean_HostLockTest_{Guid.NewGuid():N}";

    [Fact]
    public void The_update_releases_the_mutex_it_took()
    {
        var host = new ConsoleUpdateHost("/d", () => new InstanceLock(_name));
        Assert.True(host.TryTakeInstanceLock());

        host.ReleaseInstanceLock();

        Assert.True(AnotherThreadTakes());
    }

    [Fact]
    public void A_run_that_stopped_waiting_for_the_newer_version_keeps_the_mutex_past_the_release()
    {
        // Taken on a thread of its own, as the run's Main thread takes it, which ends holding
        // it: the mutex is then the abandoned one, as at the run's process end.
        var host = new ConsoleUpdateHost("/d", () => new InstanceLock(_name));
        var kept = false;
        var thread = new Thread(() =>
        {
            host.TryTakeInstanceLock();
            host.LeaveInstanceLockToProcessEnd();
            host.ReleaseInstanceLock();
            kept = !AnotherThreadTakes();
        }) { IsBackground = true };
        thread.Start();
        thread.Join();

        Assert.True(kept);
        Assert.True(AnotherThreadTakes());
    }

    private bool AnotherThreadTakes()
    {
        var took = false;
        var thread = new Thread(() =>
        {
            using var other = new InstanceLock(_name);
            took = other.TryTake();
            if (took) other.Release();
        }) { IsBackground = true };
        thread.Start();
        thread.Join();
        return took;
    }
}
