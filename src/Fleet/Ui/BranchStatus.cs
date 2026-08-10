using Fleet.Ui.Constants;

using Fleet.Shared;

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

    public static string Pill(string branch) => $"{FleetGlyphs.Branch} {branch}";
}
