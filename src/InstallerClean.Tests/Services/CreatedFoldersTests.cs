using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using InstallerClean.Services;
using NSubstitute;

namespace InstallerClean.Tests.Services;

/// <summary>
/// The folders a Move makes on the way to its destination: which ones creating the
/// destination would make, and the removal of them again, which takes a folder only
/// while it is empty and stops at the first that is there and will not go.
/// </summary>
public class CreatedFoldersTests
{
    private readonly MockFileSystem _fs = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ic-created");
    private readonly IDestinationGuard _guard;

    public CreatedFoldersTests()
    {
        _fs.AddDirectory(_root);
        _guard = new ByPathDestinationGuard(_fs);
    }

    private string Folder(params string[] parts) => Path.Combine([_root, .. parts]);

    [Fact]
    public void The_destination_and_each_missing_folder_above_it_are_listed_destination_first()
    {
        var missing = CreatedFolders.MissingOnPath(_fs, Folder("a", "b", "c"));

        Assert.Equal([Folder("a", "b", "c"), Folder("a", "b"), Folder("a")], missing);
    }

    [Fact]
    public void A_missing_destination_in_a_folder_that_is_there_is_the_only_one_listed()
    {
        Assert.Equal([Folder("a")], CreatedFolders.MissingOnPath(_fs, Folder("a")));
    }

    [Fact]
    public void A_destination_that_is_there_lists_nothing()
    {
        _fs.AddDirectory(Folder("a", "b"));

        Assert.Empty(CreatedFolders.MissingOnPath(_fs, Folder("a", "b")));
    }

    [Fact]
    public void A_folder_above_the_destination_that_is_there_is_not_listed()
    {
        _fs.AddDirectory(Folder("a"));

        Assert.Equal([Folder("a", "b")], CreatedFolders.MissingOnPath(_fs, Folder("a", "b")));
    }

    [Fact]
    public void A_trailing_separator_on_the_destination_lists_each_folder_once()
    {
        var withSeparator = Folder("a", "b") + Path.DirectorySeparatorChar;

        Assert.Equal([Folder("a", "b"), Folder("a")], CreatedFolders.MissingOnPath(_fs, withSeparator));
    }

    [Fact]
    public void A_destination_spelled_with_a_parent_reference_lists_the_folders_the_create_makes()
    {
        var spelled = Path.Combine(Folder("a", "y"), "..", "z");

        Assert.Equal([Folder("a", "z"), Folder("a")], CreatedFolders.MissingOnPath(_fs, spelled));
    }

    [Fact]
    public void A_destination_that_cannot_be_spelled_as_a_full_path_lists_nothing()
    {
        var fileSystem = Substitute.For<IFileSystem>();
        var path = Substitute.For<IPath>();
        path.GetFullPath(Arg.Any<string>()).Returns(_ => throw new PathTooLongException());
        fileSystem.Path.Returns(path);

        Assert.Empty(CreatedFolders.MissingOnPath(fileSystem, Folder("a")));
    }

    [Fact]
    public void A_drive_root_is_never_listed()
    {
        var nothingThere = Substitute.For<IFileSystem>();
        var directory = Substitute.For<IDirectory>();
        nothingThere.Path.Returns(_fs.Path);
        nothingThere.Directory.Returns(directory);
        var destination = Folder("a");

        var missing = CreatedFolders.MissingOnPath(nothingThere, destination);

        Assert.Equal(destination, missing[0]);
        Assert.All(missing, folder => Assert.False(string.IsNullOrEmpty(Path.GetDirectoryName(folder))));
    }

    [Fact]
    public void Folders_that_are_empty_are_removed_in_the_order_given()
    {
        _fs.AddDirectory(Folder("a", "b", "c"));

        CreatedFolders.RemoveWhileEmpty(_fs, _guard, [Folder("a", "b", "c"), Folder("a", "b"), Folder("a")]);

        Assert.False(_fs.Directory.Exists(Folder("a")));
        Assert.True(_fs.Directory.Exists(_root));
    }

    [Fact]
    public void A_folder_holding_a_file_is_left_and_keeps_every_folder_above_it()
    {
        _fs.AddDirectory(Folder("a", "b", "c"));
        _fs.AddFile(Path.Combine(Folder("a", "b"), "kept.txt"), new MockFileData("x"));

        CreatedFolders.RemoveWhileEmpty(_fs, _guard, [Folder("a", "b", "c"), Folder("a", "b"), Folder("a")]);

        Assert.False(_fs.Directory.Exists(Folder("a", "b", "c")));
        Assert.True(_fs.File.Exists(Path.Combine(Folder("a", "b"), "kept.txt")));
        Assert.True(_fs.Directory.Exists(Folder("a")));
    }

    [Fact]
    public void A_folder_that_is_no_longer_there_is_passed_over()
    {
        _fs.AddDirectory(Folder("a", "b"));

        CreatedFolders.RemoveWhileEmpty(_fs, _guard, [Folder("a", "b", "c"), Folder("a", "b"), Folder("a")]);

        Assert.False(_fs.Directory.Exists(Folder("a")));
    }

    [Fact]
    public void A_removal_that_throws_ends_without_throwing()
    {
        var guard = Substitute.For<IDestinationGuard>();
        guard.TryRemoveEmptyFolder(Arg.Any<string>()).Returns(_ => throw new InvalidOperationException("refused"));

        var thrown = Record.Exception(() => CreatedFolders.RemoveWhileEmpty(_fs, guard, [Folder("a")]));

        Assert.Null(thrown);
    }
}
