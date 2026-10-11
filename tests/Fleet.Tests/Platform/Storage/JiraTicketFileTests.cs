using Fleet.Features.Todos.SyncJira;
using Fleet.Platform.Storage;
using Fleet.Ports.Jira;
using Fleet.Shared.Results;
using Fleet.Shared.Settings.Models;
using Fleet.Shared.Todos.Enums;
using Fleet.Shared.Todos.Models;

namespace Fleet.Tests.Platform.Storage;

public sealed class JiraTicketFileTests : IDisposable
{
    private static readonly JiraSettings Settings = new("https://acme.atlassian.net", "me@acme.test", "x", "customfield_10016");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "fleet-jira-" + Guid.NewGuid().ToString("N"));

    private readonly Client _client = new();

    private string File(string key) => Path.Combine(_root, ".fleet", "todos", key + ".md");

    private Task<Result<int>?> Pull() =>
        new SyncJiraHandler(new FileTodoStore(), new Tokens(), () => _client).HandleAsync("techweb", _root, Settings);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task A_pulled_issue_becomes_a_key_file_and_local_notes_below_the_section_survive_the_next_pull()
    {
        _client.Issues = [new JiraIssue("WEB-7", "Fix \"login\"", "line one\n\n- a\n- b", 2, false)];
        await Pull();

        var text = System.IO.File.ReadAllText(File("WEB-7"));
        Assert.Contains("source: jira", text);
        Assert.Contains("jira: WEB-7", text);
        Assert.Contains("effort: S", text);
        Assert.Contains("<!-- jira:start -->\nline one\n\n- a\n- b\n<!-- jira:end -->", text);

        System.IO.File.WriteAllText(
            File("WEB-7"),
            text.Replace("effort: S", "effort: XL", StringComparison.Ordinal) + "\n## my notes\nremember the cache\n");
        _client.Issues = [new JiraIssue("WEB-7", "Fix login for real", "new text", 2, false)];

        await Pull();

        var ticket = new FileTodoStore().Get(_root, "WEB-7")!;
        Assert.Equal("Fix login for real", ticket.Title);
        Assert.Equal(TicketEffort.XL, ticket.Effort);
        Assert.Equal(TicketSource.Jira, ticket.Source);
        Assert.Contains("<!-- jira:start -->\nnew text\n<!-- jira:end -->", ticket.Body);
        Assert.EndsWith("## my notes\nremember the cache\n", ticket.Body);
    }

    [Fact]
    public async Task A_pull_with_nothing_new_leaves_the_file_untouched()
    {
        _client.Issues = [new JiraIssue("WEB-7", "Fix login", "text", null, false)];
        await Pull();
        var before = System.IO.File.GetLastWriteTimeUtc(File("WEB-7"));
        System.IO.File.SetLastWriteTimeUtc(File("WEB-7"), before.AddHours(-1));

        await Pull();

        Assert.Equal(before.AddHours(-1), System.IO.File.GetLastWriteTimeUtc(File("WEB-7")));
    }

    private sealed class Tokens : IJiraTokenStore
    {
        public Result<string> Read(string project) => Result<string>.Ok("token");
    }

    private sealed class Client : IJiraClient
    {
        public IReadOnlyList<JiraIssue> Issues { get; set; } = [];

        public Task<Result<IReadOnlyList<JiraIssue>>> SearchAsync(JiraSettings settings, string token, CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<JiraIssue>>.Ok(Issues));
    }
}
