namespace Fleet.Platform.Mux.Embedded.Input;

public sealed class PasteMarkers
{
    public const string Enable = "\e[?2004h";

    private const int ShortestHeldStart = 3;

    private readonly List<byte> _held = [];
    private readonly List<byte> _paste = [];
    private bool _inside;

    private static ReadOnlySpan<byte> Start => "\e[200~"u8;
    private static ReadOnlySpan<byte> End => "\e[201~"u8;

    public IReadOnlyList<PasteChunk> Feed(ReadOnlySpan<byte> data)
    {
        byte[] bytes = [.. _held, .. data];
        _held.Clear();

        var chunks = new List<PasteChunk>();
        var typed = new List<byte>();
        var i = 0;

        while (i < bytes.Length)
        {
            var rest = bytes.AsSpan(i);

            if (_inside)
            {
                var end = rest.IndexOf(End);
                if (end < 0)
                {
                    var keep = HeldPrefix(rest, End, 1);
                    _paste.AddRange(rest[..^keep]);
                    _held.AddRange(rest[^keep..]);
                    break;
                }

                _paste.AddRange(rest[..end]);
                if (_paste.Count > 0)
                {
                    chunks.Add(new PasteChunk([.. _paste], true));
                }

                _paste.Clear();
                _inside = false;
                i += end + End.Length;
                continue;
            }

            var start = rest.IndexOf(Start);
            if (start < 0)
            {
                var keep = HeldPrefix(rest, Start, ShortestHeldStart);
                typed.AddRange(rest[..^keep]);
                _held.AddRange(rest[^keep..]);
                break;
            }

            typed.AddRange(rest[..start]);
            Typed(chunks, typed);
            _inside = true;
            i += start + Start.Length;
        }

        Typed(chunks, typed);
        return chunks;
    }

    private static void Typed(List<PasteChunk> chunks, List<byte> typed)
    {
        if (typed.Count == 0)
        {
            return;
        }

        chunks.Add(new PasteChunk([.. typed], false));
        typed.Clear();
    }

    private static int HeldPrefix(ReadOnlySpan<byte> rest, ReadOnlySpan<byte> marker, int shortest)
    {
        for (var length = Math.Min(marker.Length - 1, rest.Length); length >= shortest; length--)
        {
            if (rest[^length..].SequenceEqual(marker[..length]))
            {
                return length;
            }
        }

        return 0;
    }
}
