using InstallerClean.Helpers;
using InstallerClean.Interop.Native;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The answers a drive gives that <see cref="StorageHelpers.IsNotReady"/> takes as the drive not
/// being ready, which is what sends a source on a disc drive or card reader to be compared by
/// name.
/// </summary>
public class StorageHelpersNotReadyTests
{
    [Theory]
    [InlineData(Kernel32.ERROR_NOT_READY)]
    [InlineData(Kernel32.ERROR_NO_MEDIA_IN_DRIVE)]
    public void A_drive_answering_that_it_is_not_ready_or_has_no_media_is_not_ready(int error) =>
        Assert.True(StorageHelpers.IsNotReadyAnswer(error));

    [Theory]
    [InlineData(0)]
    [InlineData(Kernel32.ERROR_FILE_NOT_FOUND)]
    [InlineData(Kernel32.ERROR_PATH_NOT_FOUND)]
    [InlineData(5)]
    [InlineData(1117)]
    public void A_drive_giving_any_other_answer_is_not_taken_as_not_ready(int error) =>
        Assert.False(StorageHelpers.IsNotReadyAnswer(error));
}
