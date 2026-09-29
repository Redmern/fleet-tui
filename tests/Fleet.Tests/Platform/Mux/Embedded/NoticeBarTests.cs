using Fleet.Platform.Mux.Embedded.Model;
using Fleet.Platform.Mux.Embedded.Render;
using Fleet.Platform.Notifications;

namespace Fleet.Tests.Platform.Mux.Embedded;

public class NoticeBarTests
{
    private readonly MuxModel _model = new();

    private string Bar(string client) => Composer.Compose(_model.View(client)!, _ => null, null).RowText(0);

    [Fact]
    public void The_project_pill_carries_its_open_notices_and_those_of_the_other_projects_in_its_window()
    {
        _model.Spawn("techweb", "C:/x", ["claude"]);
        _model.Spawn("api", "C:/y", ["claude"]);
        _model.Spawn("elsewhere", "C:/z", ["claude"]);
        var client = _model.Connect(80, 4, "techweb");
        _model.Show(client.Id, "api");
        _model.Show(client.Id, "techweb");

        _model.SetNotices("elsewhere", 7);
        Assert.DoesNotContain("●", Bar(client.Id));

        _model.SetNotices("api", 2);
        Assert.Contains(" techweb ● +2 ", Bar(client.Id));

        _model.SetNotices("techweb", 1);
        Assert.Contains(" techweb ● 1 +2 ", Bar(client.Id));

        _model.SetNotices("gone", 5);
        Assert.Contains(" techweb ● 1 +2 ", Bar(client.Id));
    }

    [Fact]
    public void The_notice_pill_is_the_click_target_for_the_notification_center()
    {
        _model.Spawn("techweb", "C:/x", ["claude"]);
        var client = _model.Connect(80, 4, "techweb");
        Assert.Null(Composer.NoticeSpan(_model.View(client.Id)!));

        _model.SetNotices("techweb", 4);
        var (start, end) = Composer.NoticeSpan(_model.View(client.Id)!)!.Value;

        Assert.Contains("techweb ● 4", Bar(client.Id)[start..end]);
    }

    [Fact]
    public void A_toast_escapes_what_the_agent_wrote()
    {
        Assert.Equal(
            "<toast><visual><binding template=\"ToastGeneric\"><text>fleet · a&amp;b</text><text>&lt;x&gt; &quot;y&quot;</text></binding></visual></toast>",
            DesktopToast.ToastXml("fleet · a&b", "<x> \"y\""));
    }
}
