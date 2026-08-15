using Fleet.Shared.Settings.Enums;

namespace Fleet.Shared.Settings.Models;

public sealed record ToolRule(ActionPolicy Policy, AskChannel Channel)
{
    public static readonly ToolRule Ask = new(ActionPolicy.Ask, AskChannel.Both);

    public static readonly ToolRule Allow = new(ActionPolicy.Allow, AskChannel.Both);
}
