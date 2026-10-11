using Fleet.Shared.Results;
using Fleet.Shared.Settings.Models;
using Fleet.Shared.Todos.Models;

namespace Fleet.Ports.Jira;

public interface IJiraClient
{
    Task<Result<IReadOnlyList<JiraIssue>>> SearchAsync(JiraSettings settings, string token, CancellationToken ct = default);
}
