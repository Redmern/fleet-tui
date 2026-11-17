using Fleet.Features.Updates.RunUpdate.Models;
using Fleet.Shared.Releases;

namespace Fleet.Features.Updates.RunUpdate;

public static class IncludeRefresh
{
    private const string Include = "ORCHESTRATOR.md";

    public static string? After(
        RunUpdateCommand command,
        UpdateOutcome outcome,
        Func<string, bool> installedWrites,
        Func<string?> writeHere)
    {
        if (!outcome.Installed)
        {
            return writeHere() is { } error ? $"could not write {Include}: {error}" : null;
        }

        if (outcome.Version is not { } installed || !VersionCompare.IsNewer(installed, command.CurrentVersion))
        {
            return $"left {Include} as it is: {outcome.Version} is older than this fleet ({command.CurrentVersion}).";
        }

        if (installedWrites(command.ExecutablePath))
        {
            return $"{Include} now carries the orchestrator instructions of {outcome.Version}.";
        }

        var note = $"the new fleet could not write {Include}, ";

        return writeHere() is { } failed
            ? note + $"and this fleet ({command.CurrentVersion}) could not either: {failed}. Run 'fleet setup'."
            : note + $"so this fleet ({command.CurrentVersion}) wrote its own text. Run 'fleet setup' to refresh it.";
    }
}
