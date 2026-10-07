using Fleet.Platform.Nvim;
using Fleet.Ports.Themes.Enums;
using Fleet.Shared.Themes;

namespace Fleet.Tests.Platform.Nvim;

public sealed class NvimThemeTests : IDisposable
{
    private readonly string _config = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_config))
        {
            Directory.Delete(_config, recursive: true);
        }
    }

    [Fact]
    public void The_palette_module_returns_every_role_and_both_ansi_rows()
    {
        var theme = BuiltInThemes.CatppuccinMocha;
        var lua = NvimTheme.Generate(theme);

        Assert.Contains("return {", lua);
        Assert.Contains("light = false,", lua);
        Assert.Contains("name = \"catppuccin-mocha\",", lua);

        foreach (var role in ThemePalette.RoleNames)
        {
            Assert.Contains($"\n  {role} = \"{theme.Role(role)}\",\n", lua);
        }

        Assert.Contains($"ansi = {{ \"{theme.Ansi[0]}\", ", lua);
        Assert.Contains($"\"{theme.Brights[7]}\" }},", lua);
        Assert.DoesNotContain("\r", lua);
    }

    [Fact]
    public void A_light_theme_says_so() =>
        Assert.Contains("light = true,", NvimTheme.Generate(BuiltInThemes.Find("catppuccin-latte")!));

    [Fact]
    public void Quotes_in_a_custom_title_cannot_break_the_lua()
    {
        var lua = NvimTheme.Generate(BuiltInThemes.CatppuccinMocha with { Title = "My \"best\" \\ theme" });

        Assert.Contains("title = \"My \\\"best\\\" \\\\ theme\",", lua);
    }

    [Fact]
    public void Applying_writes_the_palette_next_to_init_lua_and_is_idempotent()
    {
        Directory.CreateDirectory(_config);
        File.WriteAllText(Path.Combine(_config, "init.lua"), "-- fleet");
        var target = new NvimThemeTarget(_config);

        var first = target.Apply(BuiltInThemes.CatppuccinMocha);
        var second = target.Apply(BuiltInThemes.CatppuccinMocha);

        Assert.Equal(ThemeOutcome.Applied, first.Outcome);
        Assert.Equal(ThemeOutcome.Unchanged, second.Outcome);
        Assert.Equal(
            NvimTheme.Generate(BuiltInThemes.CatppuccinMocha),
            File.ReadAllText(Path.Combine(_config, NvimTheme.PaletteFile)));
    }

    [Fact]
    public void Without_fleets_nvim_config_the_palette_is_still_written_for_later()
    {
        var applied = new NvimThemeTarget(_config).Apply(BuiltInThemes.CatppuccinMocha);

        Assert.Equal(ThemeOutcome.Applied, applied.Outcome);
        Assert.Contains("not installed yet", applied.Detail);
        Assert.True(File.Exists(Path.Combine(_config, NvimTheme.PaletteFile)));
    }

    // Install rewrites only embedded files, so the generated palette must never be one of them.
    [Fact]
    public void The_embedded_config_loads_the_theme_but_does_not_ship_a_palette()
    {
        var names = typeof(FleetNvimConfig).Assembly.GetManifestResourceNames()
            .Select(n => n.Replace('\\', '/'))
            .ToList();

        Assert.Contains("nvim/lua/fleet/theme.lua", names);
        Assert.DoesNotContain("nvim/" + NvimTheme.PaletteFile, names);
    }

    [Fact]
    public void Install_keeps_a_palette_fleet_wrote_earlier()
    {
        new NvimThemeTarget(_config).Apply(BuiltInThemes.CatppuccinMocha);

        Assert.True(FleetNvimConfig.Install(_config));

        Assert.True(File.Exists(Path.Combine(_config, NvimTheme.PaletteFile)));
        Assert.Contains("require('fleet.theme').setup()", File.ReadAllText(Path.Combine(_config, "init.lua")));
    }
}
