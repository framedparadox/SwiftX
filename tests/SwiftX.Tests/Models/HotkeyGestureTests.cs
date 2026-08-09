using SwiftX.Models;
using Xunit;

namespace SwiftX.Tests.Models;

public class HotkeyGestureTests
{
    [Fact]
    public void Default_is_Ctrl_Shift_S()
    {
        Assert.Equal("Ctrl+Shift+S", HotkeyGesture.Default.ToString());
        Assert.True(HotkeyGesture.Default.IsValid);
    }

    [Theory]
    [InlineData("Ctrl+Alt+Space")]
    [InlineData("Ctrl+Shift+F12")]
    [InlineData("Win+Space")]
    [InlineData("Alt+PageDown")]
    public void Round_trips_through_its_text_form(string text)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(text, gesture!.ToString());
    }

    [Theory]
    [InlineData("ctrl+alt+space")]
    [InlineData("Control + Alt + Space")]
    [InlineData("ALT+CTRL+Space")]
    public void Parsing_is_tolerant_of_spelling_and_order(string text)
    {
        // Compared against a gesture spelled out here, not against Default: this theory is about
        // the parser, and pinning it to whatever the shipped shortcut happens to be made every
        // case fail the day that shortcut changed.
        var expected = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x20);
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(expected, gesture);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Alt+A+B")]
    public void Rejects_input_it_cannot_round_trip(string? text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Null(gesture);
    }

    [Fact]
    public void A_bare_key_parses_but_is_not_valid_to_register()
    {
        Assert.True(HotkeyGesture.TryParse("A", out var gesture));
        Assert.False(gesture!.IsValid);
    }
}
