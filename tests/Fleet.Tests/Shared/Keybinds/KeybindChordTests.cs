using Fleet.Shared.Keybinds;

namespace Fleet.Tests.Shared.Keybinds;

public class KeybindChordTests
{
    [Theory]
    [InlineData("alt+h", "Alt+h")]
    [InlineData("shift+ctrl+tab", "Ctrl+Shift+Tab")]
    [InlineData("ctrl+enter", "Ctrl+Enter")]
    [InlineData("Ctrl+D", "Ctrl+D")]
    [InlineData("G", "G")]
    [InlineData("f5", "F5")]
    [InlineData("option+n", "Alt+n")]
    [InlineData("cmd+k", "Super+k")]
    [InlineData("space", "Space")]
    [InlineData("f  f", "f f")]
    [InlineData("q r", "q r")]
    [InlineData("Ctrl++", "Ctrl++")]
    [InlineData("+", "+")]
    [InlineData("NONE", "none")]
    [InlineData("", "none")]
    public void Normalize_writes_one_portable_spelling(string chord, string expected)
    {
        Assert.Equal(expected, KeybindChord.Normalize(chord));
    }

    [Theory]
    [InlineData("hyper+h")]
    [InlineData("ctrl+")]
    public void Normalize_rejects_what_is_not_a_chord(string chord)
    {
        Assert.Throws<FormatException>(() => KeybindChord.Normalize(chord));
        Assert.False(KeybindChord.TryNormalize(chord, out _));
    }

    [Fact]
    public void SameAs_compares_normalised_chords()
    {
        Assert.True(KeybindChord.SameAs("ctrl+shift+tab", "Shift+Ctrl+Tab"));
        Assert.False(KeybindChord.SameAs("g", "G"));
        Assert.False(KeybindChord.SameAs("hyper+h", "hyper+h"));
    }
}
