using Fleet.Ports.Remotes.Models;
using Fleet.Ui.Models;

namespace Fleet.Features.Projects.SwitchProject;

public sealed record SwitchTarget(string Project, string? Host = null);

public sealed record SwitchTabs(
    IReadOnlyList<(string Title, IReadOnlyList<PickerEntry> Entries)> Tabs,
    IReadOnlyList<IReadOnlyList<SwitchTarget>> Targets,
    int ThisMachine)
{
    public const string OpenTitle = "Open";

    public const string AllTitle = "All";

    public const string ThisMachineTitle = "this machine";

    public const int Open = 0;

    public const string OpenDetail = "open";

    public const string InWindowDetail = "this window";

    public int MachineTab(int machine) => ThisMachine + 1 + machine;

    public (int Tab, int Entry) Start(Func<SwitchTarget, bool> isCurrent, (int Tab, int Entry) otherwise)
    {
        var open = Targets[Open];

        if (open.Count == 0)
        {
            return otherwise;
        }

        return (Open, open.Select((target, index) => (target, index)).Where(t => isCurrent(t.target)).Select(t => t.index).FirstOrDefault());
    }

    public static SwitchTabs For(
        IReadOnlyList<PickerEntry> local, IReadOnlyCollection<string> inWindow, IReadOnlyList<RemoteMachine> machines)
    {
        var localTargets = local.Select(e => new SwitchTarget(e.Label)).ToList();
        var remote = machines
            .Select(m => (
                m.Name,
                Entries: (IReadOnlyList<PickerEntry>)[.. m.Projects.Select(p => new PickerEntry(p, m.IsRunning(p) ? OpenDetail : string.Empty))],
                Targets: (IReadOnlyList<SwitchTarget>)[.. m.Projects.Select(p => new SwitchTarget(p, m.Host))],
                AllEntries: m.Projects.Select(p => new PickerEntry(p, m.IsRunning(p) ? $"{m.Name} · {OpenDetail}" : m.Name)),
                Open: m.Projects.Where(m.IsRunning).Select(p => (Entry: new PickerEntry(p, $"{m.Name} · {OpenDetail}"), Target: new SwitchTarget(p, m.Host)))))
            .ToList();
        var open = local
            .Where(e => inWindow.Contains(e.Label, StringComparer.OrdinalIgnoreCase))
            .Select(e => (Entry: new PickerEntry(e.Label, InWindowDetail), Target: new SwitchTarget(e.Label)))
            .Concat(remote.SelectMany(r => r.Open))
            .ToList();

        List<(string, IReadOnlyList<PickerEntry>)> tabs = [(OpenTitle, [.. open.Select(o => o.Entry)])];
        List<IReadOnlyList<SwitchTarget>> targets = [[.. open.Select(o => o.Target)]];

        if (remote.Count > 0)
        {
            tabs.Add((AllTitle, [.. local.Select(e => e with { Key = string.Empty }), .. remote.SelectMany(r => r.AllEntries)]));
            targets.Add([.. localTargets, .. remote.SelectMany(r => r.Targets)]);
        }

        var thisMachine = tabs.Count;
        tabs.Add((ThisMachineTitle, local));
        targets.Add(localTargets);
        tabs.AddRange(remote.Select(r => (r.Name, r.Entries)));
        targets.AddRange(remote.Select(r => r.Targets));

        return new SwitchTabs(tabs, targets, thisMachine);
    }
}
