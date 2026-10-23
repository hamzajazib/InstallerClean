using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// A file version is the release a version string names where the first three parts
/// match, a part left out counting as 0, as the update check compares versions. These
/// read no file, so they run on any host.
/// </summary>
public class FileVersionResourceIsReleaseTests
{
    [Theory]
    [InlineData("3.5.0.0", "3.5.0")]
    [InlineData("3.5.0.7", "3.5.0")]
    [InlineData("3.5", "3.5.0")]
    [InlineData("3.5.0.0", "3.5")]
    public void A_file_version_whose_first_three_parts_match_is_the_release(string carried, string release) =>
        Assert.True(FileVersionResource.IsRelease(Version.Parse(carried), release));

    [Theory]
    [InlineData("3.5.1.0", "3.5.0")]
    [InlineData("3.4.0.0", "3.5.0")]
    [InlineData("4.5.0.0", "3.5.0")]
    [InlineData("3.5.0.0", "3.5.1")]
    public void A_file_version_that_differs_in_any_of_the_first_three_parts_is_not(string carried, string release) =>
        Assert.False(FileVersionResource.IsRelease(Version.Parse(carried), release));

    [Theory]
    [InlineData("")]
    [InlineData("v3.5.0")]
    [InlineData("not a version")]
    public void A_release_that_is_not_a_version_is_never_matched(string release) =>
        Assert.False(FileVersionResource.IsRelease(new Version(3, 5, 0, 0), release));
}
