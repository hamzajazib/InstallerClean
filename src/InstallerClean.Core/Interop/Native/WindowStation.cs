using System.Runtime.CompilerServices;

namespace InstallerClean.Interop.Native;

/// <summary>
/// Whether the process runs in a window station that shows windows to a user, which is what
/// <see cref="Environment.UserInteractive"/> reports. The framework reads it through its own
/// user32.dll import, which the runtime finds by its own search, so the command line asks
/// through the import in <see cref="User32"/>, which is loaded from System32. The test is the
/// framework's: the station is non-interactive only where it can be read and is not visible.
/// </summary>
internal static class WindowStation
{
    private static readonly Lazy<bool> s_interactive = new(
        () => !OperatingSystem.IsWindows() || IsInteractive(User32.GetProcessWindowStation, ReadFlags));

    /// <summary>
    /// False where the process's window station can be read and is not visible, such as the
    /// service window station. True otherwise, including where the station cannot be read.
    /// The answer is read once, the process's window station being fixed for its life.
    /// </summary>
    public static bool IsInteractive() => s_interactive.Value;

    /// <summary>
    /// The test over <paramref name="getStation"/> and <paramref name="readFlags"/>, which
    /// answers the station's flags or null where they cannot be read.
    /// </summary>
    internal static bool IsInteractive(Func<IntPtr> getStation, Func<IntPtr, int?> readFlags)
    {
        var station = getStation();
        if (station == IntPtr.Zero) return true;
        return readFlags(station) is not { } flags || (flags & User32.WSF_VISIBLE) != 0;
    }

    private static int? ReadFlags(IntPtr station)
    {
        var flags = default(User32.USEROBJECTFLAGS);
        var size = (uint)Unsafe.SizeOf<User32.USEROBJECTFLAGS>();
        return User32.GetUserObjectInformation(station, User32.UOI_FLAGS, ref flags, size, out _)
            ? flags.dwFlags
            : null;
    }
}
