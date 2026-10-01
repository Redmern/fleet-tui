using Fleet.Features.Sessions.SaveSession;
using Fleet.Platform.Storage;
using Fleet.Ports.Sessions.Models;

namespace Fleet.Tests.Features.Sessions;

[Collection(ConfigHomeCollection.Name)]
public sealed class SaveSessionTests : ConfigHomeFixture
{
    private static readonly IReadOnlyList<WindowEntry> Window =
    [
        new("fleet", null, false),
        new("homelab", "red@far", true),
        new("pc", null, false),
    ];

    [Fact]
    public void A_session_keeps_the_windows_projects_in_order_with_their_machine_and_what_was_showing()
    {
        var session = SaveSessionHandler.For(" work ", Window);

        Assert.Equal("work", session.Name);
        Assert.Equal([new SessionProject("fleet"), new SessionProject("homelab", "red@far"), new SessionProject("pc")], session.Projects);
        Assert.Equal(new SessionProject("homelab", "red@far"), session.Showing);
        Assert.Equal("fleet, homelab @red@far, pc", SaveSessionHandler.Describe(session));
    }

    [Fact]
    public void Saving_needs_a_name_and_a_window_with_projects()
    {
        var handler = new SaveSessionHandler(new JsonSessionStore());

        Assert.False(handler.Handle("work", []).Succeeded);
        Assert.False(handler.Handle("  ", Window).Succeeded);
        Assert.True(handler.Handle("work", Window).Succeeded);
    }

    [Fact]
    public void The_store_lists_updates_and_removes_sessions_by_name()
    {
        var store = new JsonSessionStore();
        var handler = new SaveSessionHandler(store);

        handler.Handle("work", Window);
        handler.Handle("home", [new("pc", null, true)]);
        handler.Handle("work", [new("fleet", null, true)]);

        Assert.Equal(["home", "work"], store.List().Select(s => s.Name));
        Assert.Equal([new SessionProject("fleet")], store.List().Single(s => s.Name == "work").Projects);

        Assert.True(store.Remove("home"));
        Assert.False(store.Remove("home"));
        Assert.Equal(["work"], store.List().Select(s => s.Name));
    }
}