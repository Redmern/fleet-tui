using System.Text;

namespace Fleet.Platform.Mux.WezTerm;

public static class WezTermUserVars
{
    public const string FleetVar = "fleet";

    public const string DashboardValue = "dashboard";

    public static string Sequence(string name, string value)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        return $"]1337;SetUserVar={name}={encoded}";
    }

    public static void Mark(string name, string value)
    {
        try
        {
            Console.Out.Write(Sequence(name, value));
            Console.Out.Flush();
        }
        catch (IOException)
        {
        }
    }

    public static void MarkDashboard() => Mark(FleetVar, DashboardValue);
}
