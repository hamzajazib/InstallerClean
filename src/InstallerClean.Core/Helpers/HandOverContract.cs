namespace InstallerClean.Helpers;

/// <summary>
/// What a copy that updates itself passes the newer version it starts, and what every
/// version from <see cref="FirstVersionTakingHandOverSwitches"/> on takes in return: the
/// window and the command line alike. The newer version says through
/// <see cref="StartedEventSwitch"/> that its own code is running, and the copy that
/// started it waits for that before it ends (<see cref="Services.SelfReplacement"/>).
/// </summary>
/// <remarks>
/// Every version from <see cref="FirstVersionTakingHandOverSwitches"/> on passes
/// <see cref="StartedEventSwitch"/> to whichever version it updates to and puts itself
/// back where that version does not set the event in time. So every later version has to
/// go on taking the switch and setting the event, or every update to that version from
/// one of these is put back and says it could not be installed. A switch added to the
/// hand-over later is one an earlier version does not know, so it needs a first version
/// of its own.
/// </remarks>
internal static class HandOverContract
{
    /// <summary>
    /// The first version that takes the switches a copy updating itself adds to the
    /// command it starts the newer version with: <see cref="StartedEventSwitch"/>, and
    /// the command line's <see cref="CliContract.CancelEventSwitch"/>. A command line
    /// before it refuses either as an argument it does not know and runs nothing, and a
    /// window before it never sets the event, so neither switch is passed to one.
    /// </summary>
    internal static readonly Version FirstVersionTakingHandOverSwitches = new(3, 5, 0);

    /// <summary>
    /// Whether <paramref name="version"/> is <see cref="FirstVersionTakingHandOverSwitches"/>
    /// or later, its first three parts compared. A version that does not parse answers false.
    /// </summary>
    internal static bool TakesHandOverSwitches(string version) =>
        Version.TryParse(version, out var parsed)
        && Services.UpdateCheckService.NormaliseToBuild(parsed) >= FirstVersionTakingHandOverSwitches;

    /// <summary>
    /// The switch through which the copy that started this one passes, after the colon,
    /// the id of an event this one sets once its own code is running. Only a copy updating
    /// itself passes it, so it is not in the help.
    /// </summary>
    internal const string StartedEventSwitch = "/startedevent:";

    /// <summary>
    /// How long the copy that started the newer version waits for it to set the event
    /// before it ends the newer version and puts itself back. The wait ends at once where
    /// the newer version ends, so only a newer version that hangs reaches it. A first start
    /// is the slowest: a self-contained build writes out its native libraries the first
    /// time it runs, and an antivirus looks at a program it has not seen before. A bound
    /// a healthy start can reach fails the update on every attempt on that PC.
    /// </summary>
    internal static readonly TimeSpan StartBound = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The name of the event <paramref name="id"/> stands for, in the session's own
    /// namespace, which the newer version shares, being started by the copy that made it.
    /// </summary>
    internal static string StartedEventName(Guid id) => @"Local\InstallerClean_Started_" + id.ToString("N");

    /// <summary>The argument passing <paramref name="id"/> with <see cref="StartedEventSwitch"/>.</summary>
    internal static string StartedEventArgument(Guid id) => StartedEventSwitch + id.ToString("N");

    /// <summary>
    /// Creates the event <paramref name="id"/> names, unset, or answers null where it
    /// cannot, as <see cref="CreateEvent"/> does.
    /// </summary>
    internal static EventWaitHandle? CreateStartedEvent(Guid id) => CreateEvent(StartedEventName(id));

    /// <summary>
    /// Creates a manual-reset event named <paramref name="name"/>, unset, for one of the
    /// hand-over switches, or answers null where it cannot or where an event of that name
    /// exists already, which this process did not make. Each is written to crash.log.
    /// </summary>
    internal static EventWaitHandle? CreateEvent(string name)
    {
        try
        {
            var created = new EventWaitHandle(false, EventResetMode.ManualReset, name, out var createdNew);
            if (createdNew) return created;
            created.Dispose();
            throw new InvalidOperationException($"An event named {name} exists already.");
        }
        catch (Exception ex)
        {
            CrashLog.TryWrite(ex);
            return null;
        }
    }

    /// <summary>
    /// Takes every <paramref name="switchName"/> carrying an event's id out of
    /// <paramref name="args"/>, wherever it stands and in any case, and answers what is
    /// left. <paramref name="id"/> is the first one's id, or null where there is none. One
    /// carrying anything else is left in.
    /// </summary>
    internal static string[] TakeEventSwitch(string[] args, string switchName, out string? id)
    {
        id = null;
        var rest = new List<string>(args.Length);
        foreach (var arg in args)
        {
            if (arg.StartsWith(switchName, StringComparison.OrdinalIgnoreCase)
                && Guid.TryParseExact(arg[switchName.Length..], "N", out _))
                id ??= arg[switchName.Length..];
            else
                rest.Add(arg);
        }
        return rest.ToArray();
    }

    /// <summary>
    /// Takes every <see cref="StartedEventSwitch"/> carrying an event's id out of
    /// <paramref name="args"/>, as <see cref="TakeEventSwitch"/> does.
    /// </summary>
    internal static string[] TakeStartedEvent(string[] args, out string? startedEvent) =>
        TakeEventSwitch(args, StartedEventSwitch, out startedEvent);

    /// <summary>
    /// Sets the event <paramref name="id"/> stands for, telling the copy that started this
    /// one that its own code is running. Does nothing where <paramref name="id"/> is null.
    /// An event this process cannot open or set is written to crash.log; the copy that
    /// started it then ends it once <see cref="StartBound"/> has passed and puts itself
    /// back. Never throws.
    /// </summary>
    internal static void SignalStarted(string? id)
    {
        if (id is null) return;
        try
        {
            if (!Guid.TryParseExact(id, "N", out var parsed))
                throw new FormatException("The value passed with the started-event switch is not an event's id.");
            if (!EventWaitHandle.TryOpenExisting(StartedEventName(parsed), out var started))
                throw new InvalidOperationException("The event passed with the started-event switch does not exist.");
            using (started) started.Set();
        }
        catch (Exception ex)
        {
            CrashLog.TryWrite(ex);
        }
    }
}
