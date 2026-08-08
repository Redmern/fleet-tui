namespace Fleet.Tests;

/// <summary>
/// Serializes every test class that redirects FLEET_CONFIG_HOME.
///
/// Environment variables are process-global, and xunit runs test classes in
/// parallel by default, so without this two classes pointing the config
/// directory at different temp folders would interfere non-deterministically.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ConfigHomeCollection
{
    public const string Name = "config-home";
}

/// <summary>
/// Redirects FLEET_CONFIG_HOME at a fresh temp directory for the life of one
/// test class, and removes it afterwards.
/// </summary>
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
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
