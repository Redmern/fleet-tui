using Fleet.Shared.Iso;
using Fleet.Shared.Iso.Models;

namespace Fleet.Tests.Shared.Iso;

public sealed class IsoGuardTests
{
    [Fact]
    public void Outbound_work_is_refused_in_iso_mode()
    {
        var guarded = IsoGuard.Outbound(IsoConfig.Off with { On = true }, IsoGuard.Sync);

        Assert.False(guarded.Succeeded);
        Assert.Equal("ISO mode is on: fleet does not sync to other machines from this machine.", guarded.Error);
    }

    [Fact]
    public void Outbound_work_goes_ahead_with_iso_mode_off() =>
        Assert.True(IsoGuard.Outbound(IsoConfig.Off, IsoGuard.Sync).Succeeded);
}
