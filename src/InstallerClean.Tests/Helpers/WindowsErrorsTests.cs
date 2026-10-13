using System.Runtime.InteropServices;
using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// Reading a Windows error number off an exception, and the sentence that gives it.
/// </summary>
public class WindowsErrorsTests
{
    // ERROR_NOT_ENOUGH_MEMORY, as .NET carries it in an exception it builds from a
    // Windows error: 0x8007 in the high word, the number in the low.
    private const int NotEnoughMemory = 8;

    [Fact]
    public void An_exception_raised_for_a_Windows_error_gives_its_number()
    {
        var raised = new IOException("any text", unchecked((int)0x80070008));

        Assert.Equal(NotEnoughMemory, WindowsErrors.Of(raised));
    }

    [Fact]
    public void An_exception_dotnet_raised_on_its_own_account_gives_none()
    {
        Assert.Null(WindowsErrors.Of(new InvalidOperationException()));
        Assert.Null(WindowsErrors.Of(new ApplicationException()));
    }

    [Fact]
    public void An_exception_dotnet_raised_on_its_own_account_with_a_code_in_the_Windows_facility_gives_none()
    {
        // The code for an invalid parameter and the code for the end of a file, which .NET
        // gives these two whatever Windows did.
        Assert.Equal(unchecked((int)0x80070057), new ArgumentException().HResult);
        Assert.Null(WindowsErrors.Of(new ArgumentException()));
        Assert.Equal(unchecked((int)0x80070026), new EndOfStreamException().HResult);
        Assert.Null(WindowsErrors.Of(new EndOfStreamException()));
    }

    [Fact]
    public void A_Win32Exception_gives_its_own_number()
    {
        Assert.Equal(NotEnoughMemory, WindowsErrors.Of(new System.ComponentModel.Win32Exception(NotEnoughMemory)));
    }

    [Fact]
    public void The_sentence_with_a_number_gives_Windows_text_and_the_number()
    {
        var sentence = WindowsErrors.Sentence(NotEnoughMemory, "plain", "because: {0} (error {1})");

        Assert.Equal(
            $"because: {WindowsErrors.MessageFor(NotEnoughMemory)} (error 8)",
            sentence);
        Assert.False(WindowsErrors.MessageFor(NotEnoughMemory).EndsWith('.'));
        Assert.Equal(
            Marshal.GetPInvokeErrorMessage(NotEnoughMemory).Trim().TrimEnd('.'),
            WindowsErrors.MessageFor(NotEnoughMemory));
    }

    [Fact]
    public void Windows_text_broken_across_lines_is_carried_as_one_line()
    {
        Assert.Equal(
            "The first line of the text and its second line",
            WindowsErrors.OneLine("  The first line of the text\r\nand its\tsecond line.\r\n"));
        Assert.Equal("一行目 二行目", WindowsErrors.OneLine("一行目\r\n二行目。\r\n"));
    }

    [Fact]
    public void The_sentence_with_no_number_is_the_plain_one()
    {
        Assert.Equal("plain", WindowsErrors.Sentence(null, "plain", "because: {0} (error {1})"));
    }

    [Fact]
    public void The_number_is_printed_in_decimal_whatever_the_culture()
    {
        var before = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("1450", WindowsErrors.Number(1450));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = before;
        }
    }
}
