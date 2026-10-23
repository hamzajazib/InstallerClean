using System.Diagnostics;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// Reads and closes the console window another process is attached to, through a Windows
/// PowerShell helper of its own: a process can be attached to one console only, so the test
/// process leaves its own alone and the helper detaches from its console and attaches to the
/// other.
/// </summary>
internal static class OtherConsole
{
    private static readonly string PowerShell =
        Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    // Windows PowerShell 5.1 compiles C# 5, so the type below stays at C# 5.
    private const string Script = """
        param([string]$Mode, [int]$ProcessId, [string]$Library)
        $ErrorActionPreference = 'Stop'
        if (Test-Path -LiteralPath $Library) { Add-Type -Path $Library; $compiled = $true } else { $compiled = $false }
        if (-not $compiled) {
        Add-Type -OutputAssembly $Library -TypeDefinition @'
        using System;
        using System.Runtime.InteropServices;
        using System.Text;
        public static class IcOtherConsole
        {
            [StructLayout(LayoutKind.Sequential)] struct Coord { public short X; public short Y; }
            [StructLayout(LayoutKind.Sequential)] struct SmallRect { public short Left; public short Top; public short Right; public short Bottom; }
            [StructLayout(LayoutKind.Sequential)] struct BufferInfo { public Coord Size; public Coord Cursor; public short Attributes; public SmallRect Window; public Coord MaxSize; }
            [DllImport("kernel32.dll", SetLastError = true)] static extern bool FreeConsole();
            [DllImport("kernel32.dll", SetLastError = true)] static extern bool AttachConsole(uint processId);
            [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
            [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
            [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetConsoleScreenBufferInfo(IntPtr output, out BufferInfo info);
            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool ReadConsoleOutputCharacterW(IntPtr output, StringBuilder text, uint length, Coord origin, out uint read);
            [DllImport("user32.dll", SetLastError = true)] static extern bool PostMessageW(IntPtr window, uint message, IntPtr w, IntPtr l);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr window, StringBuilder name, int size);

            public static string Run(string mode, int processId)
            {
                FreeConsole();
                if (!AttachConsole((uint)processId)) return "!attach " + Marshal.GetLastWin32Error();
                try
                {
                    if (mode == "window" || mode == "close")
                    {
                        IntPtr window = GetConsoleWindow();
                        if (window == IntPtr.Zero) return "!nowindow";
                        StringBuilder cls = new StringBuilder(256);
                        GetClassNameW(window, cls, 256);
                        if (mode == "window") return cls.ToString();
                        // Detached first, so the close this posts is not also sent to this helper.
                        FreeConsole();
                        // WM_CLOSE, as the window's Close button sends it.
                        return PostMessageW(window, 0x0010, IntPtr.Zero, IntPtr.Zero) ? cls.ToString() : "!post " + Marshal.GetLastWin32Error();
                    }
                    IntPtr output = CreateFileW("CONOUT$", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (output == new IntPtr(-1)) return "!conout " + Marshal.GetLastWin32Error();
                    try
                    {
                        BufferInfo info;
                        if (!GetConsoleScreenBufferInfo(output, out info)) return "!buffer " + Marshal.GetLastWin32Error();
                        StringBuilder all = new StringBuilder();
                        for (int row = 0; row <= info.Cursor.Y; row++)
                        {
                            StringBuilder line = new StringBuilder(info.Size.X);
                            Coord origin = new Coord();
                            origin.Y = (short)row;
                            uint read;
                            if (!ReadConsoleOutputCharacterW(output, line, (uint)info.Size.X, origin, out read)) break;
                            all.AppendLine(line.ToString(0, (int)read).TrimEnd());
                        }
                        return all.ToString();
                    }
                    finally { CloseHandle(output); }
                }
                finally { FreeConsole(); }
            }
        }
        '@
        Add-Type -Path $Library
        }
        [Console]::OutputEncoding = [Text.Encoding]::UTF8
        [Console]::Out.Write([IcOtherConsole]::Run($Mode, $ProcessId))
        """;

    /// <summary>
    /// The script and the library its first run compiles the type into, which later runs load
    /// rather than compile again, in a folder of this run's own.
    /// </summary>
    private static readonly Lazy<(string Script, string Library)> Files = new(() =>
    {
        var folder = Directory.CreateTempSubdirectory("ic-other-console-").FullName;
        var script = Path.Combine(folder, "OtherConsole.ps1");
        File.WriteAllText(script, Script);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Directory.Delete(folder, recursive: true); } catch { } };
        return (script, Path.Combine(folder, "OtherConsole.dll"));
    });

    /// <summary>
    /// What the helper answers for <paramref name="mode"/>: the text on the console's screen
    /// for "read", the console window's class for "window" and for "close" (which posts
    /// WM_CLOSE to it), or a line starting with "!" naming what failed: a Windows call, or
    /// the helper itself, where it ends with an error, answers nothing or does not end.
    /// </summary>
    private static string Run(string mode, int processId)
    {
        var info = new ProcessStartInfo(PowerShell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        var (script, library) = Files.Value;
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, mode, processId.ToString(), library })
            info.ArgumentList.Add(argument);
        using var helper = Process.Start(info)!;
        var output = helper.StandardOutput.ReadToEndAsync();
        var error = helper.StandardError.ReadToEndAsync();
        if (!helper.WaitForExit(60_000))
        {
            try { helper.Kill(); } catch { }
            return "!helper did not end in 60 seconds";
        }
        var answer = output.Result;
        if (helper.ExitCode != 0 || answer.Length == 0)
            return $"!helper ended with {helper.ExitCode}: {error.Result.Trim()}";
        return answer;
    }

    /// <summary>Whether <paramref name="answer"/> says the helper failed rather than answered.</summary>
    public static bool Failed(string answer) => answer.StartsWith('!');

    public static string Read(int processId) => Run("read", processId);

    public static string WindowClass(int processId) => Run("window", processId);

    public static string Close(int processId) => Run("close", processId);

    /// <summary>
    /// Starts <paramref name="file"/> as Windows starts a console program from the shell: in a
    /// console window of its own, with this process's environment.
    /// </summary>
    public static Process StartInOwnConsole(string file, string arguments) =>
        Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true })!;
}
