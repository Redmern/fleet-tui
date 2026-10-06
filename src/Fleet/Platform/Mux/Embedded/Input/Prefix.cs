using Fleet.Platform.Mux.Embedded.Native;

namespace Fleet.Platform.Mux.Embedded.Input;

public enum PrefixCommand
{
    None,
    Armed,
    SendPrefix,
    Chord,
    Descend,
    Back,
    Cancel,
}

public sealed class Prefix(KeyChord chord)
{
    private const byte Delete = 0x7f;
    private const byte CtrlH = 0x08;

    public KeyChord Chord { get; } = chord;

    public bool Armed => Node is not null;

    public KeyNode? Node { get; private set; }

    public string? Command { get; private set; }

    public string Label => Chord.Label;

    public string Breadcrumb => Node?.Breadcrumb(Label) ?? Label;

    public byte[]? Bytes { get; } = chord.Bytes();

    public void Arm(KeyNode root) => Node = root;

    public void Disarm() => Node = null;

    public PrefixCommand OnKey(KeyNode root, Key key, Mods mods, string? text = null)
    {
        var isPrefix = Chord.Matches(key, mods, text);

        if (Node is not { } node)
        {
            Node = isPrefix ? root : null;
            return isPrefix ? PrefixCommand.Armed : PrefixCommand.None;
        }

        if (isPrefix)
        {
            return ToRootOrSend(root);
        }

        if (node.Match(key, mods, text) is { } step)
        {
            return Take(step);
        }

        return key switch
        {
            Key.Backspace => Up(),
            _ => Cancel(),
        };
    }

    public PrefixCommand OnBytes(KeyNode root, ReadOnlySpan<byte> bytes, out int length)
    {
        if (Node is not { } node)
        {
            length = 0;
            return PrefixCommand.None;
        }

        if (Bytes is { Length: > 0 } prefix && bytes.StartsWith(prefix))
        {
            length = prefix.Length;
            return ToRootOrSend(root);
        }

        var (step, matched) = node.Match(bytes);
        if (step is not null)
        {
            length = matched;
            return Take(step);
        }

        length = Math.Min(1, bytes.Length);
        return bytes.Length > 0 && bytes[0] is Delete or CtrlH ? Up() : Cancel();
    }

    private PrefixCommand ToRootOrSend(KeyNode root)
    {
        if (Node?.Parent is null)
        {
            Node = null;
            return PrefixCommand.SendPrefix;
        }

        Node = root;
        return PrefixCommand.Armed;
    }

    private PrefixCommand Take(KeyStep step)
    {
        switch (step)
        {
            case KeyStep.Enter enter:
                Node = enter.Node;
                return PrefixCommand.Descend;
            case KeyStep.Run run:
                Node = null;
                Command = run.Command;
                return PrefixCommand.Chord;
            default:
                return Cancel();
        }
    }

    private PrefixCommand Up()
    {
        Node = Node?.Parent;
        return Node is null ? PrefixCommand.Cancel : PrefixCommand.Back;
    }

    private PrefixCommand Cancel()
    {
        Node = null;
        return PrefixCommand.Cancel;
    }
}
