using InstallerClean.Helpers;
using InstallerClean.Models;
using InstallerClean.Services;
using InstallerClean.Tests.Helpers;
using InstallerClean.ViewModels;
using Xunit.Abstractions;

namespace InstallerClean.Tests.Helpers.Integration;

/// <summary>
/// The Move's volume questions put to a real volume mounted into a folder on the
/// drive that holds the installer cache.
/// </summary>
/// <remarks>
/// The run that mounts the volume names three things through the environment (see
/// <see cref="MountedVolumeFactAttribute"/>): the mount folder, an ordinary folder
/// beside it on the cache's volume, and the mounted volume's free space as Windows
/// reported it. The expected answers come from those, never from the calls under
/// test. Where a figure is compared, the mounted volume's is the one asserted
/// exactly, because nothing writes to it during the run; the cache's volume is
/// compared only against it, since the build writes to that drive while the tests run.
///
/// Every destination asked about under the mount folder is one the run did not
/// create, except <see cref="ExistingUnderMount"/>, which it did. Nothing here
/// creates anything, so the mounted volume's free space stays what was reported.
///
/// Each fact writes the call, the expected answer and the answer it got, so the
/// run's record says what was asked as well as whether it agreed.
/// </remarks>
public class MoveSpaceCheckMountPointTests(ITestOutputHelper output)
{
    private static string MountFolder =>
        Path.TrimEndingDirectorySeparator(
            Environment.GetEnvironmentVariable(MountedVolumeFactAttribute.FolderVariable)!);

    private static string BesideFolder =>
        Path.TrimEndingDirectorySeparator(
            Environment.GetEnvironmentVariable(MountedVolumeFactAttribute.BesideVariable)!);

    private static long MountedFreeBytes =>
        long.Parse(Environment.GetEnvironmentVariable(MountedVolumeFactAttribute.FreeBytesVariable)!);

    /// <summary>
    /// GetVolumePathName's spelling of the mount folder: the folder with a trailing
    /// backslash. Compared ignoring case, as the volume comparison itself does.
    /// </summary>
    private static string MountVolume => MountFolder + Path.DirectorySeparatorChar;

    private static string UnderMount => Path.Combine(MountFolder, "backup");
    private static string DeepUnderMount => Path.Combine(MountFolder, "new", "deeper");
    private static string ExistingUnderMount => Path.Combine(MountFolder, "exists");
    private static string BesideDestination => Path.Combine(BesideFolder, "backup");

    private void Row(int row, string call, object? expected, object? got) =>
        output.WriteLine($"ROW {row} | {call} | expected {expected ?? "null"} | got {got ?? "null"}");

    [MountedVolumeFact]
    public void Row01_the_volume_of_a_missing_folder_under_the_mount_is_the_mount_folder()
    {
        var got = StorageHelpers.GetVolumeMountPoint(UnderMount);
        Row(1, $"GetVolumeMountPoint({UnderMount})", MountVolume, got);
        Assert.Equal(MountVolume, got, ignoreCase: true);
    }

    [MountedVolumeFact]
    public void Row02_the_volume_of_the_mount_folder_is_itself()
    {
        var got = StorageHelpers.GetVolumeMountPoint(MountFolder);
        Row(2, $"GetVolumeMountPoint({MountFolder})", MountVolume, got);
        Assert.Equal(MountVolume, got, ignoreCase: true);
    }

    [MountedVolumeFact]
    public void Row03_the_volume_of_the_folder_beside_the_mount_is_its_drive()
    {
        var expected = Path.GetPathRoot(BesideFolder);
        var got = StorageHelpers.GetVolumeMountPoint(BesideFolder);
        Row(3, $"GetVolumeMountPoint({BesideFolder})", expected, got);
        Assert.Equal(expected, got, ignoreCase: true);
    }

    [MountedVolumeFact]
    public void Row04_the_volume_of_the_installer_cache_is_the_drive_beside_the_mount()
    {
        // The beside folder is on the cache's drive by the run's construction, and
        // nothing is mounted at the cache folder.
        var expected = Path.GetPathRoot(BesideFolder);
        var got = StorageHelpers.GetVolumeMountPoint(InstallerCacheHelpers.InstallerFolder);
        Row(4, $"GetVolumeMountPoint({InstallerCacheHelpers.InstallerFolder})", expected, got);
        Assert.Equal(expected, got, ignoreCase: true);
    }

    [MountedVolumeFact]
    public void Row05_a_destination_under_the_mount_is_not_on_the_cache_volume()
    {
        var got = MoveSpaceCheck.ResolveIsOnInstallerCacheDrive(UnderMount);
        Row(5, $"ResolveIsOnInstallerCacheDrive({UnderMount})", false, got);
        Assert.False(got is null, "Windows did not settle the question.");
        Assert.False(got);
    }

    [MountedVolumeFact]
    public void Row06_the_mount_folder_is_not_on_the_cache_volume()
    {
        var got = MoveSpaceCheck.ResolveIsOnInstallerCacheDrive(MountFolder);
        Row(6, $"ResolveIsOnInstallerCacheDrive({MountFolder})", false, got);
        Assert.False(got is null, "Windows did not settle the question.");
        Assert.False(got);
    }

    [MountedVolumeFact]
    public void Row07_a_destination_beside_the_mount_is_on_the_cache_volume()
    {
        var got = MoveSpaceCheck.ResolveIsOnInstallerCacheDrive(BesideDestination);
        Row(7, $"ResolveIsOnInstallerCacheDrive({BesideDestination})", true, got);
        Assert.True(got);
    }

    [MountedVolumeFact]
    public void Row08_free_space_for_a_missing_folder_under_the_mount_is_the_mounted_volume()
    {
        // Two levels that do not exist, so the walk climbs to the mount folder, which
        // answers for the mounted volume rather than for the drive it sits on.
        var got = MoveSpaceCheck.AvailableFreeSpaceForDestination(DeepUnderMount);
        Row(8, $"AvailableFreeSpaceForDestination({DeepUnderMount})", MountedFreeBytes, got);
        Assert.Equal(MountedFreeBytes, got);
    }

    [MountedVolumeFact]
    public void Row09_free_space_at_the_mount_folder_is_the_mounted_volume()
    {
        var got = MoveSpaceCheck.AvailableFreeSpaceForDestination(MountFolder);
        Row(9, $"AvailableFreeSpaceForDestination({MountFolder})", MountedFreeBytes, got);
        Assert.Equal(MountedFreeBytes, got);
    }

    [MountedVolumeFact]
    public void Row10_free_space_beside_the_mount_is_not_the_mounted_volume()
    {
        var got = MoveSpaceCheck.AvailableFreeSpaceForDestination(Path.Combine(BesideFolder, "new"));
        Row(10, $"AvailableFreeSpaceForDestination({Path.Combine(BesideFolder, "new")})",
            $"more than {MountedFreeBytes}", got);
        Assert.NotNull(got);
        Assert.True(got > MountedFreeBytes);
    }

    [MountedVolumeFact]
    public void Row11_the_command_line_refuses_a_move_too_big_for_the_mounted_volume()
    {
        // The command line's overload, which measures through the walk itself.
        var got = MoveSpaceCheck.RefusalFreeSpace(UnderMount, MountedFreeBytes + 1);
        Row(11, $"RefusalFreeSpace({UnderMount}, {MountedFreeBytes + 1})", MountedFreeBytes, got);
        Assert.Equal(MountedFreeBytes, got);
    }

    [MountedVolumeFact]
    public void Row12_the_command_line_allows_a_move_that_fits_the_mounted_volume()
    {
        var got = MoveSpaceCheck.RefusalFreeSpace(UnderMount, 1);
        Row(12, $"RefusalFreeSpace({UnderMount}, 1)", null, got);
        Assert.Null(got);
    }

    [MountedVolumeFact]
    public void Row13_a_move_beside_the_mount_is_not_measured()
    {
        // On the cache's volume, so a rename: allowed whatever the figure would be.
        var got = MoveSpaceCheck.RefusalFreeSpace(BesideDestination, long.MaxValue);
        Row(13, $"RefusalFreeSpace({BesideDestination}, {long.MaxValue})", null, got);
        Assert.Null(got);
    }

    [MountedVolumeFact]
    public void Row14_the_window_measures_an_existing_folder_under_the_mount_as_the_mounted_volume()
    {
        // The window's pre-flight creates the destination and measures it directly.
        var got = StorageHelpers.GetAvailableFreeSpace(ExistingUnderMount);
        Row(14, $"GetAvailableFreeSpace({ExistingUnderMount})", MountedFreeBytes, got);
        Assert.True(Directory.Exists(ExistingUnderMount), "The run did not create the folder.");
        Assert.Equal(MountedFreeBytes, got);
    }

    [MountedVolumeFact]
    public void Row15_the_window_refuses_a_move_too_big_for_the_mounted_volume()
    {
        // The window's three-argument call, with its own measurement.
        var measured = StorageHelpers.GetAvailableFreeSpace(ExistingUnderMount);
        Assert.NotNull(measured);
        var got = MoveSpaceCheck.RefusalFreeSpace(ExistingUnderMount, measured.Value + 1, measured);
        Row(15, $"RefusalFreeSpace({ExistingUnderMount}, {measured.Value + 1}, {measured})", measured, got);
        Assert.Equal(measured, got);
    }

    [MountedVolumeFact]
    public void Row16_the_mounted_volume_reads_as_a_fixed_drive()
    {
        var volume = StorageHelpers.GetVolumeMountPoint(UnderMount);
        Assert.NotNull(volume);
        var got = StorageHelpers.GetDriveKind(volume);
        Row(16, $"GetDriveKind({volume})", DriveType.Fixed, got);
        Assert.Equal(DriveType.Fixed, got);
    }

    [MountedVolumeFact]
    public void Row17_a_destination_under_the_mount_is_recorded_as_another_fixed_drive()
    {
        var got = CleanupViewModel.ClassifyMoveDestination(UnderMount);
        Row(17, $"ClassifyMoveDestination({UnderMount})", MoveDestinationKinds.DifferentFixedDrive, got);
        Assert.Equal(MoveDestinationKinds.DifferentFixedDrive, got);
    }

    [MountedVolumeFact]
    public void Row18_a_destination_beside_the_mount_is_recorded_as_the_same_drive()
    {
        var got = CleanupViewModel.ClassifyMoveDestination(BesideDestination);
        Row(18, $"ClassifyMoveDestination({BesideDestination})", MoveDestinationKinds.SameDrive, got);
        Assert.Equal(MoveDestinationKinds.SameDrive, got);
    }
}
