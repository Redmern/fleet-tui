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

    public static IReadOnlyList<PickerEntry> For(bool hidden) =>
    [
        new("opens", Choices[Opens], "o"),
        hidden
            ? new PickerEntry(AgentWords.Show, ShowDetail, "h")
            : new PickerEntry(AgentWords.Hide, Choices[Hide], "h"),
        new("stop", Choices[Stop], "s"),
        new("forget", Choices[Forget], "f"),
        new("delete", Choices[Delete], "d"),
    ];

    public static IReadOnlyList<PickerEntry> Entries { get; } = For(hidden: false);
}
