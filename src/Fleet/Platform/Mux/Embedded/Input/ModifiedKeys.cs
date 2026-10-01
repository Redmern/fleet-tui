using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Fleet.Platform.Mux.Embedded.Input;

public static partial class ModifiedKeys
{
    public const string Enable = "\e[>4;1m";

    public const string Disable = "\e[>4;0m";

    public static string Report(int modifier, int code) => $"\e[27;{modifier};{code}~";

    public static byte[] Normalize(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0x1b || bytes[1] != (byte)'[')
        {
            return bytes;
        }

        var text = Encoding.ASCII.GetString(bytes);
        var normalized = CsiU().Replace(text, m => Report(int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)));
        return normalized == text ? bytes : Encoding.ASCII.GetBytes(normalized);
    }

    public static (int Modifier, int Code, int Length)? Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8 || bytes[0] != 0x1b || bytes[1] != (byte)'[')
        {
            return null;
        }

        var end = bytes.IndexOf((byte)'~');
        if (end < 0)
        {
            return null;
        }

        var match = Reported().Match(Encoding.ASCII.GetString(bytes[..(end + 1)]));
        return match.Success
            ? (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), end + 1)
            : null;
    }

    public static byte[] Legacy(int modifier, int code)
    {
        var bits = modifier - 1;
        var shift = (bits & 1) != 0;
        var alt = (bits & 2) != 0;
        var ctrl = (bits & 4) != 0;

        var key = code switch
        {
            13 => "\r",
            9 => shift ? "\e[Z" : "\t",
            27 => "\e",
            8 or 127 => "\x7f",
            >= 'a' and <= 'z' when ctrl => ((char)(code - 'a' + 1)).ToString(),
            >= 'A' and <= 'Z' when ctrl => ((char)(code - 'A' + 1)).ToString(),
            _ => char.ConvertFromUtf32(code),
        };

        return Encoding.UTF8.GetBytes(alt && code != 27 ? "\e" + key : key);
    }

    [GeneratedRegex(@"\x1b\[(\d+);(\d+)u")]
    private static partial Regex CsiU();

    [GeneratedRegex(@"^\x1b\[27;(\d+);(\d+)~$")]
    private static partial Regex Reported();
}