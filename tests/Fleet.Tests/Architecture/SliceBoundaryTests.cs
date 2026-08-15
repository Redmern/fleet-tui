using System.Reflection;

namespace Fleet.Tests.Architecture;

public class SliceBoundaryTests
{
    private const string PlatformNamespace = "Fleet.Platform";
    private const string FeaturesNamespace = "Fleet.Features";
    private const string PortsNamespace = "Fleet.Ports";
    private const string UiNamespace = "Fleet.Ui";
    private const string CliNamespace = "Fleet.Cli";

    private static string RepoRoot { get; } =
        typeof(SliceBoundaryTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepoRoot")?.Value
        ?? throw new InvalidOperationException(
            "RepoRoot assembly metadata is missing - see Fleet.Tests.csproj");

    private static string SrcDir => Path.Combine(RepoRoot, "src", "Fleet");

    private static string FeaturesDir => Path.Combine(SrcDir, "Features");

    [Fact]
    public void The_source_tree_was_actually_found()
    {
        Assert.True(Directory.Exists(SrcDir), $"expected a source tree at {SrcDir}");
        Assert.NotEmpty(CsFiles(SrcDir));
    }

    [Fact]
    public void No_slice_references_another_slice()
    {
        var slices = CsFiles(FeaturesDir)
            .Select(SliceOf)
            .Where(s => s is not null)
            .Select(s => s!.Value)
            .Distinct()
            .ToList();

        var violations = new List<string>();

        foreach (var file in CsFiles(FeaturesDir))
        {
            var own = SliceOf(file);
            if (own is null)
            {
                continue;
            }

            var text = File.ReadAllText(file);

            foreach (var other in slices.Where(s => s != own.Value))
            {
                var ns = $"{FeaturesNamespace}.{other.Area}.{other.Slice}";
                if (text.Contains(ns, StringComparison.Ordinal))
                {
                    violations.Add($"{Relative(file)} references {ns}");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void Features_never_reference_Platform()
    {
        var violations = CsFiles(FeaturesDir)
            .Where(Mentions(PlatformNamespace))
            .Select(Relative)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Only_the_composition_root_references_Platform_implementations()
    {
        var violations = CsFiles(SrcDir)
            .Where(f => !Under(f, "Platform"))
            .Where(f => !Under(f, Path.Combine("Cli", "Composition")))
            .Where(Mentions(PlatformNamespace))
            .Select(Relative)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Commands_wire_things_up_but_do_not_reach_for_Platform_themselves()
    {
        var violations = CsFiles(Path.Combine(SrcDir, "Cli", "Commands"))
            .Where(Mentions(PlatformNamespace))
            .Select(Relative)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Nothing_outside_the_composition_root_depends_on_the_cli()
    {
        var violations = CsFiles(SrcDir)
            .Where(f => !Under(f, "Cli"))
            .Where(f => !string.Equals(
                f, Path.Combine(SrcDir, "Program.cs"), StringComparison.OrdinalIgnoreCase))
            .Where(Mentions(CliNamespace))
            .Select(Relative)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Program_is_a_composition_root_not_a_place_for_logic()
    {
        var lines = File.ReadAllLines(Path.Combine(SrcDir, "Program.cs")).Length;

        Assert.True(lines < 20, $"Program.cs has grown to {lines} lines");
    }

    [Fact]
    public void Ports_depend_on_nothing_but_Shared()
    {
        var violations = CsFiles(Path.Combine(SrcDir, "Ports"))
            .Where(f => Mentions(PlatformNamespace)(f)
                     || Mentions(FeaturesNamespace)(f)
                     || Mentions(UiNamespace)(f))
            .Select(Relative)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Ui_depends_on_nothing_but_Shared()
    {
        var violations = CsFiles(Path.Combine(SrcDir, "Ui"))
            .Where(f => Mentions(PlatformNamespace)(f)
                     || Mentions(FeaturesNamespace)(f)
                     || Mentions(CliNamespace)(f)
                     || Mentions(PortsNamespace)(f))
            .Select(Relative)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Shared_depends_on_nothing_inside_Fleet()
    {
        var violations = CsFiles(Path.Combine(SrcDir, "Shared"))
            .Where(f => Mentions(PlatformNamespace)(f)
                     || Mentions(FeaturesNamespace)(f)
                     || Mentions(PortsNamespace)(f)
                     || Mentions(UiNamespace)(f))
            .Select(Relative)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void No_slice_styles_itself()
    {
        var banned = new[]
        {
            "SchemeName =",
            "new Scheme",
            "SchemeManager",
            "BorderStyle =",
            "ShadowStyle =",
            "MessageBox",
        };

        var violations = CsFiles(FeaturesDir)
            .Select(f => (File: f, Text: File.ReadAllText(f)))
            .Where(x => banned.Any(b => x.Text.Contains(b, StringComparison.Ordinal)))
            .Select(x => Relative(x.File))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void No_source_file_contains_a_comment()
    {
        var violations = new List<string>();

        foreach (var file in CsFiles(SrcDir))
        {
            var lineNumber = 0;

            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("/*", StringComparison.Ordinal))
                {
                    violations.Add($"{Relative(file)}:{lineNumber}");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void The_mcp_path_never_touches_the_console_because_stdout_is_the_protocol()
    {
        var mcpDirs = new[]
        {
            Path.Combine(FeaturesDir, "Mcp"),
            Path.Combine(SrcDir, "Platform", "Mcp"),
        };

        var violations = mcpDirs
            .SelectMany(CsFiles)
            .Where(f => File.ReadAllText(f).Contains("Console.", StringComparison.Ordinal))
            .Select(Relative)
            .ToList();

        Assert.Empty(violations);
    }

    private static bool Under(string file, string relativeDir) =>
        file.StartsWith(
            Path.Combine(SrcDir, relativeDir) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private static Func<string, bool> Mentions(string ns) =>
        file => File.ReadAllText(file).Contains(ns, StringComparison.Ordinal);

    private static IReadOnlyList<string> CsFiles(string dir) =>
        Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                            StringComparison.OrdinalIgnoreCase))
                .ToList()
            : [];

    private static (string Area, string Slice)? SliceOf(string file)
    {
        var parts = Path.GetRelativePath(FeaturesDir, file).Replace('\\', '/').Split('/');
        return parts.Length >= 3 ? (parts[0], parts[1]) : null;
    }

    private static string Relative(string file) => Path.GetRelativePath(RepoRoot, file);
}
