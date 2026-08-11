using System.Reflection;
using Fleet.Ui.Constants;

namespace Fleet.Tests.Ui;

public class FleetDialogTests
{
    private static string RepoRoot { get; } =
        typeof(FleetDialogTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot")
            .Value!;

    private static readonly string Source = File.ReadAllText(
        Path.Combine(RepoRoot, "src", "Fleet", "Ui", "FleetDialog.cs"));

    [Fact]
    public void The_confirm_buttons_move_with_h_and_l_and_the_arrows()
    {
        Assert.Contains("key == Key.H || key == Key.CursorLeft", Source);
        Assert.Contains("key == Key.L || key == Key.CursorRight", Source);
    }

    [Fact]
    public void Enter_follows_the_focused_button_rather_than_a_default_one()
    {
        Assert.DoesNotContain("FleetTheme.Primary", Source[..Source.IndexOf("public static void Error", StringComparison.Ordinal)]);
        Assert.Contains("yes.SetFocus();", Source);
    }

    [Fact]
    public void The_hint_says_how_to_move_and_that_y_and_n_still_work()
    {
        Assert.Contains("h/l move", FleetHints.Confirm);
        Assert.Contains("enter select", FleetHints.Confirm);
        Assert.Contains("y yes", FleetHints.Confirm);
        Assert.Contains("n/esc no", FleetHints.Confirm);
    }
}
