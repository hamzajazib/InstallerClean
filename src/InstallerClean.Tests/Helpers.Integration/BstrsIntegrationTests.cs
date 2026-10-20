using System.Runtime.InteropServices;
using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers.Integration;

/// <summary>
/// Bstrs makes the same BSTR as <see cref="Marshal.StringToBSTR"/>: the same length in the
/// four bytes before the characters, the same characters and a null character after them.
/// Windows only: the BSTRs come from Windows.
/// </summary>
public class BstrsIntegrationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("https://github.com/no-faff/InstallerClean")]
    [InlineData("a\0b")]
    public void A_BSTR_matches_the_one_Marshal_StringToBSTR_makes(string value)
    {
        var ours = Bstrs.FromString(value);
        var theirs = Marshal.StringToBSTR(value);
        try
        {
            Assert.NotEqual(IntPtr.Zero, ours);
            Assert.Equal(Marshal.ReadInt32(theirs, -4), Marshal.ReadInt32(ours, -4));
            Assert.Equal(value.Length * sizeof(char), Marshal.ReadInt32(ours, -4));
            Assert.Equal(Marshal.PtrToStringBSTR(theirs), Marshal.PtrToStringBSTR(ours));
            Assert.Equal(0, Marshal.ReadInt16(ours, value.Length * sizeof(char)));
        }
        finally
        {
            Bstrs.Free(ours);
            Marshal.FreeBSTR(theirs);
        }
    }
}
