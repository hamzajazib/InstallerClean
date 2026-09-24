using InstallerClean.Interop.Native;
using InstallerClean.Services;

namespace InstallerClean.Tests.Services;

/// <summary>
/// What the reader concludes from Windows' answers about one open handle, on answers
/// written out here rather than read off a disk, so every volume and file system the
/// reader refuses can be put to it on any machine.
///
/// EVERY ROW IS THE CONTROL WITH ONE ANSWER CHANGED, so the answer that changed is
/// the one that moved the outcome.
/// </summary>
public class FileTimesReaderClassifyTests
{
    private static readonly DateTime Old = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Kernel32.FILE_BASIC_INFO Basic(long changeTime) => new()
    {
        CreationTime = Old.ToFileTimeUtc(),
        LastAccessTime = Old.ToFileTimeUtc(),
        LastWriteTime = Old.ToFileTimeUtc(),
        ChangeTime = changeTime,
        FileAttributes = 0x20, // FILE_ATTRIBUTE_ARCHIVE: a plain file
    };

    private static readonly FileTimesReader.HandleAnswers Control = new(
        Basic(Old.ToFileTimeUtc()),
        @"\\?\Volume{00000000-0000-0000-0000-000000000001}\",
        DriveType.Fixed,
        "NTFS");

    [Fact]
    public void A_plain_file_on_a_fixed_NTFS_volume_named_by_a_GUID_path_reads()
    {
        var outcome = FileTimesReader.Classify(Control, out var times);

        Assert.Equal(FileTimesRead.Read, outcome);
        Assert.Equal(new FileTimes(Old, Old, Old), times);
    }

    [Fact]
    public void A_change_time_of_zero_is_not_a_time()
    {
        var outcome = FileTimesReader.Classify(
            Control with { Basic = Basic(changeTime: 0) }, out var times);

        Assert.Equal(FileTimesRead.TimesUnavailable, outcome);
        Assert.Equal(default, times);
    }

    [Fact]
    public void A_volume_with_no_GUID_path_is_unestablished()
    {
        // The drive type stays Fixed so the missing root is the only answer short.
        var outcome = FileTimesReader.Classify(Control with { VolumeRoot = null }, out _);

        Assert.Equal(FileTimesRead.VolumeUnestablished, outcome);
    }

    [Fact]
    public void A_volume_that_will_not_name_its_file_system_is_unestablished()
    {
        var outcome = FileTimesReader.Classify(Control with { FileSystemName = null }, out _);

        Assert.Equal(FileTimesRead.VolumeUnestablished, outcome);
    }

    [Theory]
    [InlineData(DriveType.Removable)]
    [InlineData(DriveType.Network)]
    [InlineData(DriveType.CDRom)]
    [InlineData(DriveType.Ram)]
    [InlineData(DriveType.Unknown)]
    [InlineData(DriveType.NoRootDirectory)]
    public void A_volume_that_is_not_a_fixed_drive_is_refused(DriveType kind)
    {
        var outcome = FileTimesReader.Classify(Control with { DriveKind = kind }, out _);

        Assert.Equal(FileTimesRead.NotAFixedVolume, outcome);
    }

    [Theory]
    [InlineData("FAT32")]
    [InlineData("exFAT")]
    [InlineData("ReFS")]
    [InlineData("")]
    public void A_file_system_other_than_NTFS_is_refused(string fileSystem)
    {
        var outcome = FileTimesReader.Classify(Control with { FileSystemName = fileSystem }, out _);

        Assert.Equal(FileTimesRead.NotNtfs, outcome);
    }
}
