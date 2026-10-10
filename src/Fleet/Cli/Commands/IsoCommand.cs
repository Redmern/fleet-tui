using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Iso.ToggleIso;
using Fleet.Features.Iso.ToggleIso.Models;

namespace Fleet.Cli.Commands;

public static class IsoCommand
{
    private static readonly string[] SshVariables = ["SSH_CONNECTION", "SSH_CLIENT", "SSH_TTY"];

    private static readonly string[] AgentVariables = ["CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT"];

    public static int Run(Invocation invocation)
    {
        var caller = new IsoCaller(
            !Console.IsInputRedirected,
            SshVariables.Any(Set),
            AgentVariables.Any(Set));

        var reply = new ToggleIsoHandler(Adapters.Iso()).Handle(invocation.Arguments ?? [], caller, ConfirmOff);

        if (reply.Exit == 0)
        {
            Console.WriteLine(reply.Text);
        }
        else
        {
            Console.Error.WriteLine(reply.Text);
        }

        return reply.Exit;
    }

    private static bool ConfirmOff()
    {
        Console.Write(ToggleIsoHandler.OffQuestion);
        return string.Equals(Console.ReadLine()?.Trim(), "off", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Set(string name) => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name));
}
