using System.Text;

namespace Fleet.Shared.Themes;

public static class ThemeNames
{
    public static string Normalize(string name)
    {
        var slug = new StringBuilder(name.Length);

        foreach (var c in name.Trim().ToLowerInvariant())
        {
            var plain = Unaccent(c);

            if (char.IsAsciiLetterOrDigit(plain))
            {
                slug.Append(plain);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        return slug.ToString().TrimEnd('-');
    }

    private static char Unaccent(char c) => c switch
    {
        'á' or 'à' or 'â' or 'ä' or 'ã' or 'å' => 'a',
        'é' or 'è' or 'ê' or 'ë' => 'e',
        'í' or 'ì' or 'î' or 'ï' => 'i',
        'ó' or 'ò' or 'ô' or 'ö' or 'õ' or 'ø' => 'o',
        'ú' or 'ù' or 'û' or 'ü' => 'u',
        'ç' => 'c',
        'ñ' => 'n',
        _ => c,
    };
}
