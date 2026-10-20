namespace InstallerClean.Tests.Helpers;

/// <summary>
/// A fact that runs only where a volume has been mounted into a folder for it, and is
/// reported skipped anywhere else. Mounting one takes a second volume and administrator
/// rights, so the run that sets it up names it through the environment:
/// <see cref="FolderVariable"/> is the folder the volume is mounted at,
/// <see cref="BesideVariable"/> an ordinary folder on the volume holding the installer
/// cache, and <see cref="FreeBytesVariable"/> the mounted volume's free space as Windows
/// reported it before the run, in bytes.
/// </summary>
public sealed class MountedVolumeFactAttribute : FactAttribute
{
    internal const string FolderVariable = "IC_TEST_MOUNTED_VOLUME_FOLDER";
    internal const string BesideVariable = "IC_TEST_MOUNTED_VOLUME_BESIDE";
    internal const string FreeBytesVariable = "IC_TEST_MOUNTED_VOLUME_FREE_BYTES";

    public MountedVolumeFactAttribute()
    {
        if (!OperatingSystem.IsWindows()
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FolderVariable))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(BesideVariable))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FreeBytesVariable)))
            Skip = $"Needs a volume mounted into a folder, named by {FolderVariable}, {BesideVariable} and {FreeBytesVariable}.";
    }
}
