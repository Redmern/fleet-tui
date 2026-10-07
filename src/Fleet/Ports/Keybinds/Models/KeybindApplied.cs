using Fleet.Ports.Keybinds.Enums;
using Fleet.Shared.Keybinds;
using Fleet.Shared.Keybinds.Enums;

namespace Fleet.Ports.Keybinds.Models;

public sealed record KeybindApplied(KeybindTarget Target, string Path, KeybindOutcome Outcome, string Detail = "")
{
    public string Line
    {
        get
        {
            var what = Outcome switch
            {
                KeybindOutcome.Current => "up to date",
                KeybindOutcome.Written => "wrote",
                KeybindOutcome.Stale => "would write",
                KeybindOutcome.Skipped => "skipped",
                _ => "FAILED",
            };

            var line = $"{KeybindNames.Of(Target),-7} {what,-11} {Path}";

            return Detail.Length == 0 ? line : $"{line} ({Detail})";
        }
    }
}
