using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers.Integration;

/// <summary>
/// KnownFolders gives the same path as <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/>
/// for every folder it answers. Windows only: the answers come from Windows.
/// </summary>
public class KnownFoldersIntegrationTests
{
    [Theory]
    [InlineData(Environment.SpecialFolder.Windows)]
    [InlineData(Environment.SpecialFolder.LocalApplicationData)]
    [InlineData(Environment.SpecialFolder.CommonApplicationData)]
    [InlineData(Environment.SpecialFolder.ProgramFiles)]
    [InlineData(Environment.SpecialFolder.ProgramFilesX86)]
    public void Each_folder_is_the_path_dotnet_gives_for_it(Environment.SpecialFolder folder)
    {
        var expected = Environment.GetFolderPath(folder);

        Assert.NotEmpty(expected);
        Assert.Equal(expected, KnownFolders.GetPath(folder), StringComparer.Ordinal);
    }
}
