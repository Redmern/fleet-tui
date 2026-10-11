using System.Text.Json;
using Fleet.Cli.Commands;
using Fleet.Cli.Composition;
using Fleet.Cli.Enums;
using Fleet.Cli.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Todos;

namespace Fleet.Tests.Cli;

[Collection(ConfigHomeCollection.Name)]
public sealed class TodoCommandTests : ConfigHomeFixture
{
    private readonly StringWriter _out = new();

    private readonly StringWriter _error = new();

    private string Root
    {
        get
        {
            var root = Path.Combine(ConfigHome, "demo-root");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    private void Register() => Adapters.Projects().Save(new Project("demo", Root));

    private int Hook(string prompt) =>
        HookTodoCommand.Run(
            new Invocation(FleetVerb.HookTodo, "hook-todo", "demo", null),
            new StringReader(JsonSerializer.Serialize(new Dictionary<string, string> { ["prompt"] = prompt, ["cwd"] = Root })),
            _out,
            _error);

    private IReadOnlyList<string> Tickets() =>
        Directory.Exists(TodoPaths.Folder(Root))
            ? [.. Directory.GetFiles(TodoPaths.Folder(Root), "*.md").Select(Path.GetFileNameWithoutExtension).OfType<string>()]
            : [];

    [Fact]
    public void A_plus_prompt_files_a_ticket_and_blocks_with_a_note_naming_it()
    {
        Register();

        var exit = Hook("+ tidy the logs");

        Assert.Equal(2, exit);
        var id = Assert.Single(Tickets());
        Assert.Equal("0001-tidy-the-logs", id);
        Assert.Contains(id, _error.ToString());
        Assert.Empty(_out.ToString());
    }

    [Fact]
    public void In_json_mode_the_hook_blocks_with_a_json_reason_on_stdout()
    {
        Register();
        Environment.SetEnvironmentVariable(HookDispatchCommand.JsonModeVariable, "json");

        try
        {
            var exit = Hook("+ tidy the logs");

            Assert.Equal(2, exit);
            using var block = JsonDocument.Parse(_out.ToString());
            Assert.Contains(Assert.Single(Tickets()), block.RootElement.GetProperty("reason").GetString());
            Assert.Empty(_error.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(HookDispatchCommand.JsonModeVariable, null);
        }
    }

    [Theory]
    [InlineData("+")]
    [InlineData("+   ")]
    [InlineData("just a question")]
    public void A_bare_sigil_or_a_plain_prompt_passes_through(string prompt)
    {
        Register();

        Assert.Equal(0, Hook(prompt));
        Assert.Empty(Tickets());
        Assert.Empty(_out.ToString());
        Assert.Empty(_error.ToString());
    }

    [Fact]
    public void Fleet_todo_files_a_ticket_and_names_it()
    {
        Register();

        var exit = TodoCommand.Run(
            new Invocation(FleetVerb.Todo, "todo", "demo", null, Text: "write the docs"), _out, _error);

        Assert.Equal(0, exit);
        Assert.Equal("0001-write-the-docs", Assert.Single(Tickets()));
        Assert.Contains("0001-write-the-docs", _out.ToString());
    }

    [Fact]
    public void Fleet_todo_refuses_an_unknown_project()
    {
        var exit = TodoCommand.Run(
            new Invocation(FleetVerb.Todo, "todo", "nope", null, Text: "idea"), _out, _error);

        Assert.Equal(2, exit);
        Assert.Contains("no project named 'nope'", _error.ToString());
    }
}
