using Fleet.Features.Todos.SyncJira;
using Fleet.Platform.Jira;
using Fleet.Shared.Results;

namespace Fleet.Cli.Composition;

public sealed class JiraPulls(Func<JiraRestClient>? client = null) : IDisposable
{
    private readonly Lock _gate = new();

    private readonly List<JiraRestClient> _made = [];

    private bool _disposed;

    public Func<CancellationToken, Task<Result<int>>>? For(string project, string projectRoot)
    {
        if (Adapters.Settings().Load(project).Jira is not { } jira)
        {
            return null;
        }

        JiraRestClient? made = null;
        var sync = new SyncJiraHandler(Adapters.Todos(), new FileJiraTokenStore(), () => made ??= Make());

        return async ct => await sync.HandleAsync(project, projectRoot, jira, ct).ConfigureAwait(false) ?? Result<int>.Ok(0);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;

            foreach (var made in _made)
            {
                made.Dispose();
            }

            _made.Clear();
        }
    }

    private JiraRestClient Make()
    {
        var made = (client ?? (() => new JiraRestClient()))();

        lock (_gate)
        {
            if (!_disposed)
            {
                _made.Add(made);
                return made;
            }
        }

        made.Dispose();
        return made;
    }
}
