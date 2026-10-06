using Fleet.Platform.Mux.Embedded.Native;

namespace Fleet.Platform.Mux.Embedded.Input;

public abstract record KeyStep
{
    public sealed record Run(string Command) : KeyStep;

    public sealed record Enter(KeyNode Node) : KeyStep;
}

public sealed class KeyNode
{
    private readonly List<MuxKeys.Binding> _leaves = [];
    private readonly List<KeyGroup> _groups = [];

    public KeyNode(string label, KeyNode? parent = null)
    {
        Label = label;
        Parent = parent;
    }

    public string Label { get; }

    public KeyNode? Parent { get; }

    public IReadOnlyList<MuxKeys.Binding> Leaves => _leaves;

    public IReadOnlyList<KeyGroup> Groups => _groups;

    public int Depth => Parent is null ? 0 : Parent.Depth + 1;

    public KeyStep? Match(Key key, Mods mods, string? text)
    {
        if (_leaves.FirstOrDefault(b => b.Chord.Matches(key, mods, text)) is { } leaf)
        {
            return new KeyStep.Run(leaf.Command);
        }

        return _groups.FirstOrDefault(g => g.Chord.Matches(key, mods, text)) is { } group
            ? new KeyStep.Enter(group.Node)
            : null;
    }

    public (KeyStep? Step, int Length) Match(ReadOnlySpan<byte> bytes)
    {
        KeyStep? step = null;
        var length = 0;

        foreach (var leaf in _leaves)
        {
            if (leaf.Bytes is { Length: > 0 } sequence && sequence.Length > length && bytes.StartsWith(sequence))
            {
                step = new KeyStep.Run(leaf.Command);
                length = sequence.Length;
            }
        }

        foreach (var group in _groups)
        {
            if (group.Bytes is { Length: > 0 } sequence && sequence.Length > length && bytes.StartsWith(sequence))
            {
                step = new KeyStep.Enter(group.Node);
                length = sequence.Length;
            }
        }

        return (step, length);
    }

    public IEnumerable<MuxKeys.Binding> All() => _leaves.Concat(_groups.SelectMany(g => g.Node.All()));

    public string Breadcrumb(string prefix) =>
        Parent is null ? prefix : Parent.Breadcrumb(prefix) + Protocol.BadgeMessage.Breadcrumb + Label;

    internal void Add(MuxKeys.Binding leaf) => _leaves.Add(leaf);

    internal KeyNode Child(string spec, KeyChord chord, string label)
    {
        if (_groups.FirstOrDefault(g => g.Chord == chord) is { } existing)
        {
            return existing.Node;
        }

        var node = new KeyNode(label, this);
        _groups.Add(new KeyGroup(spec, chord, chord.Bytes(), node));
        return node;
    }

    public sealed record KeyGroup(string Spec, KeyChord Chord, byte[]? Bytes, KeyNode Node);
}
