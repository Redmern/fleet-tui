using System.Text;
using System.Text.RegularExpressions;

namespace Fleet.Platform.Mux.Embedded.Input;

public static partial class KittyReplies
{
    public static byte[] Without(byte[] reply)
    {
        var text = Encoding.Latin1.GetString(reply);
        var kept = FlagsReport().Replace(text, string.Empty);
        return kept.Length == text.Length ? reply : Encoding.Latin1.GetBytes(kept);
    }

    [GeneratedRegex(@"\e\[\?\d*u")]
    private static partial Regex FlagsReport();
}