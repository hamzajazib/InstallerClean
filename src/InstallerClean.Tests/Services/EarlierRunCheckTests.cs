using InstallerClean.Models;
using InstallerClean.Services;
using NSubstitute;

namespace InstallerClean.Tests.Services;

/// <summary>
/// <see cref="EarlierRunCheck"/> with every source it reads faked: the PC-wide mark,
/// this account's settings and <c>last-run.json</c>, and the command line's record in
/// the Application log. Each test fixes what every source would answer, so a source the
/// check should not have reached is one it can be shown not to have read.
/// </summary>
public class EarlierRunCheckTests
{
    private readonly IFirstRunMark _mark = Substitute.For<IFirstRunMark>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IResultLogService _resultLog = Substitute.For<IResultLogService>();
    private readonly ICommandLineRunRecord _commandLine = Substitute.For<ICommandLineRunRecord>();

    public EarlierRunCheckTests()
    {
        _mark.Read().Returns(FirstRunMarkState.NotSet);
        _settings.Load().Returns(new AppSettings());
        _resultLog.LastLogExists().Returns(false);
        _commandLine.Read().Returns(CommandLineRunRecordState.NothingActedOn);
    }

    private EarlierRunCheck Check(bool skipApplicationLog = false) =>
        new(_mark, _settings, _resultLog, _commandLine, skipApplicationLog);

    [Fact]
    public async Task A_mark_already_set_answers_yes_and_nothing_else_is_read()
    {
        _mark.Read().Returns(FirstRunMarkState.Set);

        Assert.True(await Check().ShowsAnEarlierRunAsync());
        _settings.DidNotReceive().Load();
        _resultLog.DidNotReceive().LastLogExists();
        _commandLine.DidNotReceive().Read();
        _mark.DidNotReceive().Set();
    }

    [Fact]
    public async Task A_mark_that_cannot_be_read_answers_yes_and_is_not_written()
    {
        _mark.Read().Returns(FirstRunMarkState.Unreadable);

        Assert.True(await Check().ShowsAnEarlierRunAsync());
        _commandLine.DidNotReceive().Read();
        _mark.DidNotReceive().Set();
    }

    [Fact]
    public async Task A_report_this_account_already_sent_answers_yes_and_sets_the_mark()
    {
        _settings.Load().Returns(new AppSettings { HasSentResultLog = true });

        Assert.True(await Check().ShowsAnEarlierRunAsync());
        _mark.Received(1).Set();
        _commandLine.DidNotReceive().Read();
    }

    [Fact]
    public async Task A_last_run_file_in_this_account_answers_yes_and_sets_the_mark()
    {
        _resultLog.LastLogExists().Returns(true);

        Assert.True(await Check().ShowsAnEarlierRunAsync());
        _mark.Received(1).Set();
        _commandLine.DidNotReceive().Read();
    }

    [Fact]
    public async Task A_command_line_run_that_acted_on_files_answers_yes_and_sets_the_mark()
    {
        _commandLine.Read().Returns(CommandLineRunRecordState.ActedOnFiles);

        Assert.True(await Check().ShowsAnEarlierRunAsync());
        _mark.Received(1).Set();
    }

    [Fact]
    public async Task A_log_that_cannot_be_read_answers_yes_and_sets_the_mark()
    {
        _commandLine.Read().Returns(CommandLineRunRecordState.Unreadable);

        Assert.True(await Check().ShowsAnEarlierRunAsync());
        _mark.Received(1).Set();
    }

    [Fact]
    public async Task No_earlier_run_anywhere_answers_no_and_leaves_the_mark()
    {
        // The control for the four above: every source read, none answering.
        Assert.False(await Check().ShowsAnEarlierRunAsync());
        _settings.Received(1).Load();
        _resultLog.Received(1).LastLogExists();
        _commandLine.Received(1).Read();
        _mark.DidNotReceive().Set();
    }

    [Fact]
    public async Task With_the_log_skipped_a_command_line_run_is_not_read()
    {
        _commandLine.Read().Returns(CommandLineRunRecordState.ActedOnFiles);

        Assert.False(await Check(skipApplicationLog: true).ShowsAnEarlierRunAsync());
        _commandLine.DidNotReceive().Read();
        _mark.DidNotReceive().Set();
    }

    [Fact]
    public async Task With_the_log_skipped_this_account_s_own_files_still_answer()
    {
        _resultLog.LastLogExists().Returns(true);

        Assert.True(await Check(skipApplicationLog: true).ShowsAnEarlierRunAsync());
        _mark.Received(1).Set();
    }

    [Fact]
    public async Task A_source_that_throws_answers_yes_and_sets_the_mark()
    {
        _resultLog.LastLogExists().Returns(_ => throw new InvalidOperationException("planted"));

        Assert.True(await Check().ShowsAnEarlierRunAsync());
        _mark.Received(1).Set();
    }

    [Fact]
    public async Task Every_ask_shares_one_check()
    {
        var check = Check();

        var first = check.ShowsAnEarlierRunAsync();
        var second = check.ShowsAnEarlierRunAsync();
        await first;

        Assert.Same(first, second);
        Assert.False(await check.ShowsAnEarlierRunWithinAsync(TimeSpan.FromSeconds(30)));
        _mark.Received(1).Read();
        _commandLine.Received(1).Read();
    }

    [Fact]
    public async Task A_check_that_answers_within_the_bound_keeps_its_own_answer()
    {
        Assert.False(await Check().ShowsAnEarlierRunWithinAsync(TimeSpan.FromSeconds(30)));
        _mark.DidNotReceive().Set();
    }

    [Fact]
    public async Task A_check_still_reading_when_the_bound_passes_answers_yes_and_sets_the_mark()
    {
        using var release = new ManualResetEventSlim(false);
        _commandLine.Read().Returns(_ =>
        {
            release.Wait(TimeSpan.FromSeconds(30));
            return CommandLineRunRecordState.NothingActedOn;
        });
        var check = Check();

        try
        {
            Assert.True(await check.ShowsAnEarlierRunWithinAsync(TimeSpan.FromMilliseconds(100)));
            _mark.Received(1).Set();
        }
        finally
        {
            release.Set();
        }

        // The read the bound gave up on still finishes with its own answer.
        Assert.False(await check.ShowsAnEarlierRunAsync());
    }

#if DEBUG
    [Fact]
    public async Task In_a_debug_build_the_environment_variable_skips_the_log()
    {
        _commandLine.Read().Returns(CommandLineRunRecordState.ActedOnFiles);
        var before = Environment.GetEnvironmentVariable(EarlierRunCheck.SkipApplicationLogVariable);
        try
        {
            Environment.SetEnvironmentVariable(EarlierRunCheck.SkipApplicationLogVariable, "1");
            var skipped = new EarlierRunCheck(_mark, _settings, _resultLog, _commandLine);
            Assert.False(await skipped.ShowsAnEarlierRunAsync());
            _commandLine.DidNotReceive().Read();

            // The control: the same check with the variable cleared reads the log.
            Environment.SetEnvironmentVariable(EarlierRunCheck.SkipApplicationLogVariable, null);
            var reading = new EarlierRunCheck(_mark, _settings, _resultLog, _commandLine);
            Assert.True(await reading.ShowsAnEarlierRunAsync());
            _commandLine.Received(1).Read();
        }
        finally
        {
            Environment.SetEnvironmentVariable(EarlierRunCheck.SkipApplicationLogVariable, before);
        }
    }
#endif
}
