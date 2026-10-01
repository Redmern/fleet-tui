namespace Fleet.Shared.Constants;

public static class FloatPane
{
    public const string Variable = "FLEET_FLOAT";

    public static bool Inside => Environment.GetEnvironmentVariable(Variable) == "1";
}