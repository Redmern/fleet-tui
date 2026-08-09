using Fleet.Cli;

namespace Fleet;

public static class Program
{
    public static Task<int> Main(string[] args) => Runner.RunAsync(CommandLine.Parse(args));
}
