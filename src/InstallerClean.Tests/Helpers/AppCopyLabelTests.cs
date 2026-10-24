using InstallerClean.Helpers;
using InstallerClean.Services;

namespace InstallerClean.Tests.Helpers;

public class AppCopyLabelTests
{
    // The receiver's allowlist for app.copyKind, spelled out here as the seven values it
    // accepts.
    private static readonly string[] ReceiverValues =
        ["setup", "portable", "slim", "commandLine", "scoop", "fromSource", "undetermined"];

    [Fact]
    public void Every_kind_of_copy_has_a_label_the_receiver_accepts()
    {
        var kinds = Enum.GetValues<AppCopyKind>();

        // Both directions: a kind added to the enum has to be named by the receiver, and a
        // value the receiver accepts has to be one the client can send.
        Assert.Equal(ReceiverValues.Length, kinds.Length);
        foreach (var kind in kinds)
            Assert.Contains(AppCopyLabel.For(kind), ReceiverValues);
        Assert.Equal(
            ReceiverValues.Order(),
            kinds.Select(AppCopyLabel.For).Order());
    }

    [Theory]
    [InlineData(AppCopyKind.Setup, "setup")]
    [InlineData(AppCopyKind.Portable, "portable")]
    [InlineData(AppCopyKind.Slim, "slim")]
    [InlineData(AppCopyKind.CommandLineDownload, "commandLine")]
    [InlineData(AppCopyKind.Scoop, "scoop")]
    [InlineData(AppCopyKind.FromSource, "fromSource")]
    [InlineData(AppCopyKind.Undetermined, "undetermined")]
    public void Each_kind_has_its_own_label(AppCopyKind kind, string expected)
        => Assert.Equal(expected, AppCopyLabel.For(kind));

    [Fact]
    public void A_value_that_is_not_a_kind_has_no_label()
        => Assert.Throws<ArgumentOutOfRangeException>(() => AppCopyLabel.For((AppCopyKind)99));
}
