using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers.Integration;

/// <summary>
/// Guids makes random GUIDs of the same kind as <see cref="Guid.NewGuid"/>: version 4,
/// RFC 9562 variant, no two alike. Windows only: the GUIDs come from Windows.
/// </summary>
public class GuidsIntegrationTests
{
    [Fact]
    public void Each_GUID_is_a_random_version_4_GUID_and_none_repeats()
    {
        var made = Enumerable.Range(0, 1000).Select(_ => Guids.NewGuid()).ToList();

        Assert.All(made, guid =>
        {
            Assert.Equal(4, guid.Version);
            Assert.Equal(0b10, guid.Variant >> 2);
        });
        Assert.Equal(made.Count, made.Distinct().Count());
    }
}
