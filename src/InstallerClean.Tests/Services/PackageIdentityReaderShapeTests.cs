using System.Reflection;
using InstallerClean.Models;
using InstallerClean.Services;

namespace InstallerClean.Tests.Services;

/// <summary>
/// The real reader answers the read that says why it gave no identity with a method of its
/// own. The interface's default body answers every null as a file that would not read, so a
/// reader left on it would count no installation as declaring no product code.
/// </summary>
public class PackageIdentityReaderShapeTests
{
    [Fact]
    public void The_real_reader_says_why_with_a_method_of_its_own()
    {
        Assert.Equal(typeof(PackageIdentityReader), ImplementerOfTheReadThatSaysWhy(typeof(PackageIdentityReader)));
    }

    [Fact]
    public void A_reader_with_only_the_plain_read_is_left_on_the_interface_s_default()
    {
        // The must-miss half: what the test above would see of a reader that did not
        // implement the read itself.
        Assert.Equal(typeof(IPackageIdentityReader), ImplementerOfTheReadThatSaysWhy(typeof(PlainReader)));
    }

    /// <summary>The type declaring the method that answers the four-argument read for <paramref name="reader"/>.</summary>
    private static Type? ImplementerOfTheReadThatSaysWhy(Type reader)
    {
        var map = reader.GetInterfaceMap(typeof(IPackageIdentityReader));
        var index = Array.FindIndex(map.InterfaceMethods, method => method.GetParameters().Length == 4);
        Assert.True(index >= 0, "IPackageIdentityReader has no four-argument Read");
        return map.TargetMethods[index].DeclaringType;
    }

    private sealed class PlainReader : IPackageIdentityReader
    {
        public PackageIdentity? Read(string filePath, bool isPatch, out string detail)
        {
            detail = string.Empty;
            return null;
        }
    }
}
