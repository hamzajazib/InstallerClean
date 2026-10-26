namespace InstallerClean.Tests.Helpers;

/// <summary>
/// A synchronisation context that asks to be told of every wait on its thread, as WPF's
/// dispatcher context does, and counts them. A wait handed to it is one a window's thread
/// could dispatch messages in.
/// </summary>
internal sealed class WaitRecordingContext : SynchronizationContext
{
    public WaitRecordingContext() => SetWaitNotificationRequired();

    public int Waits { get; private set; }

    public override int Wait(IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout)
    {
        Waits++;
        return base.Wait(waitHandles, waitAll, millisecondsTimeout);
    }

    /// <summary>
    /// Runs <paramref name="work"/> on a new thread whose context is a new
    /// <see cref="WaitRecordingContext"/>, and answers what it returned. A throw on that
    /// thread fails the test with it.
    /// </summary>
    public static T RunOnThreadWithIt<T>(Func<WaitRecordingContext, T> work)
    {
        var context = new WaitRecordingContext();
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(context);
            try { result = work(context); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException($"The work on the other thread threw: {failure}");
        return result;
    }
}
