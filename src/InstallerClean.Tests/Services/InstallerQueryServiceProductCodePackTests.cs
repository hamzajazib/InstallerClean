using InstallerClean.Services;

namespace InstallerClean.Tests.Services;

/// <summary>
/// <see cref="InstallerQueryService.PackRegistryCode"/>, which writes a braced product
/// or patch code in the packed form Windows Installer names its registry keys with, so
/// the declared-product check can read the key holding a source list.
///
/// THE EXPECTED VALUES ARE READ OFF WINDOWS, NOT COMPOSED. The first three pairs are the
/// ones <see cref="InstallerQueryServiceProductCodeUnpackTests"/> takes from a machine's
/// SOFTWARE hive; the last two are a test product and its patch, each key name printed
/// beside the code Windows Installer was given for it. A code whose fields read the same
/// either way round, as the all-ones codes elsewhere do, packs to one name however a
/// field is written; these do not.
/// </summary>
public class InstallerQueryServiceProductCodePackTests
{
    [Theory]
    [InlineData("{1D8E6291-B0D5-35EC-8441-6616F567A0F7}", "1926E8D15D0BCE53481466615F760A7F")]
    [InlineData("{C67045D4-F4DE-3AB5-B2DB-E3F5DAC14D9C}", "4D54076CED4F5BA32BBD3E5FAD1CD4C9")]
    [InlineData("{6F8500D2-A80F-3347-9081-B41E71C8592B}", "2D0058F6F08A743309184BE1178C95B2")]
    [InlineData("{9F5D14EB-8BE8-4B4F-B961-3137244BFA02}", "BE41D5F98EB8F4B49B16137342B4AF20")]
    [InlineData("{CD8784C8-1137-495E-A7C3-E5992B0E00CB}", "8C4878DC7311E5947A3C5E99B2E000BC")]
    public void A_code_packs_to_the_key_name_the_machine_records_for_it(string code, string packed)
    {
        Assert.Equal(packed, InstallerQueryService.PackRegistryCode(code));
        Assert.Equal(code, InstallerQueryService.UnpackRegistryProductCode(packed));
    }

    [Fact]
    public void A_lower_case_code_packs_to_the_upper_case_key_name()
    {
        Assert.Equal(
            "8C4878DC7311E5947A3C5E99B2E000BC",
            InstallerQueryService.PackRegistryCode("{cd8784c8-1137-495e-a7c3-e5992b0e00cb}"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("CD8784C8-1137-495E-A7C3-E5992B0E00CB")]
    [InlineData("{CD8784C8-1137-495E-A7C3-E5992B0E00C}")]
    [InlineData("{CD8784C81-137-495E-A7C3-E5992B0E00CB}")]
    [InlineData("{CD8784C8-1137-495E-A7C3-E5992B0E00CG}")]
    [InlineData(@"{CD8784C8-1137-495E-A7C3-E5992B0E00C\}")]
    [InlineData("8C4878DC7311E5947A3C5E99B2E000BC")]
    public void A_value_that_is_not_a_braced_guid_packs_to_nothing(string code)
    {
        // A key path built from anything else would name a key that is not the list's.
        Assert.Null(InstallerQueryService.PackRegistryCode(code));
    }
}
