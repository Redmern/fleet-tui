using Fleet.Platform.Storage;
using Fleet.Shared.Keymap.Enums;

namespace Fleet.Tests.Platform.Storage;

[Collection(ConfigHomeCollection.Name)]
public sealed class FileActionRequestStoreTests : ConfigHomeFixture
{
    private readonly FileActionRequestStore _store = new();

    [Fact]
    public void Nothing_is_pending_until_something_is_submitted()
    {
        Assert.Equal(FleetAction.None, _store.TakePending("techweb"));
    }

    [Fact]
    public void A_submitted_action_is_picked_up_by_the_dashboard()
    {
        _store.Submit("techweb", FleetAction.AddRepository);

        Assert.Equal(FleetAction.AddRepository, _store.TakePending("techweb"));
    }

    [Fact]
    public void Taking_a_request_consumes_it_so_the_form_opens_once()
    {
        _store.Submit("techweb", FleetAction.AddRepository);

        _store.TakePending("techweb");

        Assert.Equal(FleetAction.None, _store.TakePending("techweb"));
    }

    [Fact]
    public void Requests_do_not_leak_between_projects()
    {
        _store.Submit("techweb", FleetAction.AddRepository);

        Assert.Equal(FleetAction.None, _store.TakePending("other"));
        Assert.Equal(FleetAction.AddRepository, _store.TakePending("techweb"));
    }

    [Fact]
    public void The_newest_request_wins_rather_than_queueing_up()
    {
        _store.Submit("techweb", FleetAction.AddRepository);
        _store.Submit("techweb", FleetAction.EditKeybinds);

        Assert.Equal(FleetAction.EditKeybinds, _store.TakePending("techweb"));
        Assert.Equal(FleetAction.None, _store.TakePending("techweb"));
    }

    [Fact]
    public void A_project_name_that_sanitizes_to_nothing_is_ignored()
    {
        _store.Submit("///", FleetAction.AddRepository);

        Assert.Equal(FleetAction.None, _store.TakePending("///"));
    }
}
