using InstallerClean.Helpers;
using InstallerClean.Interop.Native;

namespace InstallerClean.Services;

/// <summary>
/// Production <see cref="IWindowsRegion"/>, through <c>GetUserDefaultGeoName</c>. That
/// call returns the home location a person picks under Country or region, which is
/// neither the format locale (<c>RegionInfo.CurrentRegion</c> follows that) nor the
/// display language.
/// </summary>
internal sealed class WindowsRegion : IWindowsRegion
{
    public string? Read()
    {
        try
        {
            // A two-letter code or a number of up to three digits, and the terminator.
            var buffer = new char[16];
            var length = Kernel32.GetUserDefaultGeoName(buffer, buffer.Length);
            if (length <= 1 || length > buffer.Length) return null;
            return new string(buffer, 0, length - 1);
        }
        catch (Exception ex)
        {
            // EntryPointNotFoundException on a Windows older than 1709, which has no
            // such call; anything else is logged the same way.
            CrashLog.TryWrite(ex);
            return null;
        }
    }
}
