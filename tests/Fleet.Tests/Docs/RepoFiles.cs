using System.Reflection;

namespace Fleet.Tests.Docs;

public static class RepoFiles
{
    public static string Root { get; } =
        typeof(RepoFiles).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot")
            .Value!;

    public static string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);
}
