using Fleet.Shared;

namespace Fleet.Tests;

public abstract class ConfigHomeFixture : IDisposable
{
    private readonly IDisposable _scope;

    protected ConfigHomeFixture()
    {
        ConfigHome = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(ConfigHome);
        _scope = FleetHome.Scope(ConfigHome);
    }

    protected string ConfigHome { get; }

    public void Dispose()
    {
        _scope.Dispose();
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
