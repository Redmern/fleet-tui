namespace Fleet.Cli.Commands;

public static class SwitchProjectPlan
{
    public static (bool Focus, bool IntoThisWindow) Resolve(
        bool dashOpen, bool parked, bool here, bool newWindow)
    {
        var focus = dashOpen && !parked && here != newWindow;

        return (focus, !newWindow);
    }
}
