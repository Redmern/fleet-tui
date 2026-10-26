using System.Text.Json;
using Fleet.Cli.Commands;
using Fleet.Cli.Composition;
using Fleet.Cli.Enums;
using Fleet.Cli.Models;
using Fleet.Ports.Projects.Models;

namespace Fleet.Tests.Cli;

[Collection(ConfigHomeCollection.Name)]
public sealed class HookDispatchCommandTests : ConfigHomeFixture
{
    private readonly StringWriter _out = new();

    private string Root
    {
        get
        {
            var root = Path.Combine(ConfigHome, "demo-root");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    private Task<int> Hook(string prompt)
    {
        Adapters.Projects().Save(new Project("demo", Root));

        return HookDispatchCommand.RunAsync(
            new Invocation(FleetVerb.HookDispatch, "hook-dispatch", "demo", null),
            new StringReader(JsonSerializer.Serialize(new Dictionary<string, string> { ["prompt"] = prompt, ["cwd"] = Root })),
            _out);
    }

    [Theory]
    [InlineData("just a question")]
    [InlineData(",")]
    [InlineData(",   ")]
    [InlineData("<cross-session-message from=\"uds:x\" from-name=\"head\">\n, task\n</cross-session-message>")]
    public async Task A_prompt_the_hook_does_not_take_passes_through_with_no_output(string prompt)
    {
        var exit = await Hook(prompt);

        Assert.Equal(0, exit);
        Assert.Empty(_out.ToString());
    }
}
