using Fleet.Ports.Remotes.Models;
using Fleet.Ui.Models;

namespace Fleet.Features.Projects.SwitchProject;

public sealed record SwitchTarget(string Project, string? Host = null);

public sealed record SwitchTabs(
    IReadOnlyList<(string Title, IReadOnlyList<PickerEntry> Entries)> Tabs,
    IReadOnlyList<IReadOnlyList<SwitchTarget>> Targets)
{
    public const string AllTitle = "All";

    public const string ThisMachineTitle = "this machine";

    public const int ThisMachine = 1;

    public static SwitchTabs For(IReadOnlyList<PickerEntry> local, IReadOnlyList<RemoteMachine> machines)
    {
        var localTargets = local.Select(e => new SwitchTarget(e.Label)).ToList();
        var remote = machines
            .Select(m => (
                m.Name,
                Entries: (IReadOnlyList<PickerEntry>)[.. m.Projects.Select(p => new PickerEntry(p))],
                Targets: (IReadOnlyList<SwitchTarget>)[.. m.Projects.Select(p => new SwitchTarget(p, m.Host))],
                AllEntries: m.Projects.Select(p => new PickerEntry(p, m.Name))))
            .ToList();

        List<(string, IReadOnlyList<PickerEntry>)> tabs =
        [
            (AllTitle, [.. local.Select(e => e with { Key = string.Empty }), .. remote.SelectMany(r => r.AllEntries)]),
            (ThisMachineTitle, local),
            .. remote.Select(r => (r.Name, r.Entries)),
        ];

        List<IReadOnlyList<SwitchTarget>> targets =
        [
            [.. localTargets, .. remote.SelectMany(r => r.Targets)],
            localTargets,
            .. remote.Select(r => r.Targets),
        ];

        return new SwitchTabs(tabs, targets);
    }
}
