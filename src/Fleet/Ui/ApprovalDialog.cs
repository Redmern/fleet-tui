using Terminal.Gui.App;

namespace Fleet.Ui;

public static class ApprovalDialog
{
    public const string Title = "Approve this action?";

    public static bool Ask(IApplication app, string summary, string tool) =>
        FleetDialog.Confirm(app, Title, [summary, string.Empty, $"tool: {tool}"], confirmText: "Allow");
}
