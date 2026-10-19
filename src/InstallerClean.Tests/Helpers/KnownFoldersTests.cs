using System.Runtime.InteropServices;
using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// KnownFolders asks for each folder with the GUID .NET uses, takes the answer only on
/// S_OK, falls back to the environment variable .NET names for the folder, and frees the
/// string Windows hands back on every path. A stand-in plays Windows, so these run on any
/// host.
/// </summary>
public class KnownFoldersTests
{
    private const int S_OK = 0;
    private const int S_FALSE = 1;
    private const int E_FAIL = unchecked((int)0x80004005);

    public static TheoryData<Environment.SpecialFolder> Folders => new()
    {
        Environment.SpecialFolder.Windows,
        Environment.SpecialFolder.LocalApplicationData,
        Environment.SpecialFolder.CommonApplicationData,
        Environment.SpecialFolder.ProgramFiles,
        Environment.SpecialFolder.ProgramFilesX86,
    };

    public static TheoryData<Environment.SpecialFolder, string> FolderGuids => new()
    {
        { Environment.SpecialFolder.Windows, "F38BF404-1D43-42F2-9305-67DE0B28FC23" },
        { Environment.SpecialFolder.LocalApplicationData, "F1B32785-6FBA-4FCF-9D55-7B8E7F157091" },
        { Environment.SpecialFolder.CommonApplicationData, "62AB5D82-FDC1-4DC3-A9DD-070D1D495D97" },
        { Environment.SpecialFolder.ProgramFiles, "905E63B6-C1BF-494E-B29C-65B732D3D21A" },
        { Environment.SpecialFolder.ProgramFilesX86, "7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E" },
    };

    public static TheoryData<Environment.SpecialFolder, string> FolderVariables => new()
    {
        { Environment.SpecialFolder.Windows, "windir" },
        { Environment.SpecialFolder.LocalApplicationData, "LOCALAPPDATA" },
        { Environment.SpecialFolder.CommonApplicationData, "ProgramData" },
        { Environment.SpecialFolder.ProgramFiles, "ProgramFiles" },
        { Environment.SpecialFolder.ProgramFilesX86, "ProgramFiles(x86)" },
    };

    /// <summary>Plays SHGetKnownFolderPath: records what it was asked and hands back a fixed answer.</summary>
    private sealed class StandIn(int result, string? answer)
    {
        public Guid? FolderId;
        public uint? Flags;
        public IntPtr? Token;
        public IntPtr Handed;
        public readonly List<IntPtr> Freed = [];
        public readonly List<string> VariablesAsked = [];

        public int Query(in Guid folderId, uint flags, IntPtr token, out IntPtr path)
        {
            FolderId = folderId;
            Flags = flags;
            Token = token;
            path = Handed = answer is null ? IntPtr.Zero : Marshal.StringToHGlobalUni(answer);
            return result;
        }

        public void Free(IntPtr pointer)
        {
            Freed.Add(pointer);
            Marshal.FreeHGlobal(pointer);
        }

        public Func<string, string?> Variables(string? value) => name =>
        {
            VariablesAsked.Add(name);
            return value;
        };
    }

    [Theory]
    [MemberData(nameof(FolderGuids))]
    public void Each_folder_is_asked_for_by_its_GUID_with_no_flags_and_no_token(
        Environment.SpecialFolder folder, string guid)
    {
        var windows = new StandIn(S_OK, @"C:\Somewhere");

        KnownFolders.GetPath(folder, windows.Query, windows.Free, windows.Variables(null));

        Assert.Equal(new Guid(guid), windows.FolderId);
        Assert.Equal(0u, windows.Flags);
        Assert.Equal(IntPtr.Zero, windows.Token);
    }

    [Theory]
    [MemberData(nameof(Folders))]
    public void On_S_OK_the_answer_is_the_string_Windows_handed_back_and_it_is_freed_once(
        Environment.SpecialFolder folder)
    {
        var windows = new StandIn(S_OK, @"C:\Somewhere");

        var path = KnownFolders.GetPath(folder, windows.Query, windows.Free, windows.Variables("unused"));

        Assert.Equal(@"C:\Somewhere", path);
        Assert.Equal([windows.Handed], windows.Freed);
        Assert.Empty(windows.VariablesAsked);
    }

    [Theory]
    [MemberData(nameof(FolderVariables))]
    public void A_failed_call_answers_with_the_folders_variable_and_frees_what_it_was_handed(
        Environment.SpecialFolder folder, string variable)
    {
        foreach (var result in new[] { E_FAIL, S_FALSE })
        {
            var windows = new StandIn(result, @"C:\Not-taken");

            var path = KnownFolders.GetPath(folder, windows.Query, windows.Free, windows.Variables(@"D:\From-variable"));

            Assert.Equal(@"D:\From-variable", path);
            Assert.Equal([variable], windows.VariablesAsked);
            Assert.Equal([windows.Handed], windows.Freed);
        }
    }

    [Theory]
    [MemberData(nameof(Folders))]
    public void A_failed_call_with_the_variable_unset_answers_with_an_empty_string(
        Environment.SpecialFolder folder)
    {
        var windows = new StandIn(E_FAIL, answer: null);

        var path = KnownFolders.GetPath(folder, windows.Query, windows.Free, windows.Variables(null));

        Assert.Equal(string.Empty, path);
        Assert.Empty(windows.Freed);
    }

    [Theory]
    [InlineData(Environment.SpecialFolder.System)]
    [InlineData(Environment.SpecialFolder.ApplicationData)]
    [InlineData(Environment.SpecialFolder.Desktop)]
    [InlineData(Environment.SpecialFolder.CommonProgramFiles)]
    [InlineData((Environment.SpecialFolder)12345)]
    public void A_folder_not_listed_throws_without_asking_Windows(Environment.SpecialFolder folder)
    {
        var windows = new StandIn(S_OK, @"C:\Somewhere");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            KnownFolders.GetPath(folder, windows.Query, windows.Free, windows.Variables(null)));
        Assert.Null(windows.FolderId);
    }
}
