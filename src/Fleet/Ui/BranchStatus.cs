using Fleet.Shared;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;

namespace Fleet.Ui;

public static class BranchStatus
{
    public static string Of(BranchState state)
    {
        var text = string.Empty;

        if (state.Behind > 0)
        {
            text += $"{FleetGlyphs.Behind}{state.Behind}";
        }

        if (state.Ahead > 0)
        {
            text += $"{FleetGlyphs.Ahead}{state.Ahead}";
        }

        if (state.Dirty)
        {
            text += FleetGlyphs.Dirty;
        }

        return text;
    }

    public static IReadOnlyList<FleetSpan> Pill(string branch, BranchState state)
    {
        var spans = new List<FleetSpan>
        {
            new(FleetGlyphs.PillLeft, FleetTones.PillEdge),
            new($"{branch}  ", FleetTones.BranchName),
            new($"{FleetGlyphs.Branch} ", FleetTones.Icon),
        };

        if (state.Behind > 0)
        {
            spans.Add(new($"{FleetGlyphs.Behind}{state.Behind} ", FleetTones.Behind));
        }

        if (state.Ahead > 0)
        {
            spans.Add(new($"{FleetGlyphs.Ahead}{state.Ahead} ", FleetTones.Ahead));
        }

        if (state.Dirty)
        {
            spans.Add(new($"{FleetGlyphs.Dirty} ", FleetTones.Dirty));
        }

        spans.Add(new(FleetGlyphs.PillRight, FleetTones.PillEdge));

        return spans;
    }

    public static int PillWidth(string branch, BranchState state) =>
        Pill(branch, state).Sum(s => s.Text.Length);
}
