using InstallerClean.Interop.Native;

namespace InstallerClean.Services;

/// <summary>
/// A rename or delete that met a sharing violation, another process holding the file
/// open, on every try for as long as it was tried, with what the Restart Manager says
/// holds the file, for crash.log. The violation the last try met is the inner exception,
/// and its HResult is this one's.
/// </summary>
internal sealed class FileHeldOpenException : IOException
{
    /// <summary>
    /// The exception for <paramref name="path"/>, asked for the moment the last try fails,
    /// while the file is still held. Its message names the processes the Restart Manager
    /// says hold the file open, this one left out, by name and process id. Where the
    /// Restart Manager cannot answer, the message says so. Never throws.
    /// </summary>
    internal FileHeldOpenException(string path, IOException inner)
        : base(Holders(path), inner)
    {
        HResult = inner.HResult;
    }

    private const string NotNamed = "The Restart Manager could not say what holds the file open.";

    private static unsafe string Holders(string path)
    {
        try
        {
            var key = stackalloc char[Rstrtmgr.SessionKeyLength + 1];
            if (Rstrtmgr.RmStartSession(out var session, 0, key) != Rstrtmgr.ERROR_SUCCESS) return NotNamed;
            try
            {
                return Listed(session, path) ?? NotNamed;
            }
            finally
            {
                Rstrtmgr.RmEndSession(session);
            }
        }
        catch (Exception)
        {
            return NotNamed;
        }
    }

    /// <summary>
    /// The holders of <paramref name="path"/> in <paramref name="session"/>, as a sentence,
    /// or null where the Restart Manager refuses either call.
    /// </summary>
    private static unsafe string? Listed(uint session, string path)
    {
        fixed (char* name = path)
        {
            var names = name;
            if (Rstrtmgr.RmRegisterResources(session, 1, &names, 0, null, 0, null) != Rstrtmgr.ERROR_SUCCESS)
                return null;
        }

        uint count = 0;
        var result = Rstrtmgr.RmGetList(session, out var needed, ref count, null, out _);
        var processes = Array.Empty<Rstrtmgr.RM_PROCESS_INFO>();
        // A process that opens the file between two calls makes the list longer than the
        // room given for it, so the call is made again with room for the new length.
        for (var tries = 0; result == Kernel32.ERROR_MORE_DATA && tries < 3; tries++)
        {
            processes = new Rstrtmgr.RM_PROCESS_INFO[needed];
            count = needed;
            fixed (Rstrtmgr.RM_PROCESS_INFO* list = processes)
                result = Rstrtmgr.RmGetList(session, out needed, ref count, list, out _);
        }
        if (result != Rstrtmgr.ERROR_SUCCESS) return null;

        var self = (uint)Environment.ProcessId;
        var named = new List<string>();
        fixed (Rstrtmgr.RM_PROCESS_INFO* list = processes)
        {
            for (var i = 0; i < count && i < processes.Length; i++)
            {
                if (list[i].ProcessId == self) continue;
                var app = Text(list[i].AppName, Rstrtmgr.MaxAppNameLength + 1);
                var service = Text(list[i].ServiceShortName, Rstrtmgr.MaxServiceNameLength + 1);
                named.Add(service.Length > 0
                    ? $"{app} (process {list[i].ProcessId}, service {service})"
                    : $"{app} (process {list[i].ProcessId})");
            }
        }
        return named.Count == 0
            ? "The Restart Manager names nothing else holding the file open."
            : "The Restart Manager names what holds the file open: " + string.Join("; ", named) + ".";
    }

    /// <summary>The text in a fixed buffer of <paramref name="capacity"/> characters, up to its terminator.</summary>
    private static unsafe string Text(char* buffer, int capacity)
    {
        var text = new ReadOnlySpan<char>(buffer, capacity);
        var end = text.IndexOf('\0');
        return new string(end < 0 ? text : text[..end]);
    }
}
