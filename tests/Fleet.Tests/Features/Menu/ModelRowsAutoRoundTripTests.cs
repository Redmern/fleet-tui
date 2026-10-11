using Fleet.Features.Menu.ShowMenu;
using Fleet.Platform.Storage;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Menu;

[Collection(ConfigHomeCollection.Name)]
public sealed class ModelRowsAutoRoundTripTests : ConfigHomeFixture
{
    [Theory]
    [InlineData(FleetAction.EditSubModel)]
    [InlineData(FleetAction.EditAgentModel)]
    public void Picking_auto_persists_and_reloads_as_auto(FleetAction action)
    {
        var store = new JsonSettingsStore();
        var model = ModelRows.ModelAt(action, ModelRows.ModelIndex(action, new RoleModel(ModelChoice.Auto, ModelChoice.Inherit)))!;

        store.Save("techweb", ModelRows.With(action, store.Load("techweb"), new RoleModel(model, ModelChoice.Inherit)));

        var reloaded = ModelRows.Current(action, new JsonSettingsStore().Load("techweb"), RoleModel.Inherit);

        Assert.True(reloaded.IsAuto);
        Assert.Equal(ModelRows.ModelIndex(action, new RoleModel(ModelChoice.Auto, ModelChoice.Inherit)), ModelRows.ModelIndex(action, reloaded));
    }
}
