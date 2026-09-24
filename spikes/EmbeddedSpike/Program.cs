namespace EmbeddedSpike;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] is "-h" or "--help")
        {
            Console.WriteLine("""
                embeddedspike - one pane: PTY -> libghostty-vt -> diffed host render

                  embeddedspike [--prefix ctrl+b] [--dump FILE] [--log FILE] [--keys MODE] [--] [program args...]

                  prefix q        quit (restores the host terminal)
                  prefix prefix   send the prefix chord itself to the pane
                  prefix d        write the emulator's screen text to --dump FILE
                  prefix r        full redraw

                  --dump FILE     keep FILE updated with the emulator's screen as text
                  --log FILE      log key records, encoded bytes and resizes
                  --keys MODE     Windows: auto (win32-input-mode when ConPTY asks, the
                                  default) or ghostty (libghostty-vt's key encoder)

                Windows test helper (writes real console input records):

                  embeddedspike --inject PID TOKEN...
                    TOKEN: ctrl+<letter> | alt+<char> | enter | esc | tab | bs
                           | up | down | left | right | text:<chars> | sleep:<ms>
                           | window:<width>x<height> (pixels)
                """);
            return 0;
        }

        if (args.Length > 0 && args[0] == "--inject")
        {
            return OperatingSystem.IsWindows() ? Injector.Run(args[1..]) : 2;
        }

        return new Session(Options.Parse(args)).Run();
    }
}
