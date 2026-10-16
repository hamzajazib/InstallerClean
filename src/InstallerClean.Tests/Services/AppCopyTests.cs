using Microsoft.Win32;
using NSubstitute;
using InstallerClean.Services;

namespace InstallerClean.Tests.Services;

/// <summary>
/// Which kind of copy is running, decided from facts handed in, and the download
/// address each kind updates from.
/// </summary>
public class AppCopyTests
{
    private const string ProgramFilesFolder = @"C:\Program Files\InstallerClean";
    private const string ScoopFolder = @"C:\Users\someone\scoop\apps\installerclean\3.4.0";
    private const string DownloadsFolder = @"C:\Users\someone\Downloads";

    private static Func<string, bool> FilesPresent(params string[] paths)
    {
        var present = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        return present.Contains;
    }

    private static readonly Func<string, bool> NoFiles = _ => false;

    [Fact]
    public void A_build_that_is_not_a_single_file_is_from_source_whatever_else_holds()
    {
        var kind = AppCopy.Decide(
            isSingleFile: false, isSelfContained: true, ProgramFilesFolder,
            setupInstallLocation: ProgramFilesFolder + @"\", NoFiles);

        Assert.Equal(AppCopyKind.FromSource, kind);
    }

    [Theory]
    [InlineData(@"C:\Program Files\InstallerClean\")]
    [InlineData(@"C:\Program Files\InstallerClean")]
    [InlineData(@"c:\program files\installerclean\")]
    public void A_copy_in_the_folder_the_setup_recorded_is_the_setups(string installLocation)
    {
        var kind = AppCopy.Decide(
            isSingleFile: true, isSelfContained: true, ProgramFilesFolder, installLocation, NoFiles);

        Assert.Equal(AppCopyKind.Setup, kind);
    }

    [Fact]
    public void The_portable_download_beside_a_setup_install_elsewhere_is_the_portable()
    {
        // The setup installs the very file the portable download is, so only the
        // folder tells them apart.
        var kind = AppCopy.Decide(
            isSingleFile: true, isSelfContained: true, DownloadsFolder,
            setupInstallLocation: ProgramFilesFolder + @"\", NoFiles);

        Assert.Equal(AppCopyKind.Portable, kind);
    }

    [Fact]
    public void A_recorded_location_that_is_not_a_path_names_no_folder()
    {
        var kind = AppCopy.Decide(
            isSingleFile: true, isSelfContained: true, ProgramFilesFolder,
            setupInstallLocation: "\0not a path", NoFiles);

        Assert.Equal(AppCopyKind.Portable, kind);
    }

    [Theory]
    [InlineData("scoop-manifest.json", "scoop-install.json")]
    [InlineData("manifest.json", "install.json")]
    public void A_copy_beside_both_of_Scoops_files_is_Scoops(string manifest, string install)
    {
        var kind = AppCopy.Decide(
            isSingleFile: true, isSelfContained: true, ScoopFolder, setupInstallLocation: null,
            FilesPresent(Path.Combine(ScoopFolder, manifest), Path.Combine(ScoopFolder, install)));

        Assert.Equal(AppCopyKind.Scoop, kind);
    }

    [Theory]
    [InlineData("scoop-manifest.json")]
    [InlineData("install.json")]
    public void One_of_Scoops_files_alone_does_not_make_a_Scoop_copy(string only)
    {
        var kind = AppCopy.Decide(
            isSingleFile: true, isSelfContained: true, ScoopFolder, setupInstallLocation: null,
            FilesPresent(Path.Combine(ScoopFolder, only)));

        Assert.Equal(AppCopyKind.Portable, kind);
    }

    [Theory]
    [InlineData(true, AppCopyKind.Portable)]
    [InlineData(false, AppCopyKind.Slim)]
    public void Otherwise_the_build_tells_the_portable_from_the_slim(bool selfContained, AppCopyKind expected)
    {
        var kind = AppCopy.Decide(
            isSingleFile: true, selfContained, DownloadsFolder, setupInstallLocation: null, NoFiles);

        Assert.Equal(expected, kind);
    }

    [Theory]
    [InlineData(AppCopyKind.Setup, "https://github.com/no-faff/InstallerClean/releases/download/v3.5.0/InstallerClean-3.5.0-setup.exe")]
    [InlineData(AppCopyKind.Portable, "https://github.com/no-faff/InstallerClean/releases/download/v3.5.0/InstallerClean-3.5.0-portable.exe")]
    [InlineData(AppCopyKind.Scoop, "https://github.com/no-faff/InstallerClean/releases/download/v3.5.0/InstallerClean-3.5.0-portable.exe")]
    [InlineData(AppCopyKind.Slim, "https://github.com/no-faff/InstallerClean/releases/download/v3.5.0/InstallerClean-3.5.0-slim.exe")]
    public void Each_kind_downloads_its_own_file_from_the_releases_tag(AppCopyKind kind, string expected)
    {
        Assert.Equal(new Uri(expected), AppCopy.DownloadAddress(kind, "3.5.0"));
        Assert.True(AppCopy.CanUpdateItself(kind));
    }

    [Fact]
    public void A_build_from_source_has_no_download_and_cannot_update_itself()
    {
        Assert.Null(AppCopy.DownloadAddress(AppCopyKind.FromSource, "3.5.0"));
        Assert.Null(AppCopy.DownloadFileName(AppCopyKind.FromSource, "3.5.0"));
        Assert.False(AppCopy.CanUpdateItself(AppCopyKind.FromSource));
    }

    [Fact]
    public void The_setups_location_is_read_from_its_uninstall_key()
    {
        var registry = Substitute.For<IRegistryReader>();
        registry.LocalMachineValues(AppCopy.SetupUninstallKeyPath).Returns(new RegistryKeyValues(
            RegistryKeyPresence.Present,
            [
                new RegistryValue("DisplayVersion", RegistryValueKind.String, "3.4.0"),
                new RegistryValue("InstallLocation", RegistryValueKind.String, @"C:\Program Files\InstallerClean\"),
            ]));

        Assert.Equal(@"C:\Program Files\InstallerClean\", AppCopy.ReadSetupInstallLocation(registry));
    }

    [Theory]
    [InlineData(RegistryKeyPresence.Absent)]
    [InlineData(RegistryKeyPresence.Unreadable)]
    public void No_location_where_the_key_is_absent_or_unreadable(RegistryKeyPresence presence)
    {
        var registry = Substitute.For<IRegistryReader>();
        registry.LocalMachineValues(AppCopy.SetupUninstallKeyPath).Returns(new RegistryKeyValues(presence));

        Assert.Null(AppCopy.ReadSetupInstallLocation(registry));
    }

    [Fact]
    public void No_location_where_the_value_is_missing_or_empty()
    {
        var registry = Substitute.For<IRegistryReader>();
        registry.LocalMachineValues(AppCopy.SetupUninstallKeyPath).Returns(
            new RegistryKeyValues(RegistryKeyPresence.Present,
                [new RegistryValue("DisplayVersion", RegistryValueKind.String, "3.4.0")]),
            new RegistryKeyValues(RegistryKeyPresence.Present,
                [new RegistryValue("InstallLocation", RegistryValueKind.String, "  ")]));

        Assert.Null(AppCopy.ReadSetupInstallLocation(registry));
        Assert.Null(AppCopy.ReadSetupInstallLocation(registry));
    }
}
