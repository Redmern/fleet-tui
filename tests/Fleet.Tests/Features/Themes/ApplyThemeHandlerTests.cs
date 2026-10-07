using Fleet.Features.Themes.ApplyTheme;
using Fleet.Ports.Themes;
using Fleet.Ports.Themes.Enums;
using Fleet.Ports.Themes.Models;
using Fleet.Shared.Themes;

namespace Fleet.Tests.Features.Themes;

public sealed class ApplyThemeHandlerTests
{
    [Fact]
    public void Every_target_gets_the_theme_and_reports_in_order()
    {
        var first = new FakeTarget("one", t => ThemeApplied.Applied("one", t.Name));
        var second = new FakeTarget("two", _ => ThemeApplied.Skipped("two", "not installed"));

        var applied = new ApplyThemeHandler([first, second]).Handle(BuiltInThemes.CatppuccinMocha);

        Assert.Equal(["one", "two"], applied.Select(a => a.Tool));
        Assert.Equal([ThemeOutcome.Applied, ThemeOutcome.Skipped], applied.Select(a => a.Outcome));
        Assert.Equal(BuiltInThemes.DefaultName, first.Seen?.Name);
    }

    // One tool's broken config must not stop the others from following the theme.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_target_that_throws_fails_alone(bool io)
    {
        var broken = new FakeTarget(
            "broken", _ => throw (io ? new IOException("disk full") : new ArgumentException("disk full")));
        var fine = new FakeTarget("fine", _ => ThemeApplied.Applied("fine", "ok"));

        var applied = new ApplyThemeHandler([broken, fine]).Handle(BuiltInThemes.CatppuccinMocha);

        Assert.Equal(ThemeOutcome.Failed, applied[0].Outcome);
        Assert.Equal("disk full", applied[0].Detail);
        Assert.Equal(ThemeOutcome.Applied, applied[1].Outcome);
    }

    [Fact]
    public void A_report_line_names_the_tool_and_outcome() =>
        Assert.Equal("yazi     skipped: your own file", ThemeApplied.Skipped("yazi", "your own file").Line);

    private sealed class FakeTarget(string tool, Func<ThemePalette, ThemeApplied> apply) : IThemeTarget
    {
        public ThemePalette? Seen { get; private set; }

        public string Tool => tool;

        public ThemeApplied Apply(ThemePalette theme)
        {
            Seen = theme;
            return apply(theme);
        }
    }
}
