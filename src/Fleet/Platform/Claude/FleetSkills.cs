namespace Fleet.Platform.Claude;

public static class FleetSkills
{
    public static readonly IReadOnlyList<string> Names =
        ["fl-discuss", "fl-tdd", "fl-review", "fl-pr", "fl-map"];

    public static IReadOnlyDictionary<string, string> Embedded()
    {
        var assembly = typeof(FleetSkills).Assembly;
        var skills = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var resource in assembly.GetManifestResourceNames())
        {
            var parts = resource.Replace('\\', '/').Split('/');

            if (parts is not ["skills", var name, "SKILL.md"] || !Names.Contains(name))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resource);

            if (stream is null)
            {
                continue;
            }

            using var reader = new StreamReader(stream);
            skills[name] = reader.ReadToEnd();
        }

        return skills;
    }

    public static void WriteTo(string folder) => WriteTo(folder, Embedded());

    public static void WriteTo(string folder, IReadOnlyDictionary<string, string> skills)
    {
        foreach (var (name, content) in skills)
        {
            var path = Path.Combine(folder, ".claude", "skills", name, "SKILL.md");
            var text = content.Replace("\r\n", "\n", StringComparison.Ordinal);

            try
            {
                if (File.Exists(path) && File.ReadAllText(path) == text)
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
