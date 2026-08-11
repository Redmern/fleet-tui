using Fleet.Features.Repositories.Secrets.Models;
using Fleet.Ui.Models;

namespace Fleet.Features.Repositories.Secrets;

public static class SecretsRows
{
    public const string Empty = "(no secret files yet - put them in the folder below)";

    public const string Open = "open";

    public const string Copy = "copy";

    public static IReadOnlyList<FleetRow> For(SecretsPlan plan)
    {
        if (plan.Files.Count == 0)
        {
            return [FleetRow.Plain(Empty)];
        }

        return [.. plan.Files.Select(FleetRow.Plain)];
    }

    public static string Title(SecretsPlan plan) =>
        $"{plan.Repository} secrets - {plan.Files.Count} file(s), {plan.Worktrees.Count} worktree(s)";

    public static IReadOnlyList<PickerEntry> Actions(SecretsPlan plan) =>
    [
        new(Open, $"Edit them in {plan.Root}", "o"),
        new(Copy, $"Copy them into {plan.Worktrees.Count} worktree(s) now", "c"),
    ];
}
