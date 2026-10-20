using System.Runtime.InteropServices;
using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// Bstrs makes a BSTR the way <see cref="Marshal.StringToBSTR"/> does: a null pointer for a
/// null string, a BSTR of the string's length with its characters copied in, and an
/// <see cref="OutOfMemoryException"/> where Windows allocates nothing. Free leaves alone any
/// value with no bit above the low 16. A stand-in plays Windows, so these run on any host.
/// </summary>
public class BstrsTests
{
    // The stand-in's characters past the string, which the copy must leave as they are:
    // SysAllocStringLen writes the null character itself.
    private const short Untouched = unchecked((short)0xFFFF);

    [Fact]
    public void A_null_string_is_a_null_pointer_and_nothing_is_allocated()
    {
        var asked = false;

        var bstr = Bstrs.FromString(null, (_, _) => { asked = true; return IntPtr.Zero; });

        Assert.Equal(IntPtr.Zero, bstr);
        Assert.False(asked);
    }

    [Fact]
    public void An_empty_string_is_a_BSTR_of_no_characters()
    {
        using var buffer = new StandIn(1);

        var bstr = Bstrs.FromString(string.Empty, buffer.Allocate);

        Assert.Equal(buffer.Pointer, bstr);
        Assert.Equal(new uint[] { 0 }, buffer.LengthsAsked);
        Assert.Equal(Untouched, Marshal.ReadInt16(bstr));
    }

    [Fact]
    public void Every_character_is_copied_and_the_one_after_them_is_left_to_Windows()
    {
        const string value = "https://example.org/a\0b";
        using var buffer = new StandIn(value.Length + 1);

        var bstr = Bstrs.FromString(value, buffer.Allocate);

        Assert.Equal(new[] { (uint)value.Length }, buffer.LengthsAsked);
        Assert.Equal(IntPtr.Zero, buffer.SourcesPassed.Single());
        Assert.Equal(value, Marshal.PtrToStringUni(bstr, value.Length));
        Assert.Equal(Untouched, Marshal.ReadInt16(bstr, value.Length * sizeof(char)));
    }

    [Fact]
    public void A_null_answer_from_Windows_throws_OutOfMemoryException()
    {
        Assert.Throws<OutOfMemoryException>(() => Bstrs.FromString("x", (_, _) => IntPtr.Zero));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(0xFFFFL)]
    public void Free_leaves_null_and_an_atom_alone(long value)
    {
        var freed = new List<IntPtr>();

        Bstrs.Free((IntPtr)value, freed.Add);

        Assert.Empty(freed);
    }

    [Theory]
    [InlineData(0x10000L)]
    [InlineData(0x7FF6_1234_5678L)]
    public void Free_frees_anything_above_the_atom_range(long value)
    {
        var freed = new List<IntPtr>();

        Bstrs.Free((IntPtr)value, freed.Add);

        Assert.Equal(new[] { (IntPtr)value }, freed);
    }

    // Memory the stand-in hands back, filled with a value the copy must not write.
    private sealed class StandIn : IDisposable
    {
        private readonly int _chars;

        public StandIn(int chars)
        {
            _chars = chars;
            Pointer = Marshal.AllocHGlobal(chars * sizeof(char));
            for (var i = 0; i < chars; i++)
                Marshal.WriteInt16(Pointer, i * sizeof(char), Untouched);
        }

        public IntPtr Pointer { get; }
        public List<uint> LengthsAsked { get; } = new();
        public List<IntPtr> SourcesPassed { get; } = new();

        public IntPtr Allocate(IntPtr source, uint length)
        {
            SourcesPassed.Add(source);
            LengthsAsked.Add(length);
            Assert.True(length < _chars, "The stand-in holds fewer characters than were asked for.");
            return Pointer;
        }

        public void Dispose() => Marshal.FreeHGlobal(Pointer);
    }
}
