using Fleet.Features.Head.ServeHead;
using Fleet.Ports.Settings;
using Fleet.Shared.Iso;
using Fleet.Shared.Iso.Models;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Head;

public sealed partial class HeadServiceTests
{
    private readonly IsoSwitch _iso = new();

    private void IsoOn() => _iso.Config = IsoConfig.Off with { On = true };

    [Fact]
    public async Task In_iso_mode_list_agents_returns_codes_and_states_only()
    {
        IsoOn();
        Open(Web, Idle);
        _agents.Records["web"] =
        [
            Worker("site", "login", status: "done", summary: "merged the ACME login page", open: true),
            Worker("site", "billing", status: "waiting", open: true),
        ];

        var result = await Service().ServeOriginAsync(Call(HeadTools.ListAgents));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(["sub2.agent1: waiting for input", "sub2.agent2: done"], result.Text.Split('\n'));
    }

    [Fact]
    public async Task In_iso_mode_project_structure_leaks_no_names_paths_or_reports()
    {
        IsoOn();
        _repositories["web"] = ["site"];
        _agents.Records["web"] = [Orchestrator("upgrade", "working", "two agents started"), Worker("site", "login", "upgrade")];

        var result = await Service().ServeOriginAsync(Call(HeadTools.ProjectStructure, (HeadTools.Project, "sub2")));

        Assert.False(result.IsError, result.Text);
        foreach (var secret in new[] { "web", "site", "login", "upgrade", "two agents", "C:/p" })
        {
            Assert.DoesNotContain(secret, result.Text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task In_iso_mode_a_real_project_name_is_not_an_address()
    {
        IsoOn();

        var result = await Service().ServeOriginAsync(Call(HeadTools.ProjectStructure, (HeadTools.Project, "web")));

        Assert.True(result.IsError);
        Assert.Equal(HeadIso.NoSuchCode, result.Text);
    }

    [Fact]
    public async Task In_iso_mode_a_menu_action_runs_by_code_and_answers_a_bare_ack()
    {
        IsoOn();
        Open(Web, Idle);

        var result = await Service().ServeOriginAsync(
            Call(HeadTools.MenuAction, (HeadTools.Project, "sub2"), (HeadTools.Action, "new-agent")));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(IsoProjection.Ack, result.Text);
        Assert.Equal(("web", FleetAction.NewAgent), Assert.Single(_requests.Submitted));
    }

    [Fact]
    public async Task In_iso_mode_a_failure_carries_no_detail()
    {
        IsoOn();
        _settings.Config = SettingsConfig.Default.With(HarnessTool.Dispatch, ActionPolicy.Forbid);

        var result = await Service().ServeOriginAsync(
            Call(HeadTools.Relay, (HeadTools.Project, "sub2"), (HeadTools.Prompt, "go")));

        Assert.True(result.IsError);
        Assert.Equal(HeadIso.Failed, result.Text);
    }

    [Theory]
    [InlineData(HeadTools.ShowAgent)]
    [InlineData(HeadTools.HideAgent)]
    public async Task In_iso_mode_agents_are_not_shown_on_another_machine(string tool)
    {
        IsoOn();

        var result = await Service().ServeOriginAsync(Call(tool, (HeadTools.Project, "sub2")));

        Assert.True(result.IsError);
        Assert.Equal(IsoProjection.Refused, result.Text);
    }

    [Fact]
    public async Task In_iso_mode_a_refused_listing_names_only_the_code()
    {
        IsoOn();
        Open(Web, Idle);
        _settings.Config = SettingsConfig.Default.With(HarnessTool.ListAgents, ActionPolicy.Forbid);

        var result = await Service().ServeOriginAsync(Call(HeadTools.ListAgents));

        Assert.Equal($"sub2: {IsoProjection.Refused}", result.Text);
    }

    [Fact]
    public async Task With_iso_mode_off_the_origin_sees_the_full_listing()
    {
        Open(Web, Idle);
        _agents.Records["web"] = [Worker("site", "login", status: "done", open: true)];

        var result = await Service().ServeOriginAsync(Call(HeadTools.ListAgents));

        Assert.Contains("site/login", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Iso_mode_does_not_filter_the_local_head()
    {
        IsoOn();
        Open(Web, Idle);
        _agents.Records["web"] = [Worker("site", "login", status: "done", open: true)];

        var result = await Service().HandleAsync(Call(HeadTools.ListAgents));

        Assert.Contains("site/login", result.Text, StringComparison.Ordinal);
    }

    private sealed class IsoSwitch : IIsoMode
    {
        public IsoConfig Config { get; set; } = IsoConfig.Off;

        public IsoConfig Load() => Config;

        public void Save(IsoConfig config) => Config = config;
    }
}
