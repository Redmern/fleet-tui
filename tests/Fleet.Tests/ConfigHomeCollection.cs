namespace Fleet.Tests;

[CollectionDefinition(Name)]
public sealed class ConfigHomeCollection
{
    public const string Name = "config-home";
}

public abstract class ConfigHomeFixture : IDisposable
{
    protected ConfigHomeFixture()
    {
        ConfigHome = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(ConfigHome);
        Environment.SetEnvironmentVariable("FLEET_CONFIG_HOME", ConfigHome);
    }

    protected string ConfigHome { get; }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("FLEET_CONFIG_HOME", null);
        TryDelete(ConfigHome);
        GC.SuppressFinalize(this);
    }

    protected static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
