using System.IO.Abstractions;
using InstallerClean.Services;
using NSubstitute;

namespace InstallerClean.Tests.Services;

/// <summary>
/// The by-path destination guard over a substituted filesystem. The view-model fixtures
/// that build a Move over a substituted <see cref="IFileSystem"/> assert the folder
/// removal as the one-argument <c>Directory.Delete(path)</c> call, and NSubstitute treats
/// each overload as its own call, so these hold the guard to that overload.
/// </summary>
public class ByPathDestinationGuardTests
{
    private readonly IFileSystem _fileSystem = Substitute.For<IFileSystem>();
    private readonly IDirectory _directory = Substitute.For<IDirectory>();
    private readonly ByPathDestinationGuard _guard;

    public ByPathDestinationGuardTests()
    {
        _fileSystem.Directory.Returns(_directory);
        _guard = new ByPathDestinationGuard(_fileSystem);
    }

    [Fact]
    public void An_emptied_folder_is_removed_with_the_one_argument_Delete()
    {
        Assert.True(_guard.TryRemoveEmptyFolder("X"));

        _directory.Received(1).Delete("X");
        _directory.DidNotReceive().Delete(Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public void A_folder_the_filesystem_will_not_remove_answers_false()
    {
        _directory.When(d => d.Delete("X")).Do(_ => throw new IOException("not empty"));

        Assert.False(_guard.TryRemoveEmptyFolder("X"));
    }
}
