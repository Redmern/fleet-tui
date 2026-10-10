using Fleet.Shared.Constants;
using Fleet.Shared.Iso;
using Fleet.Shared.Iso.Models;

namespace Fleet.Tests.Shared.Iso;

public sealed class IsoCodesTests
{
    private static IsoCodes Codes(IsoConfig? config = null, Func<string, IEnumerable<string>>? worktrees = null) =>
        IsoCodes.Assign(config ?? IsoConfig.Off, ["zeta-bank", "acme-portal", "Acme-Portal"], worktrees ?? (_ => []));

    [Fact]
    public void Projects_get_numbered_codes_in_name_order()
    {
        var codes = Codes();

        Assert.Equal("sub1", codes.Project("acme-portal"));
        Assert.Equal("sub2", codes.Project("zeta-bank"));
        Assert.Equal("zeta-bank", codes.ProjectNamed("SUB2"));
    }

    [Fact]
    public void A_code_never_contains_the_project_name()
    {
        var codes = Codes();

        Assert.DoesNotContain("acme", codes.Project("acme-portal"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(IsoCodes.Unknown, codes.Project("not-a-project"));
    }

    [Fact]
    public void An_override_wins_and_auto_codes_skip_it()
    {
        var config = IsoConfig.Off with
        {
            Codes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["zeta-bank"] = "sub1" },
        };

        var codes = Codes(config);

        Assert.Equal("sub1", codes.Project("zeta-bank"));
        Assert.Equal("sub2", codes.Project("acme-portal"));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("fleet-head")]
    [InlineData("fleet-hidden")]
    public void Fleet_workspace_names_are_not_codes(string code) =>
        Assert.False(IsoCodes.IsValidCode(code));

    [Fact]
    public void An_invalid_override_falls_back_to_an_auto_code()
    {
        var config = IsoConfig.Off with
        {
            Codes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["zeta-bank"] = "has space" },
        };

        Assert.Equal("sub2", Codes(config).Project("zeta-bank"));
    }

    [Fact]
    public void Agents_are_numbered_within_their_project_and_map_back()
    {
        var codes = Codes(worktrees: p => p == "acme-portal" ? ["/w/acme/login", "/w/acme/api"] : []);

        Assert.Equal("sub1.agent1", codes.Agent("acme-portal", "/w/acme/api"));
        Assert.Equal("sub1.agent2", codes.Agent("acme-portal", "/w/acme/login/"));
        Assert.Equal("sub1.agent", codes.Agent("acme-portal", "/w/elsewhere"));
        Assert.Equal("/w/acme/login", codes.WorktreeOf("acme-portal", "sub1.agent2"));
        Assert.Null(codes.WorktreeOf("acme-portal", "sub1.agent9"));
    }

    [Fact]
    public void Fleet_workspaces_keep_their_names_and_hidden_ones_their_suffix()
    {
        var codes = Codes();

        Assert.Equal(FleetWorkspaces.Head, codes.Workspace(FleetWorkspaces.Head));
        Assert.Equal(FleetWorkspaces.HiddenFor("sub2"), codes.Workspace(FleetWorkspaces.HiddenFor("zeta-bank")));
        Assert.Equal(FleetWorkspaces.HiddenFor("zeta-bank"), codes.WorkspaceNamed(FleetWorkspaces.HiddenFor("sub2")));
        Assert.Equal("acme-portal", codes.WorkspaceNamed("sub1"));
        Assert.Null(codes.WorkspaceNamed("acme-portal"));
    }
}
