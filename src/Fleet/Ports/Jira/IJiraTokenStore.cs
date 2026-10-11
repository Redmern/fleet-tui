using Fleet.Shared.Results;

namespace Fleet.Ports.Jira;

public interface IJiraTokenStore
{
    Result<string> Read(string project);
}
