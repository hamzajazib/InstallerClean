using InstallerClean.Services;
using Microsoft.Win32;

namespace InstallerClean.Tests.Services.Integration;

/// <summary>
/// <see cref="FirstRunMark"/> against the real registry, on a scratch key under
/// HKCU that each test creates under its own name and deletes afterwards, so it
/// needs no administrator rights and leaves nothing behind.
/// </summary>
public sealed class FirstRunMarkTests : IDisposable
{
    // One level under Software, so the key Dispose deletes is the only one a test creates.
    private readonly string _keyPath =
        @"Software\InstallerClean-test-FirstRunMark-" + Guid.NewGuid().ToString("N");

    private FirstRunMark Mark => new(RegistryHive.CurrentUser, _keyPath);

    public void Dispose() =>
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);

    [Fact]
    public void A_key_that_is_not_there_reads_as_not_set()
    {
        Assert.Equal(FirstRunMarkState.NotSet, Mark.Read());
    }

    [Fact]
    public void A_key_without_the_value_reads_as_not_set()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(_keyPath))
            key.SetValue("SomethingElse", 1, RegistryValueKind.DWord);

        Assert.Equal(FirstRunMarkState.NotSet, Mark.Read());
    }

    [Fact]
    public void Setting_it_creates_the_key_and_writes_a_dword_of_one()
    {
        Mark.Set();

        using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
        Assert.NotNull(key);
        Assert.Equal(RegistryValueKind.DWord, key.GetValueKind(FirstRunMark.ValueName));
        Assert.Equal(1, key.GetValue(FirstRunMark.ValueName));
        Assert.Equal(FirstRunMarkState.Set, Mark.Read());
    }

    [Fact]
    public void Setting_it_again_leaves_it_set()
    {
        Mark.Set();
        Mark.Set();

        Assert.Equal(FirstRunMarkState.Set, Mark.Read());
    }

    [Theory]
    [InlineData(0, RegistryValueKind.DWord)]
    [InlineData("0", RegistryValueKind.String)]
    [InlineData("", RegistryValueKind.String)]
    public void Any_value_under_the_name_reads_as_set(object value, RegistryValueKind kind)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(_keyPath))
            key.SetValue(FirstRunMark.ValueName, value, kind);

        Assert.Equal(FirstRunMarkState.Set, Mark.Read());
    }

    [Fact]
    public void The_mark_is_FirstRunRecorded_under_the_NoFaff_key()
    {
        // The installer's note names this key and value.
        Assert.Equal(@"SOFTWARE\NoFaff\InstallerClean", FirstRunMark.KeyPath);
        Assert.Equal("FirstRunRecorded", FirstRunMark.ValueName);
    }
}
