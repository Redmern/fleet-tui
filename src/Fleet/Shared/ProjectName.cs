using System.Text;

namespace Fleet.Shared;

public static class ProjectName
{
    public static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);

        foreach (var c in name)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
