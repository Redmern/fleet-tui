namespace Fleet.Features.Setup.RunSetup;

public static class MuxTrouble
{
    public const string Embedded = "embedded";

    public static string? With(string chosen, bool embeddedReady = false)
    {
        if (chosen == Embedded && embeddedReady)
        {
            return null;
        }

        return chosen == Embedded
            ? "this build cannot run the embedded multiplexer (libghostty-vt is not linked); install a release build"
            : $"the '{chosen}' driver is not implemented; fleet ships the embedded multiplexer only, so set FLEET_MUX=embedded";
    }
}
