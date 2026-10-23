namespace InstallerClean.Services;

/// <summary>
/// The single-instance mutex the window, a command-line Delete or Move and a command-line
/// update all take, so no two of them run at once. The setup names it too, as AppMutex,
/// and stops while the name can be opened, which it can while any process has it open,
/// held or not.
/// </summary>
/// <remarks>
/// A mutex is owned by the thread that took it and only that thread can release it, so
/// each caller takes and releases it on one thread. Opening it throws where Windows refuses
/// the name, and the throw reaches the caller.
/// </remarks>
internal sealed class InstanceLock : IDisposable
{
    /// <summary>
    /// The mutex's name. Older and newer copies and the installed setup all exclude each
    /// other through this one name, so changing it lets an older copy and a newer one run
    /// at once. installer/InstallerClean.iss spells it in AppMutexName.
    /// </summary>
    public const string Name = @"Global\InstallerClean_SingleInstance";

    private readonly Mutex _mutex;

    /// <summary>Opens the mutex <see cref="Name"/> names, without taking it.</summary>
    public InstanceLock() : this(Name) { }

    /// <summary>Opens the mutex <paramref name="name"/> names, without taking it.</summary>
    internal InstanceLock(string name) => _mutex = new Mutex(initiallyOwned: false, name);

    /// <summary>Whether this lock holds the mutex.</summary>
    public bool IsHeld { get; private set; }

    /// <summary>
    /// Takes the mutex without waiting where nothing else holds it, and answers whether this
    /// lock holds it. A mutex whose last owner ended without releasing it is taken.
    /// </summary>
    public bool TryTake()
    {
        if (IsHeld) return true;
        try
        {
            IsHeld = _mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // Its last owner ended without releasing it; the runtime hands it to this thread.
            IsHeld = true;
        }
        return IsHeld;
    }

    /// <summary>Releases the mutex where this lock holds it, and answers whether it did.</summary>
    public bool Release()
    {
        if (!IsHeld) return false;
        _mutex.ReleaseMutex();
        IsHeld = false;
        return true;
    }

    /// <summary>
    /// Closes the handle. Closing it does not release a mutex this lock holds: that stays
    /// owned by the thread that took it until the thread ends, so release it first.
    /// </summary>
    public void Dispose() => _mutex.Dispose();
}
