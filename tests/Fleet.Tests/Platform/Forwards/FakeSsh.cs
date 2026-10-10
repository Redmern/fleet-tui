using Fleet.Platform.Forwards.Models;

namespace Fleet.Tests.Platform.Forwards;

public sealed class FakeSsh
{
    public List<IReadOnlyList<string>> Calls { get; } = [];

    public string Listening { get; set; } = string.Empty;

    public Func<IReadOnlyList<string>, SshResult?> Answer { get; set; } = _ => null;

    public IEnumerable<string> Forwards(string op) =>
        Calls.Where(c => c.Contains("-O") && c[c.ToList().IndexOf("-O") + 1] == op)
            .Select(c => c[c.ToList().IndexOf("-L") + 1]);

    public Task<SshResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        lock (Calls)
        {
            Calls.Add(arguments);
        }

        if (Answer(arguments) is { } answer)
        {
            return Task.FromResult(answer);
        }

        return Task.FromResult(arguments.Contains("-O")
            ? new SshResult(0, string.Empty, string.Empty)
            : new SshResult(0, Listening, string.Empty));
    }
}
