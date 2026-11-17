using Fleet.Features.Setup.WriteInclude.Enums;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Results;

namespace Fleet.Features.Setup.WriteInclude;

public static class IncludeWriter
{
    public static Result<IncludeWrite> Write(string configHome)
    {
        var path = OrchestratorInclude.PathIn(configHome);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            if (File.Exists(path) && File.ReadAllText(path) == OrchestratorInclude.Text)
            {
                return Result<IncludeWrite>.Ok(IncludeWrite.Unchanged);
            }

            Directory.CreateDirectory(configHome);
            File.WriteAllText(temporary, OrchestratorInclude.Text);
            File.Move(temporary, path, overwrite: true);

            return Result<IncludeWrite>.Ok(IncludeWrite.Written);
        }
        catch (Exception e)
            when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Discard(temporary);

            return Result<IncludeWrite>.Fail($"could not write {path}: {e.Message}");
        }
    }

    private static void Discard(string temporary)
    {
        if (!File.Exists(temporary))
        {
            return;
        }

        try
        {
            File.Delete(temporary);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
