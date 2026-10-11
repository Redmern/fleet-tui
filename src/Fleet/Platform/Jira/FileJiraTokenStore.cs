using Fleet.Platform.Storage;
using Fleet.Ports.Jira;
using Fleet.Shared;
using Fleet.Shared.Results;

namespace Fleet.Platform.Jira;

public sealed class FileJiraTokenStore : IJiraTokenStore
{
    private const UnixFileMode OthersCanTouch =
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    public static string FileFor(string project) =>
        Path.Combine(FleetPaths.Config, "secrets", "jira-" + ProjectName.Sanitize(project));

    public Result<string> Read(string project)
    {
        var file = FileFor(project);

        try
        {
            if (!File.Exists(file))
            {
                return Result<string>.Fail($"no Jira API token: put it in {file} (chmod 600).");
            }

            if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(file) & OthersCanTouch) != 0)
            {
                return Result<string>.Fail($"{file} is readable by others; run chmod 600 on it.");
            }

            var token = File.ReadAllText(file).Trim();

            return token.Length == 0
                ? Result<string>.Fail($"{file} is empty; put the Jira API token in it.")
                : Result<string>.Ok(token);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Fail($"fleet could not read {file}.");
        }
    }
}
