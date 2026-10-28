using System.Reflection;
using System.Text.RegularExpressions;

namespace Fleet.Tests.Architecture;

public partial class HarnessSwitchTests
{
    private static string SrcDir { get; } = Path.Combine(
        typeof(HarnessSwitchTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!,
        "src",
        "Fleet");

    private const string HarnessName =
        @"(AgentHarness\.(Claude|Nvim|Opencode|Orchestrator)|HarnessRegistry\.\w+Name)\b";

    // A switch arm, case label or list pattern on a harness constant means a per-name branch escaped the registry.
    [GeneratedRegex(
        @"case\s+" + HarnessName
        + @"|" + HarnessName + @"\s*(=>|when\b)"
        + @"|\b(is|case)\s*\[\s*(AgentHarness\.)?(Claude|Nvim|Opencode|Orchestrator)\b")]
    private static partial Regex HarnessBranch();

    private static IEnumerable<string> SourcesOutsideTheRegistry() =>
        Directory.GetFiles(SrcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.StartsWith(
                Path.Combine(SrcDir, "Shared", "Harness") + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    [Fact]
    public void No_code_outside_the_registry_switches_on_a_harness_name()
    {
        var violations = SourcesOutsideTheRegistry()
            .SelectMany(f => File.ReadAllLines(f)
                .Select((line, i) => (line, i))
                .Where(l => HarnessBranch().IsMatch(l.line))
                .Select(l => $"{Path.GetRelativePath(SrcDir, f)}:{l.i + 1}: {l.line.Trim()}"))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Agent_harness_has_no_switch_of_its_own()
    {
        var text = File.ReadAllText(Path.Combine(SrcDir, "Shared", "Constants", "AgentHarness.cs"));

        Assert.DoesNotMatch(@"\bswitch\b", text);
    }

    [Fact]
    public void The_check_leaves_other_constants_alone() =>
        Assert.DoesNotMatch(HarnessBranch(), "        Shared.Constants.AgentHarness.TitledVerb => FleetVerb.Titled,");

    [Theory]
    [InlineData("        AgentHarness.Claude => [Claude],")]
    [InlineData("            case AgentHarness.Opencode:")]
    [InlineData("        HarnessRegistry.OpencodeName when x => y,")]
    [InlineData("    if (command is [AgentHarness.Nvim, ..])")]
    public void The_check_catches_a_per_name_branch(string line) =>
        Assert.Matches(HarnessBranch(), line);
}
