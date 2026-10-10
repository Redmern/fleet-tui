using Fleet.Features.Iso.ToggleIso;
using Fleet.Features.Iso.ToggleIso.Models;
using Fleet.Ports.Settings;
using Fleet.Shared.Iso.Models;

namespace Fleet.Tests.Features.Iso;

public sealed class ToggleIsoHandlerTests
{
    private static readonly IsoCaller Local = new(InteractiveInput: true, OverSsh: false, InAgent: false);

    private readonly MemoryIso _iso = new();

    private IsoReply Run(IsoCaller caller, bool confirm = true, params string[] args) =>
        new ToggleIsoHandler(_iso).Handle(args, caller, () => confirm);

    [Fact]
    public void On_turns_iso_mode_on()
    {
        var reply = Run(Local, args: "on");

        Assert.Equal(0, reply.Exit);
        Assert.True(_iso.Config.On);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void Only_a_local_terminal_may_toggle(bool tty, bool ssh, bool agent)
    {
        var reply = Run(new IsoCaller(tty, ssh, agent), args: "on");

        Assert.Equal(1, reply.Exit);
        Assert.False(_iso.Saved);
    }

    [Fact]
    public void Status_is_refused_over_ssh_too()
    {
        _iso.Config = _iso.Config with { Codes = new Dictionary<string, string> { ["acme"] = "sub9" } };

        var reply = Run(Local with { OverSsh = true }, args: "status");

        Assert.Equal(1, reply.Exit);
        Assert.DoesNotContain("acme", reply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Off_needs_a_confirmation()
    {
        _iso.Config = _iso.Config with { On = true };

        var refused = Run(Local, confirm: false, args: "off");

        Assert.Equal(1, refused.Exit);
        Assert.True(_iso.Config.On);

        Run(Local, confirm: true, args: "off");

        Assert.False(_iso.Config.On);
    }

    [Fact]
    public void Allow_and_disallow_edit_the_attach_list()
    {
        Run(Local, args: ["allow", "laptop.lan"]);
        Run(Local, args: ["allow", "LAPTOP.lan"]);
        Assert.Equal(["LAPTOP.lan"], _iso.Config.AttachFrom);

        Run(Local, args: ["disallow", "laptop.lan"]);
        Assert.Empty(_iso.Config.AttachFrom);
    }

    [Fact]
    public void Code_sets_and_clears_an_override()
    {
        Assert.Equal(0, Run(Local, args: ["code", "acme", "alpha"]).Exit);
        Assert.Equal("alpha", _iso.Config.Codes["acme"]);

        Assert.Equal(2, Run(Local, args: ["code", "zeta", "alpha"]).Exit);
        Assert.Equal(2, Run(Local, args: ["code", "zeta", "has space"]).Exit);

        Run(Local, args: ["code", "acme"]);
        Assert.Empty(_iso.Config.Codes);
    }

    [Fact]
    public void Status_lists_the_mode_hosts_and_codes()
    {
        _iso.Config = new IsoConfig(true, ["laptop.lan"], new Dictionary<string, string> { ["acme"] = "alpha" });

        var text = Run(Local, args: "status").Text;

        Assert.Contains("ISO mode: on", text, StringComparison.Ordinal);
        Assert.Contains("laptop.lan", text, StringComparison.Ordinal);
        Assert.Contains("acme -> alpha", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_word_prints_the_usage() =>
        Assert.Equal(2, Run(Local, args: "sideways").Exit);

    private sealed class MemoryIso : IIsoMode
    {
        public IsoConfig Config { get; set; } = IsoConfig.Off;

        public bool Saved { get; private set; }

        public IsoConfig Load() => Config;

        public void Save(IsoConfig config)
        {
            Config = config;
            Saved = true;
        }
    }
}
