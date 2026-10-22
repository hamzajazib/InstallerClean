using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The write probe names a step as refused access only where its open returned
/// STATUS_ACCESS_DENIED. Every other status the open can return names the step as failed,
/// without that cause.
/// </summary>
public class DirectoryGuardAccessRefusalTests
{
    [Theory]
    [InlineData(unchecked((int)0xC0000022), true)]  // STATUS_ACCESS_DENIED
    [InlineData(unchecked((int)0xC0000043), false)] // STATUS_SHARING_VIOLATION
    [InlineData(unchecked((int)0xC00000CA), false)] // STATUS_NETWORK_ACCESS_DENIED
    [InlineData(unchecked((int)0xC0000034), false)] // STATUS_OBJECT_NAME_NOT_FOUND
    [InlineData(unchecked((int)0xC0000056), false)] // STATUS_DELETE_PENDING
    public void Only_STATUS_ACCESS_DENIED_is_a_refusal_of_access(int status, bool refusal)
    {
        Assert.Equal(refusal, DirectoryGuard.IsAccessRefusal(status));
    }
}
