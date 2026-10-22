using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The write probe names a folder as one that will not give files back only where its
/// second open of the probe's file was refused access. Every other status the open can
/// return leaves the probe's refusal without that cause.
/// </summary>
public class DirectoryGuardReadBackTests
{
    [Theory]
    [InlineData(unchecked((int)0xC0000022), true)]  // STATUS_ACCESS_DENIED
    [InlineData(unchecked((int)0xC0000043), false)] // STATUS_SHARING_VIOLATION
    [InlineData(unchecked((int)0xC00000CA), false)] // STATUS_NETWORK_ACCESS_DENIED
    [InlineData(unchecked((int)0xC0000034), false)] // STATUS_OBJECT_NAME_NOT_FOUND
    [InlineData(unchecked((int)0xC0000056), false)] // STATUS_DELETE_PENDING
    public void Only_a_refusal_of_access_is_a_folder_that_will_not_give_files_back(int status, bool refusal)
    {
        Assert.Equal(refusal, DirectoryGuard.IsReadBackRefusal(status));
    }
}
