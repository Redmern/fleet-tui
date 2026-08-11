using Fleet.Features.Dashboard.ShowDashboard.Models;
using Fleet.Features.Repositories;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;

namespace Fleet.Tests.Features.Repositories;

public class RepositoryChoresTests
{
    [Fact]
    public void The_manage_menu_carries_pull_remove_and_secrets()
    {
        Assert.Equal(4, RepositoryChores.Entries.Count);

        Assert.Equal(
            ["branch", "pull", "remove", "secrets"],
            RepositoryChores.Entries.Select(e => e.Label));

        Assert.Equal(RepositoryChores.Choices, RepositoryChores.Entries.Select(e => e.Detail));
    }

    [Fact]
    public void Its_keys_read_as_mnemonics()
    {
        var keys = PickerKeys.For([.. RepositoryChores.Entries.Select(e => e.Label)]);

        Assert.Equal(["b", "p", "r", "s"], keys);
    }

    [Fact]
    public void A_chore_can_hand_the_dashboard_back_the_work_it_owns()
    {
        Assert.Equal(FleetAction.None, RepositoryManaged.Nothing.Follow);
        Assert.Null(RepositoryManaged.Nothing.Status);

        var pull = RepositoryManaged.Then(FleetAction.PullRepository);

        Assert.Equal(FleetAction.PullRepository, pull.Follow);
        Assert.Null(pull.Status);
    }
}
