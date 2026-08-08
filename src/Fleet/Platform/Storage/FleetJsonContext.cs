using System.Text.Json.Serialization;

namespace Fleet.Platform.Storage;

/// <summary>
/// The on-disk shape of a project. Versioned so a later format change is
/// detectable rather than silently misread.
/// </summary>
public sealed class ProjectFile
{
    public int Version { get; set; } = 1;

    public string Name { get; set; } = string.Empty;

    public string Root { get; set; } = string.Empty;
}

/// <summary>
/// Source-generated serialization. Reflection-based System.Text.Json is not
/// AOT-safe, so every serialized type is declared here and the analyzers in
/// Fleet.csproj turn any missed one into a build error.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProjectFile))]
public partial class FleetJsonContext : JsonSerializerContext;
