using InstallerClean.Helpers;
using InstallerClean.Models;

namespace InstallerClean.Tests.Helpers;

public class ReportHostLabelTests
{
    [Theory]
    [InlineData(ReportHost.Window, "window")]
    [InlineData(ReportHost.CommandLine, "commandLine")]
    public void Each_program_has_its_own_label(ReportHost host, string expected)
        => Assert.Equal(expected, ReportHostLabel.For(host));

    [Fact]
    public void Every_program_has_a_label_and_the_labels_differ()
    {
        var labels = Enum.GetValues<ReportHost>().Select(ReportHostLabel.For).ToArray();

        Assert.Equal(2, labels.Length);
        Assert.Equal(labels.Length, labels.Distinct().Count());
    }

    [Fact]
    public void A_value_that_is_not_a_program_has_no_label()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ReportHostLabel.For((ReportHost)99));
}
