namespace Fleet.Platform.Mux.Embedded.Input;

public sealed class PasteMarkers(int maxPaste = PasteMarkers.MaxPaste)
{
    public const string Enable = "\e[?2004h";

    public const int MaxPaste = 1024 * 1024;

    public static readonly TimeSpan StartHold = TimeSpan.FromMilliseconds(40);

    public static readonly TimeSpan PasteIdle = TimeSpan.FromSeconds(2);

    private readonly List<byte> _held = [];
    private readonly List<byte> _paste = [];
    private bool _inside;
    private TimeSpan _lastRead;

    private static ReadOnlySpan<byte> Start => "\e[200~"u8;
    private static ReadOnlySpan<byte> End => "\e[201~"u8;

    public TimeSpan? Deadline =>
        _inside ? _lastRead + PasteIdle
        : _held.Count > 0 ? _lastRead + StartHold
        : null;

    public IReadOnlyList<PasteChunk> Feed(ReadOnlySpan<byte> data, TimeSpan now)
    {
        _lastRead = now;
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
                var room = maxPaste - _paste.Count;

                if (end < 0 || end > room)
                {
                    var keep = end < 0 ? HeldPrefix(rest, End) : 0;
                    var take = Math.Min(rest.Length - keep, room);
                    _paste.AddRange(rest[..take]);

                    if (_paste.Count < maxPaste)
                    {
                        _held.AddRange(rest[take..]);
                        break;
                    }

                    LeavePaste(chunks);
                    i += take;
                    continue;
                }

                _paste.AddRange(rest[..end]);
                LeavePaste(chunks);
                i += end + End.Length;
                continue;
            }

            var start = rest.IndexOf(Start);
            var stray = rest.IndexOf(End);

            if (stray >= 0 && (start < 0 || stray < start))
            {
                typed.AddRange(rest[..stray]);
                i += stray + End.Length;
                continue;
            }

            if (start < 0)
            {
                var keep = HeldPrefix(rest, Start);
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

    public IReadOnlyList<PasteChunk> Expire(TimeSpan now)
    {
        var chunks = new List<PasteChunk>();

        if (Deadline is not { } due || now < due)
        {
            return chunks;
        }

        if (_inside)
        {
            _paste.AddRange(_held);
            _held.Clear();
            LeavePaste(chunks);
            return chunks;
        }

        Typed(chunks, _held);
        return chunks;
    }

    private void LeavePaste(List<PasteChunk> chunks)
    {
        if (_paste.Count > 0)
        {
            chunks.Add(new PasteChunk([.. _paste], true));
        }

        _paste.Clear();
        _inside = false;
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

    private static int HeldPrefix(ReadOnlySpan<byte> rest, ReadOnlySpan<byte> marker)
    {
        for (var length = Math.Min(marker.Length - 1, rest.Length); length >= 1; length--)
        {
            if (rest[^length..].SequenceEqual(marker[..length]))
            {
                return length;
            }
        }

        return 0;
    }
}
