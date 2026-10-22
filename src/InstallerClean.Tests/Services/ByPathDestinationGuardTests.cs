using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using InstallerClean.Services;
using NSubstitute;

namespace InstallerClean.Tests.Services;

/// <summary>
/// The by-path destination guard over a substituted filesystem, which the Move fixtures
/// use in place of the handle-based guard. These hold what each of its three methods
/// answers, the failure code it reports and the filesystem calls it makes. The folder
/// removal is the one-argument <c>Directory.Delete(path)</c>, which NSubstitute treats as
/// a different call from the two-argument form.
/// </summary>
public class ByPathDestinationGuardTests
{
    private readonly IFileSystem _fileSystem = Substitute.For<IFileSystem>();
    private readonly IDirectory _directory = Substitute.For<IDirectory>();
    private readonly IFile _file = Substitute.For<IFile>();
    private readonly ByPathDestinationGuard _guard;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ic-guard-test");
    private readonly string _copy = Path.Combine(Path.GetTempPath(), "ic-guard-test", "a.msi");

    public ByPathDestinationGuardTests()
    {
        // MockFileSystem's Path is a working implementation; a bare substitute returns an
        // empty string from Combine and GetRandomFileName.
        _fileSystem.Path.Returns(new MockFileSystem().Path);
        _fileSystem.Directory.Returns(_directory);
        _fileSystem.File.Returns(_file);
        _guard = new ByPathDestinationGuard(_fileSystem);
    }

    /// <summary>
    /// The refusals a filesystem gives for a folder or a file it will not take, by exception
    /// name. A theory row carries the name, which the test framework can serialise, so each
    /// refusal is its own test case.
    /// </summary>
    private static readonly Dictionary<string, Func<Exception>> RefusalFactories = new()
    {
        [nameof(IOException)] = () => new IOException("refused"),
        [nameof(UnauthorizedAccessException)] = () => new UnauthorizedAccessException("refused"),
        [nameof(DirectoryNotFoundException)] = () => new DirectoryNotFoundException("refused"),
        [nameof(PathTooLongException)] = () => new PathTooLongException("refused"),
        [nameof(NotSupportedException)] = () => new NotSupportedException("refused"),
        [nameof(ArgumentException)] = () => new ArgumentException("refused"),
    };

    public static TheoryData<string> RefusalNames => new(RefusalFactories.Keys);

    private static Exception NewRefusal(string name) => RefusalFactories[name]();

    [Theory]
    [MemberData(nameof(RefusalNames))]
    public void Each_refusal_name_builds_the_exception_it_names(string refusalName)
    {
        Assert.Equal(refusalName, NewRefusal(refusalName).GetType().Name);
    }

    [Fact]
    public void An_emptied_folder_is_removed_with_the_one_argument_Delete()
    {
        Assert.True(_guard.TryRemoveEmptyFolder("X"));

        _directory.Received(1).Delete("X");
        _directory.DidNotReceive().Delete(Arg.Any<string>(), Arg.Any<bool>());
    }

    [Theory]
    [MemberData(nameof(RefusalNames))]
    public void A_folder_the_filesystem_will_not_remove_answers_false(string refusalName)
    {
        var refusal = NewRefusal(refusalName);
        _directory.When(d => d.Delete("X")).Do(_ => throw refusal);

        Assert.False(_guard.TryRemoveEmptyFolder("X"));
    }

    [Fact]
    public void A_folder_that_takes_a_file_and_lets_it_be_read_back_passes_the_probe()
    {
        string? written = null;
        byte[]? payload = null;
        _file.When(f => f.WriteAllBytes(Arg.Any<string>(), Arg.Any<byte[]>()))
            .Do(call =>
            {
                written = call.ArgAt<string>(0);
                payload = call.ArgAt<byte[]>(1);
            });

        Assert.True(_guard.TryProbeWritable(_folder, out var failureCode, out var readBackRefused));

        Assert.Equal(0, failureCode);
        Assert.False(readBackRefused);
        Assert.NotNull(written);
        Assert.Equal(_folder, Path.GetDirectoryName(written));
        Assert.NotNull(payload);
        Assert.Empty(payload);
        Received.InOrder(() =>
        {
            _file.WriteAllBytes(written!, Arg.Any<byte[]>());
            _file.ReadAllBytes(written!);
            _file.Delete(written!);
        });
    }

    [Theory]
    [MemberData(nameof(RefusalNames))]
    public void A_folder_that_refuses_the_probe_file_fails_the_probe_with_the_refusals_HResult(string refusalName)
    {
        var refusal = NewRefusal(refusalName);
        _file.When(f => f.WriteAllBytes(Arg.Any<string>(), Arg.Any<byte[]>())).Do(_ => throw refusal);

        Assert.False(_guard.TryProbeWritable(_folder, out var failureCode, out var readBackRefused));

        Assert.Equal(refusal.HResult, failureCode);
        Assert.False(readBackRefused);
        _file.DidNotReceive().ReadAllBytes(Arg.Any<string>());
        _file.DidNotReceive().Delete(Arg.Any<string>());
    }

    [Theory]
    [MemberData(nameof(RefusalNames))]
    public void A_folder_that_will_not_let_the_probe_file_be_read_back_fails_the_probe_and_still_deletes_it(string refusalName)
    {
        var refusal = NewRefusal(refusalName);
        _file.ReadAllBytes(Arg.Any<string>()).Returns(_ => throw refusal);

        Assert.False(_guard.TryProbeWritable(_folder, out var failureCode, out var readBackRefused));

        Assert.Equal(refusal.HResult, failureCode);
        Assert.True(readBackRefused);
        _file.Received(1).Delete(Arg.Any<string>());
    }

    [Theory]
    [MemberData(nameof(RefusalNames))]
    public void A_folder_whose_probe_file_cannot_be_deleted_passes_the_probe(string refusalName)
    {
        var refusal = NewRefusal(refusalName);
        _file.When(f => f.Delete(Arg.Any<string>())).Do(_ => throw refusal);

        Assert.True(_guard.TryProbeWritable(_folder, out var failureCode, out var readBackRefused));

        Assert.Equal(0, failureCode);
        Assert.False(readBackRefused);
        _file.Received(1).Delete(Arg.Any<string>());
    }

    [Fact]
    public void A_copy_that_is_not_there_is_reported_gone_and_nothing_is_deleted()
    {
        _file.Exists(_copy).Returns(false);

        Assert.True(_guard.TryDiscardCopy(_copy, out var failureCode));

        Assert.Equal(0, failureCode);
        _file.Received(1).Exists(_copy);
        _file.DidNotReceive().Delete(Arg.Any<string>());
        _file.DidNotReceive().SetAttributes(Arg.Any<string>(), Arg.Any<FileAttributes>());
    }

    [Fact]
    public void A_copy_that_is_there_is_deleted_without_touching_its_attributes()
    {
        _file.Exists(_copy).Returns(true);
        _file.GetAttributes(_copy).Returns(FileAttributes.Archive);

        Assert.True(_guard.TryDiscardCopy(_copy, out var failureCode));

        Assert.Equal(0, failureCode);
        _file.Received(1).Delete(_copy);
        _file.DidNotReceive().SetAttributes(Arg.Any<string>(), Arg.Any<FileAttributes>());
    }

    [Fact]
    public void A_read_only_copy_has_the_attribute_cleared_and_then_is_deleted()
    {
        _file.Exists(_copy).Returns(true);
        _file.GetAttributes(_copy).Returns(FileAttributes.ReadOnly | FileAttributes.Archive);

        Assert.True(_guard.TryDiscardCopy(_copy, out _));

        Received.InOrder(() =>
        {
            _file.SetAttributes(_copy, FileAttributes.Archive);
            _file.Delete(_copy);
        });
    }

    [Theory]
    [MemberData(nameof(RefusalNames))]
    public void A_read_only_copy_whose_attribute_will_not_clear_fails_the_discard_and_is_not_deleted(string refusalName)
    {
        var refusal = NewRefusal(refusalName);
        _file.Exists(_copy).Returns(true);
        _file.GetAttributes(_copy).Returns(FileAttributes.ReadOnly);
        _file.When(f => f.SetAttributes(_copy, Arg.Any<FileAttributes>())).Do(_ => throw refusal);

        Assert.False(_guard.TryDiscardCopy(_copy, out var failureCode));

        Assert.Equal(refusal.HResult, failureCode);
        _file.DidNotReceive().Delete(Arg.Any<string>());
    }

    [Fact]
    public void A_copy_whose_attributes_cannot_be_read_fails_the_discard_and_is_not_deleted()
    {
        var refusal = new UnauthorizedAccessException("refused");
        _file.Exists(_copy).Returns(true);
        _file.GetAttributes(_copy).Returns(_ => throw refusal);

        Assert.False(_guard.TryDiscardCopy(_copy, out var failureCode));

        Assert.Equal(refusal.HResult, failureCode);
        _file.DidNotReceive().Delete(Arg.Any<string>());
    }

    [Fact]
    public void A_copy_whose_presence_cannot_be_asked_fails_the_discard_and_is_not_deleted()
    {
        var refusal = new IOException("refused");
        _file.Exists(_copy).Returns(_ => throw refusal);

        Assert.False(_guard.TryDiscardCopy(_copy, out var failureCode));

        Assert.Equal(refusal.HResult, failureCode);
        _file.DidNotReceive().Delete(Arg.Any<string>());
    }

    [Theory]
    [MemberData(nameof(RefusalNames))]
    public void A_copy_the_filesystem_will_not_delete_fails_the_discard_with_the_refusals_HResult(string refusalName)
    {
        var refusal = NewRefusal(refusalName);
        _file.Exists(_copy).Returns(true);
        _file.GetAttributes(_copy).Returns(FileAttributes.Archive);
        _file.When(f => f.Delete(_copy)).Do(_ => throw refusal);

        Assert.False(_guard.TryDiscardCopy(_copy, out var failureCode));

        Assert.Equal(refusal.HResult, failureCode);
    }
}
