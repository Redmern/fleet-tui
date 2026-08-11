namespace Fleet.Features.Setup.RunSetup;

public static class HarnessTrouble
{
    public static string Missing(string harness) =>
        $"{harness} is not on PATH. Install it, or change what this agent opens.";
}
