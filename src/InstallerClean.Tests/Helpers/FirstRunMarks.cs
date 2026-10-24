using InstallerClean.Services;
using NSubstitute;

namespace InstallerClean.Tests.Helpers;

/// <summary>Substitutes for <see cref="IFirstRunMark"/>.</summary>
internal static class FirstRunMarks
{
    /// <summary>A mark whose claim is granted, which is what a PC with no earlier run answers.</summary>
    public static IFirstRunMark Claiming()
    {
        var mark = Substitute.For<IFirstRunMark>();
        mark.TryClaim().Returns(true);
        return mark;
    }
}
