using System.Text;

namespace Fleet.Ui;

public static class TerminalClipboard
{
    public static string Sequence(string text) => $"\e]52;c;{Convert.ToBase64String(Encoding.UTF8.GetBytes(text))}\a";

    public static void Copy(string text, TextWriter? to = null)
    {
        var writer = to ?? Console.Out;
        writer.Write(Sequence(text));
        writer.Flush();
    }
}
