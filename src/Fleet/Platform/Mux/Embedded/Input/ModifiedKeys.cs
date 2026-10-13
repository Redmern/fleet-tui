using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Fleet.Platform.Mux.Embedded.Input;

public static partial class ModifiedKeys
{
    public const string Enable = "\e[>4;1m";

    public const string Disable = "\e[>4;0m";

    private const int CtrlShift = 6;

    public static string Report(int modifier, int code) => $"\e[27;{modifier};{code}~";

    public static byte[] Normalize(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0x1b || bytes[1] != (byte)'[')
        {
            return bytes;
        }

        var text = Encoding.ASCII.GetString(bytes);
        var normalized = CsiU().Replace(text, m => Canonical(m, m.Groups[2], m.Groups[1]));
        normalized = Xterm().Replace(normalized, m => Canonical(m, m.Groups[1], m.Groups[2]));
        return normalized == text ? bytes : Encoding.ASCII.GetBytes(normalized);
    }

    private static int? Number(Group group) =>
        int.TryParse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;

    public static bool IsClipboardChord(int modifier, int code) =>
        modifier == CtrlShift && code is 'C' or 'V' or 'c' or 'v';

    private static string Canonical(Match match, Group modifierGroup, Group codeGroup) =>
        (Number(modifierGroup), Number(codeGroup)) is (int modifier, int code)
            ? Report(modifier, IsClipboardChord(modifier, code) && code is >= 'a' and <= 'z' ? code - ('a' - 'A') : code)
            : match.Value;

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
        return match.Success && (Number(match.Groups[1]), Number(match.Groups[2])) is (int modifier, int code)
            ? (modifier, code, end + 1)
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

    [GeneratedRegex(@"\x1b\[27;(\d+);(\d+)~")]
    private static partial Regex Xterm();

    [GeneratedRegex(@"^\x1b\[27;(\d+);(\d+)~$")]
    private static partial Regex Reported();
}