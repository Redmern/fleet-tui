using System.Reflection;
using System.Text.RegularExpressions;

namespace Fleet.Tests.Architecture;

/// <summary>
/// Terminal.Gui views must be created and run on the UI thread. Dashboard callbacks
/// are async and resume on a pool thread after their first await (ConfigureAwait(false)),
/// so any modal they open after that point has to be marshalled back via FleetAsync.OnUi.
/// A modal opened off-thread runs a nested Application.Run that races the main loop's
/// drawing, which shows up as an intermittently garbled screen.
/// </summary>
public partial class UiThreadTests
{
    private static string RepoRoot { get; } =
        typeof(UiThreadTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepoRoot")?.Value
        ?? throw new InvalidOperationException(
            "RepoRoot assembly metadata is missing - see Fleet.Tests.csproj");

    private static string WiringPath =>
        Path.Combine(RepoRoot, "src", "Fleet", "Cli", "Composition", "DashboardWiring.cs");

    [GeneratedRegex(@"^ {12}[A-Z]\w*:\s", RegexOptions.Multiline)]
    private static partial Regex CallbackStart();

    [GeneratedRegex(@"\b(\w+View\.Show|FleetPicker\.Choose|FleetDialog\.\w+|FleetPrompt\.\w+|FleetUi\.Menu|Secrets|ChooseHarness)\(\s*app\b")]
    private static partial Regex UiCall();

    [GeneratedRegex(@"\bawait\b")]
    private static partial Regex Await();

    [Fact]
    public void Dashboard_callbacks_open_modals_on_the_ui_thread_after_their_first_await()
    {
        var source = File.ReadAllText(WiringPath);
        var start = source.IndexOf("return new DashboardCallbacks(", StringComparison.Ordinal);
        Assert.True(start >= 0, "DashboardCallbacks construction not found");

        var body = source[start..];
        var starts = CallbackStart().Matches(body).Select(m => m.Index).Append(body.Length).ToList();
        var offenders = new List<string>();

        for (var i = 0; i + 1 < starts.Count; i++)
        {
            var chunk = body[starts[i]..starts[i + 1]];
            var name = chunk.TrimStart().Split(':')[0];

            foreach (Match ui in UiCall().Matches(chunk))
            {
                var before = chunk[..ui.Index];
                var lastAwait = Await().Matches(before).LastOrDefault();

                if (lastAwait is null)
                {
                    continue;
                }

                var between = before[lastAwait.Index..];

                if (!between.Contains("OnUi(", StringComparison.Ordinal))
                {
                    offenders.Add($"{name}: {ui.Value} runs after an await without FleetAsync.OnUi");
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }
}
