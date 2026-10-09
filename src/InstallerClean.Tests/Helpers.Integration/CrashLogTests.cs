using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers.Integration;

public class CrashLogTests
{
    [Fact]
    public void The_suite_writes_the_log_under_the_temp_folder()
    {
        // TestCrashLog sets this before any test runs, so no test writes into the log of
        // the PC running the suite.
        Assert.NotNull(CrashLog.FolderForTests);
        Assert.StartsWith(Path.GetTempPath(), CrashLog.LogPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_entry_is_written_to_the_folder_it_is_sent_to()
    {
        var folder = Path.Combine(Path.GetTempPath(), "InstallerClean.Tests", Guid.NewGuid().ToString("N"));
        var marker = Guid.NewGuid().ToString("N");
        var suite = CrashLog.FolderForTests;
        CrashLog.FolderForTests = folder;
        try
        {
            var (path, written) = CrashLog.TryWrite(new InvalidOperationException(marker));

            Assert.True(written);
            Assert.Equal(Path.Combine(folder, "crash.log"), path);
            Assert.Contains(marker, File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            CrashLog.FolderForTests = suite;
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Without_a_folder_set_the_log_is_in_the_app_s_own_folder()
    {
        // Read without writing, so the PC's own log is untouched.
        var suite = CrashLog.FolderForTests;
        CrashLog.FolderForTests = null;
        try
        {
            Assert.EndsWith(Path.Combine("NoFaff", "InstallerClean", "crash.log"), CrashLog.LogPath);
        }
        finally
        {
            CrashLog.FolderForTests = suite;
        }
    }

    [Fact]
    public void Write_never_throws_even_when_exception_serialisation_is_unusual()
    {
        var nested = new Exception("outer", new Exception("inner", new Exception("innermost")));

        var ex = Record.Exception(() => CrashLog.Write(nested));

        Assert.Null(ex);
    }
}
