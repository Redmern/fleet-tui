using System.Text;

namespace Fleet.Platform.Mux.Embedded.Input;

public sealed class SizeQueries
{
    private static readonly byte[] Query = "\e[18t"u8.ToArray();

    private int _matched;

    public int Count(ReadOnlySpan<byte> output)
    {
        var found = 0;

        foreach (var b in output)
        {
            if (b == Query[_matched])
            {
                _matched++;
            }
            else
            {
                _matched = b == Query[0] ? 1 : 0;
            }

            if (_matched == Query.Length)
            {
                found++;
                _matched = 0;
            }
        }

        return found;
    }

    public static byte[] Reply(int cols, int rows) => Encoding.ASCII.GetBytes($"\e[8;{rows};{cols}t");
}
