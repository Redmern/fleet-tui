using Fleet.Shared.Results;

namespace Fleet.Features.Repositories.RenameRepository;

public sealed class RenameRepositoryHandler
{
    public Result<string> Handle(string projectRoot, string directory, string newName)
    {
        var name = newName.Trim();

        if (name.Length == 0)
        {
            return Result<string>.Fail("A repository name is required.");
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return Result<string>.Fail("A repository name cannot contain path separators.");
        }

        var target = Path.Combine(projectRoot, name);

        if (Directory.Exists(target))
        {
            return Result<string>.Fail($"{name} already exists.");
        }

        try
        {
            Directory.Move(directory, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Fail($"Could not rename the repository: {e.Message}");
        }

        return Result<string>.Ok(target);
    }
}
