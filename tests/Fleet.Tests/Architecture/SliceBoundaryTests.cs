using System.Reflection;

namespace Fleet.Tests.Architecture;

/// <summary>
/// The layout rules from docs/PHASE1-PLAN.md, enforced mechanically.
///
/// Scans source text rather than using reflection: a reference inside a method
/// body is caught, which a signature-level reflection check would miss, and no
/// fragile IL parsing is needed.
///
/// Known gap, stated rather than hidden: a violation written without naming the
/// namespace — a same-namespace type, or a global using — slips past. If that
/// ever happens in practice, add NetArchTest.Rules alongside these rather than
/// replacing them.
/// </summary>
public class SliceBoundaryTests
{
    private const string PlatformNamespace = "Fleet.Platform";
    private const string FeaturesNamespace = "Fleet.Features";

    private static string RepoRoot { get; } =
        typeof(SliceBoundaryTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepoRoot")?.Value
        ?? throw new InvalidOperationException(
            "RepoRoot assembly metadata is missing — see Fleet.Tests.csproj");

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
    public void Only_Program_references_Platform_implementations()
    {
        var platformDir = Path.Combine(SrcDir, "Platform") + Path.DirectorySeparatorChar;
        var composition = Path.Combine(SrcDir, "Program.cs");

        var violations = CsFiles(SrcDir)
            .Where(f => !f.StartsWith(platformDir, StringComparison.OrdinalIgnoreCase))
            .Where(f => !string.Equals(f, composition, StringComparison.OrdinalIgnoreCase))
            .Where(Mentions(PlatformNamespace))
            .Select(Relative)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Ports_depend_on_nothing_but_Shared()
    {
        var violations = CsFiles(Path.Combine(SrcDir, "Ports"))
            .Where(f => Mentions(PlatformNamespace)(f) || Mentions(FeaturesNamespace)(f))
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
                     || Mentions("Fleet.Ports")(f))
            .Select(Relative)
            .ToList();

        Assert.Empty(violations);
    }

    private static Func<string, bool> Mentions(string ns) =>
        file => File.ReadAllText(file).Contains(ns, StringComparison.Ordinal);

    private static IReadOnlyList<string> CsFiles(string dir) =>
        Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            : [];

    /// <summary>
    /// Features/&lt;Area&gt;/&lt;Slice&gt;/... — null for a file sitting above
    /// slice level, which is the area-shared case and is allowed.
    /// </summary>
    private static (string Area, string Slice)? SliceOf(string file)
    {
        var parts = Path.GetRelativePath(FeaturesDir, file).Replace('\\', '/').Split('/');
        return parts.Length >= 3 ? (parts[0], parts[1]) : null;
    }

    private static string Relative(string file) => Path.GetRelativePath(RepoRoot, file);
}
