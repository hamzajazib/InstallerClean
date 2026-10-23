using InstallerClean.Cli;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The command line's read of the answer to its update question: a cancel ends the wait
/// with the read still waiting, as a console being closed leaves it, and the next read in the
/// process waits on that same read rather than start another beside it.
/// </summary>
public sealed class CliUpdateQuestionReadTests : IDisposable
{
    private readonly TextReader _previousIn = Console.In;
    private readonly HeldReader _reader = new();

    public CliUpdateQuestionReadTests()
    {
        ConsoleUpdateHost.ForgetPendingRead();
        Console.SetIn(_reader);
    }

    public void Dispose()
    {
        _reader.Release(null);
        ConsoleUpdateHost.ForgetPendingRead();
        Console.SetIn(_previousIn);
    }

    /// <summary>A reader whose ReadLine waits until the test gives it a line, and counts its reads.</summary>
    private sealed class HeldReader : TextReader
    {
        private readonly SemaphoreSlim _line = new(0);
        private string? _next;
        private int _reads;
        public int Reads => Volatile.Read(ref _reads);
        public readonly ManualResetEventSlim Entered = new();

        public void Release(string? line)
        {
            _next = line;
            _line.Release();
        }

        public override string? ReadLine()
        {
            Interlocked.Increment(ref _reads);
            Entered.Set();
            _line.Wait(TimeSpan.FromSeconds(30));
            return _next;
        }
    }

    [Fact]
    public void A_cancel_ends_the_wait_with_the_read_still_waiting()
    {
        var host = new ConsoleUpdateHost("/s");
        using var cancel = new CancellationTokenSource();
        // Cancelled once the read is waiting inside the console's ReadLine.
        _ = Task.Run(() => { _reader.Entered.Wait(TimeSpan.FromSeconds(30)); cancel.Cancel(); });

        var started = DateTime.UtcNow;
        Assert.Null(host.ReadLine(cancel.Token));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
        Assert.Equal(1, _reader.Reads);
    }

    [Fact]
    public void A_read_after_a_cancel_waits_on_the_same_read_and_gets_its_line()
    {
        using (var cancel = new CancellationTokenSource())
        {
            _ = Task.Run(() => { _reader.Entered.Wait(TimeSpan.FromSeconds(30)); cancel.Cancel(); });
            Assert.Null(new ConsoleUpdateHost("/s").ReadLine(cancel.Token));
        }

        _reader.Release("n");

        // A later run in the same process, with a host of its own, waits on the same read.
        Assert.Equal("n", new ConsoleUpdateHost("/s").ReadLine(CancellationToken.None));
        Assert.Equal(1, _reader.Reads);
    }

    [Fact]
    public void A_line_answered_before_any_cancel_is_the_answer()
    {
        var host = new ConsoleUpdateHost("/s");
        _reader.Release("y");

        Assert.Equal("y", host.ReadLine(CancellationToken.None));
    }
}
