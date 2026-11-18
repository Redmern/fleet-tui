using Fleet.Features.Diagnostics.RunDoctor;
using Fleet.Features.Diagnostics.RunDoctor.Models;
using Fleet.Shared.Orchestrations;

namespace Fleet.Tests.Features.Diagnostics;

public class IncludeCheckTests
{
    private const string AddLine = "  add to ~/CLAUDE.md:  @~/.config/fleet/ORCHESTRATOR.md";

    private static readonly string Home = Path.Combine(Path.GetTempPath(), "fleet-doctor\\home");

    private static string InHome(params string[] parts) => Path.Combine([Home, .. parts]);

    private static string Include => InHome(".config", "fleet", OrchestratorInclude.FileName);

    private static IncludeFacts Facts(
        string? homeClaude = null,
        string? userClaude = null,
        string? includeText = null,
        string? opencode = null,
        params string[] inUse) =>
        new(
            Home,
            Include,
            includeText ?? OrchestratorInclude.Text,
            [new InstructionFile(InHome("CLAUDE.md"), homeClaude), new InstructionFile(InHome(".claude", "CLAUDE.md"), userClaude)],
            [new InstructionFile(InHome(".config", "opencode", "opencode.json"), opencode)],
            InHome(".codex", "AGENTS.md"),
            InHome(".pi", "agent", "AGENTS.md"),
            inUse);

    private static IReadOnlyList<string> Lines(IncludeFacts facts) => IncludeCheck.Lines(facts);

    [Fact]
    public void A_missing_import_prints_the_exact_line_to_add()
    {
        var lines = Lines(Facts(homeClaude: "# my rules\n"));

        Assert.Contains(AddLine, lines);
        Assert.Contains(lines, l => l.Contains("claude: not imported", StringComparison.Ordinal));
    }

    [Fact]
    public void No_claude_file_at_all_also_prints_the_line_to_add() =>
        Assert.Contains(AddLine, Lines(Facts()));

    [Theory]
    [InlineData("@~/.config/fleet/ORCHESTRATOR.md")]
    [InlineData("# rules\n\n  @~/.config/fleet/ORCHESTRATOR.md  \nmore")]
    [InlineData("See @~/.config/fleet/ORCHESTRATOR.md for fleet.")]
    public void A_home_relative_import_in_the_home_claude_file_counts(string text)
    {
        var lines = Lines(Facts(homeClaude: text));

        Assert.DoesNotContain(AddLine, lines);
        Assert.Contains(lines, l => l.Contains("claude: imported by ~/CLAUDE.md", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("```\n@~/.config/fleet/ORCHESTRATOR.md\n```\n")]
    [InlineData("~~~\n@~/.config/fleet/ORCHESTRATOR.md\n~~~\n")]
    [InlineData("Write `@~/.config/fleet/ORCHESTRATOR.md` in your file.")]
    public void An_import_inside_code_is_not_expanded_by_claude_so_it_does_not_count(string text) =>
        Assert.Contains(AddLine, Lines(Facts(homeClaude: text)));

    [Fact]
    public void An_import_after_a_closed_code_block_counts() =>
        Assert.DoesNotContain(AddLine, Lines(Facts(homeClaude: "```\ncode\n```\n@~/.config/fleet/ORCHESTRATOR.md\n")));

    [Fact]
    public void An_absolute_import_counts() =>
        Assert.DoesNotContain(AddLine, Lines(Facts(homeClaude: $"@{Include}\n")));

    [Fact]
    public void An_import_in_the_claude_user_memory_file_counts()
    {
        var lines = Lines(Facts(userClaude: "@~/.config/fleet/ORCHESTRATOR.md\n"));

        Assert.DoesNotContain(AddLine, lines);
        Assert.Contains(lines, l => l.Contains("imported by ~/.claude/CLAUDE.md", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("~/.config/fleet/ORCHESTRATOR.md")]
    [InlineData("@~/.config/fleet/OTHER.md")]
    [InlineData("email me@~/.config/fleet/ORCHESTRATOR.md")]
    public void Text_that_is_not_an_import_of_the_include_does_not_count(string text) =>
        Assert.Contains(AddLine, Lines(Facts(homeClaude: text)));

    [Fact]
    public void The_first_line_names_the_include_and_whether_it_is_current()
    {
        Assert.Equal("  orchestrator  ~/.config/fleet/ORCHESTRATOR.md, current", Lines(Facts())[0]);
        Assert.Contains("out of date; run fleet setup", Lines(Facts(includeText: "old"))[0]);
        Assert.Contains("missing; run fleet setup", Lines(Facts() with { IncludeText = null })[0]);
    }

    [Fact]
    public void An_include_outside_the_home_is_imported_by_its_absolute_path()
    {
        var elsewhere = Path.Combine(Path.GetTempPath(), "fleet-doctor-elsewhere", OrchestratorInclude.FileName);
        var facts = Facts() with { IncludePath = elsewhere };

        Assert.Contains($"  add to ~/CLAUDE.md:  @{elsewhere.Replace('\\', '/')}", Lines(facts));
        Assert.DoesNotContain(
            $"  add to ~/CLAUDE.md:  @{elsewhere.Replace('\\', '/')}",
            Lines(facts with { ClaudeFiles = [new InstructionFile(InHome("CLAUDE.md"), $"@{elsewhere}")] }));
    }

    [Fact]
    public void Without_a_non_claude_harness_in_use_no_agents_md_or_harness_line_is_printed()
    {
        var lines = Lines(Facts(inUse: ["claude"]));

        Assert.DoesNotContain(lines, l => l.Contains("AGENTS.md", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("opencode", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("codex", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("pi:", StringComparison.Ordinal));
    }

    [Fact]
    public void Opencode_in_use_without_the_include_in_its_instructions_prints_the_entry_to_add()
    {
        var lines = Lines(Facts(opencode: """{ "instructions": ["CONTRIBUTING.md"] }""", inUse: ["opencode"]));

        Assert.Contains(
            "  add to \"instructions\" in ~/.config/opencode/opencode.json:  \"~/.config/fleet/ORCHESTRATOR.md\"",
            lines);
        Assert.Contains(lines, l => l.Contains("opencode: AGENTS.md cannot import a file", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{ "instructions": ["~/.config/fleet/ORCHESTRATOR.md"] }""")]
    [InlineData("""
        {
          // fleet
          "instructions": [ "a.md", "~/.config/fleet/ORCHESTRATOR.md", ],
        }
        """)]
    public void Opencode_listing_the_include_counts(string config)
    {
        var lines = Lines(Facts(opencode: config, inUse: ["opencode"]));

        Assert.DoesNotContain(lines, l => l.StartsWith("  add to \"instructions\"", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("opencode: listed in ~/.config/opencode/opencode.json", StringComparison.Ordinal));
    }

    [Fact]
    public void Opencode_listing_the_include_in_its_extra_config_folder_counts()
    {
        var facts = Facts(opencode: "{}", inUse: ["opencode"]) with
        {
            OpencodeConfigs =
            [
                new InstructionFile(InHome(".config", "opencode", "opencode.json"), "{}"),
                new InstructionFile(InHome("oc", "opencode.json"), """{ "instructions": ["~/.config/fleet/ORCHESTRATOR.md"] }"""),
            ],
        };

        var lines = Lines(facts);

        Assert.Contains(lines, l => l.Contains("opencode: listed in ~/oc/opencode.json", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith("  add to \"instructions\"", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unreadable_opencode_config_prints_the_entry_to_add_and_says_why()
    {
        var lines = Lines(Facts(opencode: "{ not json", inUse: ["opencode"]));

        Assert.Contains(lines, l => l.StartsWith("  add to \"instructions\"", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("could not be read", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("codex", "~/.codex/AGENTS.md")]
    [InlineData("pi", "~/.pi/agent/AGENTS.md")]
    public void A_harness_that_cannot_import_says_so_plainly_and_prints_no_line_to_add(string harness, string file)
    {
        var lines = Lines(Facts(inUse: [harness]));

        Assert.Contains(
            lines,
            l => l.Contains($"{harness}: {file} cannot import a file", StringComparison.Ordinal)
                && l.Contains("not available", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.StartsWith($"  add to {file}", StringComparison.Ordinal));
    }
}
