using Fleet.Platform.Mux.Embedded.Native;

namespace Fleet.Platform.Mux.Embedded.Input;

public enum PrefixCommand
{
    None,
    Armed,
    SendPrefix,
    Chord,
}

public sealed class Prefix(KeyChord chord)
{
    public KeyChord Chord { get; } = chord;

    public bool Armed { get; private set; }

    public string Label => Chord.Label;

    public byte[]? Bytes { get; } = chord.Bytes();

    public void Arm() => Armed = true;

    public void Disarm() => Armed = false;

    public PrefixCommand OnKey(Key key, Mods mods, string? text = null)
    {
        var isPrefix = Chord.Matches(key, mods, text);

        if (!Armed)
        {
            Armed = isPrefix;
            return isPrefix ? PrefixCommand.Armed : PrefixCommand.None;
        }

        Armed = false;
        return isPrefix ? PrefixCommand.SendPrefix : PrefixCommand.Chord;
    }
}