namespace Fleet.Shared.Constants;

public static class FramedPane
{
    public const string Variable = "FLEET_FRAMED";

    public static bool Inside => Environment.GetEnvironmentVariable(Variable) == "1";
}
