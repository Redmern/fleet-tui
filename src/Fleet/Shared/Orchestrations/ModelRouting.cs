using Fleet.Shared.Settings.Models;

namespace Fleet.Shared.Orchestrations;

public static class ModelRouting
{
    private const string ModelKey = "model=";

    private const string EffortKey = "effort=";

    public static IReadOnlyList<string> Models { get; } = ["haiku", "sonnet", "opus", "fable"];

    public static IReadOnlyList<string> Efforts { get; } = ["low", "medium", "high", "xhigh", "max"];

    public static string BuildPrompt(ModelRoutingRequest request) =>
        "You route a coding task to the Claude model and reasoning effort that should run it. "
        + "Pick the cheapest pair that will still do the task well. "
        + "Models, cheapest to strongest: " + string.Join(", ", Models) + ". "
        + "Efforts, lowest to highest: " + string.Join(", ", Efforts) + ".\n"
        + "Reply with ONLY one line, exactly in the form model=<model> effort=<effort>, "
        + "no quotes, no explanation.\n\n"
        + "profile: " + (string.IsNullOrWhiteSpace(request.Profile) ? "none" : request.Profile.Trim()) + "\n"
        + "research: " + (request.Research ? "yes" : "no") + "\n"
        + "repository: " + (string.IsNullOrWhiteSpace(request.Repository) ? "none" : request.Repository.Trim()) + "\n"
        + "task:\n\n" + request.Task;

    public static bool IsValid(RoleModel choice) =>
        Models.Contains(choice.Model) && Efforts.Contains(choice.Effort);

    public static RoleModel? Parse(string stdout) =>
        stdout
            .Split('\n')
            .Select(ParseLine)
            .LastOrDefault(r => r is not null);

    private static RoleModel? ParseLine(string line)
    {
        var parts = line.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2 || !parts[0].StartsWith(ModelKey, StringComparison.Ordinal)
            || !parts[1].StartsWith(EffortKey, StringComparison.Ordinal))
        {
            return null;
        }

        var choice = new RoleModel(parts[0][ModelKey.Length..], parts[1][EffortKey.Length..]);

        return IsValid(choice) ? choice : null;
    }
}
