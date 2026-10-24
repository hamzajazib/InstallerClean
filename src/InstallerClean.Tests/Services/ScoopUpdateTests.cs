using InstallerClean.Services;

namespace InstallerClean.Tests.Services;

/// <summary>
/// The command that updates a copy Scoop installed: the app's name read from the folder
/// the copy runs from, and <c>--global</c> after it where that folder is in Scoop's global
/// root and in no other case.
/// </summary>
public class ScoopUpdateTests
{
    private const string UserProfile = @"C:\Users\someone";
    private const string ProgramData = @"C:\ProgramData";
    private const string PerUserFolder = @"C:\Users\someone\scoop\apps\installerclean\3.4.0";
    private const string DefaultGlobalFolder = @"C:\ProgramData\scoop\apps\installerclean\3.4.0";

    private static Func<string, string?> Env(params (string Name, string Value)[] set)
    {
        var map = set.ToDictionary(s => s.Name, s => s.Value, StringComparer.OrdinalIgnoreCase);
        return name => map.TryGetValue(name, out var value) ? value : null;
    }

    private static readonly Func<string, string?> NoConfig = _ => null;

    private static Func<string, string?> ConfigAt(string path, string text) =>
        asked => string.Equals(asked, path, StringComparison.OrdinalIgnoreCase) ? text : null;

    private static ScoopUpdate? Read(
        string folder, Func<string, string?>? environment = null, Func<string, string?>? readConfig = null) =>
        AppCopy.ReadScoopUpdate(
            folder, environment ?? Env(("USERPROFILE", UserProfile)), readConfig ?? NoConfig, ProgramData);

    [Theory]
    [InlineData(PerUserFolder)]
    [InlineData(@"C:\Users\someone\scoop\apps\installerclean\current")]
    [InlineData(@"C:\Users\someone\scoop\apps\installerclean\3.4.0\")]
    [InlineData(@"D:\tools\scoop\Apps\installerclean\3.4.0")]
    public void A_copy_in_a_users_Scoop_root_updates_with_the_plain_command(string folder)
    {
        var update = Read(folder);

        Assert.NotNull(update);
        Assert.Equal("scoop update installerclean", update.Command);
        Assert.False(update.Global);
    }

    [Fact]
    public void The_command_names_the_app_by_the_folder_Scoop_gave_it()
    {
        Assert.Equal("scoop update installerclean-test",
            Read(@"C:\Users\someone\scoop\apps\installerclean-test\3.4.0")!.Command);
    }

    [Fact]
    public void A_copy_in_the_default_global_root_updates_with_the_global_command()
    {
        var update = Read(DefaultGlobalFolder);

        Assert.NotNull(update);
        Assert.Equal("scoop update installerclean --global", update.Command);
        Assert.True(update.Global);
    }

    [Theory]
    [InlineData(@"d:\shared\SCOOP")]
    [InlineData(@"D:\Shared\Scoop\")]
    public void SCOOP_GLOBAL_names_the_global_root(string variable)
    {
        var update = Read(@"D:\Shared\Scoop\apps\installerclean\3.4.0",
            Env(("USERPROFILE", UserProfile), ("SCOOP_GLOBAL", variable)));

        Assert.True(update!.Global);
        Assert.Equal("scoop update installerclean --global", update.Command);
    }

    [Fact]
    public void The_global_path_setting_names_the_global_root_when_SCOOP_GLOBAL_is_not_set()
    {
        var config = ConfigAt(@"C:\Users\someone\.config\scoop\config.json",
            "{ \"root_path\": \"C:\\\\Users\\\\someone\\\\scoop\", \"Global_Path\": \"E:\\\\Everyone\\\\Scoop\" }");

        Assert.True(Read(@"E:\Everyone\Scoop\apps\installerclean\3.4.0", readConfig: config)!.Global);
        Assert.False(Read(DefaultGlobalFolder, readConfig: config)!.Global);
    }

    [Fact]
    public void The_settings_file_is_read_from_XDG_CONFIG_HOME_where_it_is_set()
    {
        var asked = new List<string>();
        Func<string, string?> config = path => { asked.Add(path); return null; };

        Read(PerUserFolder, Env(("USERPROFILE", UserProfile), ("XDG_CONFIG_HOME", @"D:\cfg")), config);
        Read(PerUserFolder, readConfig: config);

        Assert.Equal([@"D:\cfg\scoop\config.json", @"C:\Users\someone\.config\scoop\config.json"], asked);
    }

    [Fact]
    public void SCOOP_GLOBAL_wins_over_the_global_path_setting_and_the_setting_over_the_default()
    {
        var config = ConfigAt(@"C:\Users\someone\.config\scoop\config.json",
            "{ \"global_path\": \"E:\\\\FromConfig\" }");
        var both = Env(("USERPROFILE", UserProfile), ("SCOOP_GLOBAL", @"F:\FromVariable"));

        Assert.True(Read(@"F:\FromVariable\apps\installerclean\3.4.0", both, config)!.Global);
        Assert.False(Read(@"E:\FromConfig\apps\installerclean\3.4.0", both, config)!.Global);
        Assert.False(Read(DefaultGlobalFolder, readConfig: config)!.Global);
    }

    [Fact]
    public void A_root_that_is_not_Scoops_global_root_is_a_plain_update_whatever_else_it_lacks()
    {
        // The app's own root has no apps\scoop beside it, and still is not global.
        var update = Read(@"D:\tools\scoop\apps\installerclean\3.4.0");

        Assert.False(update!.Global);
        Assert.Equal("scoop update installerclean", update.Command);
    }

    [Fact]
    public void A_global_path_setting_that_is_null_or_empty_is_not_set()
    {
        foreach (var value in new[] { "null", "\"\"" })
        {
            var config = ConfigAt(@"C:\Users\someone\.config\scoop\config.json", $"{{ \"global_path\": {value} }}");

            Assert.True(Read(DefaultGlobalFolder, readConfig: config)!.Global, value);
        }
    }

    [Fact]
    public void A_global_root_that_is_not_established_gives_the_plain_command()
    {
        // Each of these leaves the global root unknown, so a copy in the default
        // global folder is not called global.
        var unknown = new (string Case, Func<string, string?> Environment, Func<string, string?> Config)[]
        {
            ("relative SCOOP_GLOBAL", Env(("USERPROFILE", UserProfile), ("SCOOP_GLOBAL", @"scoop\global")), NoConfig),
            ("no config home", Env(), NoConfig),
            ("unreadable settings file", Env(("USERPROFILE", UserProfile)),
                _ => throw new UnauthorizedAccessException()),
            ("settings file that is not JSON", Env(("USERPROFILE", UserProfile)),
                ConfigAt(@"C:\Users\someone\.config\scoop\config.json", "{ not json")),
            ("settings file that is not an object", Env(("USERPROFILE", UserProfile)),
                ConfigAt(@"C:\Users\someone\.config\scoop\config.json", "[1, 2]")),
            ("global_path that is not a string", Env(("USERPROFILE", UserProfile)),
                ConfigAt(@"C:\Users\someone\.config\scoop\config.json", "{ \"global_path\": 7 }")),
            ("relative global_path", Env(("USERPROFILE", UserProfile)),
                ConfigAt(@"C:\Users\someone\.config\scoop\config.json", "{ \"global_path\": \"shared\" }")),
        };

        foreach (var (name, environment, config) in unknown)
        {
            var update = Read(DefaultGlobalFolder, environment, config);
            Assert.True(update is { Global: false }, name);
            Assert.Equal("scoop update installerclean", update!.Command);
        }
    }

    [Theory]
    [InlineData(@"C:\Program Files\InstallerClean")]
    [InlineData(@"C:\Users\someone\scoop\installerclean\3.4.0")]
    [InlineData(@"C:\Users\someone\scoop\apps\3.4.0")]
    [InlineData(@"C:\apps\3.4.0")]
    [InlineData(@"C:\")]
    public void A_folder_that_is_not_root_apps_name_version_names_no_app(string folder)
    {
        Assert.Null(Read(folder));
    }

    [Theory]
    [InlineData("installer clean")]
    [InlineData("installerclean&calc")]
    [InlineData("installerclean;calc")]
    [InlineData("$(calc)")]
    [InlineData("-installerclean")]
    public void A_folder_name_Scoop_does_not_give_an_app_is_not_put_in_a_command(string name)
    {
        Assert.Null(Read($@"C:\Users\someone\scoop\apps\{name}\3.4.0"));
    }

    [Fact]
    public void A_Scoop_copy_carries_the_update_command_for_the_folder_it_runs_from_and_no_other_kind_does()
    {
        var scoopExe = Path.Combine(PerUserFolder, "InstallerClean.exe");
        AppUpdater Updater(AppCopyKind kind, string exe) => new(
            kind, exe, new UpdateDownloadService(), Path.GetTempPath(), AppUpdater.WindowSetupArguments);

        var scoop = Updater(AppCopyKind.Scoop, scoopExe);
        var elsewhere = Updater(AppCopyKind.Scoop, @"C:\Users\someone\Downloads\InstallerClean.exe");

        Assert.Equal("scoop update installerclean", scoop.ScoopUpdate?.Command);
        Assert.False(scoop.ScoopUpdate!.Global);
        Assert.Null(elsewhere.ScoopUpdate);
        foreach (var kind in Enum.GetValues<AppCopyKind>().Where(k => k != AppCopyKind.Scoop))
            Assert.Null(Updater(kind, scoopExe).ScoopUpdate);
    }

    [Fact]
    public void The_default_update_is_the_plain_command_for_InstallerClean()
    {
        Assert.Equal("scoop update installerclean", AppCopy.DefaultScoopUpdate.Command);
        Assert.False(AppCopy.DefaultScoopUpdate.Global);
        Assert.Equal(AppCopy.ScoopUpdateCommand, AppCopy.DefaultScoopUpdate.Command);
    }
}
