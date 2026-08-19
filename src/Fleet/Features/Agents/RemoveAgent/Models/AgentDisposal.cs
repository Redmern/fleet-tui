using Fleet.Shared.Constants;
using Fleet.Ui.Models;

namespace Fleet.Features.Agents.RemoveAgent.Models;

public static class AgentDisposal
{
    public const int Opens = 0;

    public const int Hide = 1;

    public const int Stop = 2;

    public const int Forget = 3;

    public const int Delete = 4;

    public static IReadOnlyList<string> Choices { get; } =
    [
        "Change what it opens",
        "Hide it from the terminal",
        "Stop the agent, keep everything",
        "Remove the agent, keep its files",
        "Remove the agent and delete its worktree",
    ];

    public const string ShowDetail = "Bring it back into the terminal";

    public const string RenameDetail = "Rename its branch (close it first)";

    public static IReadOnlyList<PickerEntry> For(bool hidden) => For(hidden, orchestrator: false);

    public static IReadOnlyList<PickerEntry> For(bool hidden, bool orchestrator)
    {
        var entries = new List<PickerEntry>();

        if (!orchestrator)
        {
            entries.Add(new("opens", Choices[Opens], "o"));
        }

        entries.Add(hidden
            ? new PickerEntry(AgentWords.Show, ShowDetail, "h")
            : new PickerEntry(AgentWords.Hide, Choices[Hide], "h"));
        entries.Add(new("rename", RenameDetail, "r"));
        entries.Add(new("stop", Choices[Stop], "s"));
        entries.Add(new("forget", Choices[Forget], "f"));
        entries.Add(new("delete", Choices[Delete], "d"));

        return entries;
    }

    public static IReadOnlyList<PickerEntry> Entries { get; } = For(hidden: false);
}
