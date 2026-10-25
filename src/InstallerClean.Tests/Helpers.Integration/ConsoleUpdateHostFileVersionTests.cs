using InstallerClean.Cli;
using InstallerClean.Helpers;
using InstallerClean.Interop.Native;

namespace InstallerClean.Tests.Helpers.Integration;

/// <summary>
/// The command line's read of the version a file carries, on a real file: a file another
/// process holds sharing nothing for reading throws, so the update can wait for it, where
/// a missing file answers no version.
/// </summary>
public sealed class ConsoleUpdateHostFileVersionTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"ic-hostversion-{Guid.NewGuid():N}");

    public ConsoleUpdateHostFileVersionTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void A_file_held_open_sharing_nothing_throws_a_sharing_violation_rather_than_answer_no_version()
    {
        var copy = Path.Combine(_folder, "held.dll");
        File.Copy(Path.Combine(Environment.SystemDirectory, "kernel32.dll"), copy);
        var host = new ConsoleUpdateHost("/s");
        Assert.NotNull(host.FileVersion(copy));
        using var held = new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.None);

        var thrown = Assert.ThrowsAny<IOException>(() => host.FileVersion(copy));
        Assert.Equal(Kernel32.ERROR_SHARING_VIOLATION, WindowsErrors.Of(thrown));
    }

    [Fact]
    public void A_missing_file_answers_no_version() =>
        Assert.Null(new ConsoleUpdateHost("/s").FileVersion(Path.Combine(_folder, "absent.exe")));
}
