using Fleet.Platform.Uninstall;
using Fleet.Ports.Uninstall.Enums;
using Fleet.Ports.Uninstall.Models;

namespace Fleet.Tests.Platform.Uninstall;

public sealed class JsonDependencyManifestTests : IDisposable
{
    private readonly string _configHome = Path.Combine(Path.GetTempPath(), $"fleet-manifest-{Guid.NewGuid():N}");

    public JsonDependencyManifestTests() => Directory.CreateDirectory(_configHome);

    public void Dispose() => Directory.Delete(_configHome, recursive: true);

    private IReadOnlyList<InstalledDependency> ReadWith(string? content)
    {
        if (content is not null)
        {
            File.WriteAllText(Path.Combine(_configHome, "installed-deps.json"), content);
        }

        return new JsonDependencyManifest(_configHome).Read();
    }

    [Fact]
    public void A_valid_manifest_yields_its_entries()
    {
        var deps = ReadWith("""
            {"version":1,"deps":[
              {"tool":"nvim","manager":"pacman","package":"neovim","sudo":true,"installedAt":"2026-10-23T05:00:00Z"},
              {"tool":"yazi","manager":"winget","package":"sxyazi.yazi","sudo":false,"installedAt":"2026-10-23T05:01:00Z"},
              {"tool":"git","manager":"apt","package":"git","sudo":true,"installedAt":"2026-10-23T05:02:00Z"}
            ]}
            """);

        Assert.Equal(
            [
                new InstalledDependency(DependencyTool.Nvim, PackageManager.Pacman, "neovim", true, "2026-10-23T05:00:00Z"),
                new InstalledDependency(DependencyTool.Yazi, PackageManager.Winget, "sxyazi.yazi", false, "2026-10-23T05:01:00Z"),
                new InstalledDependency(DependencyTool.Git, PackageManager.Apt, "git", true, "2026-10-23T05:02:00Z"),
            ],
            deps);
    }

    [Fact]
    public void InstalledAt_is_kept_as_the_exact_string()
    {
        var deps = ReadWith("""{"version":1,"deps":[{"tool":"git","manager":"brew","package":"git","sudo":false,"installedAt":"not a date"}]}""");

        Assert.Equal("not a date", Assert.Single(deps).InstalledAt);
    }

    [Fact]
    public void A_missing_manifest_yields_nothing() => Assert.Empty(ReadWith(null));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("""{"version":1}""")]
    [InlineData("""{"version":1,"deps":[]}""")]
    [InlineData("""{"version":1,"deps":[{"tool":"git","manager":"brew","package":"git","sudo":"true","installedAt":"x"}]}""")]
    public void An_empty_or_unreadable_manifest_yields_nothing(string content) => Assert.Empty(ReadWith(content));

    [Theory]
    [InlineData("2")]
    [InlineData("0")]
    [InlineData("\"1\"")]
    public void A_version_other_than_1_is_unreadable(string version) =>
        Assert.Empty(ReadWith($$"""{"version":{{version}},"deps":[{"tool":"git","manager":"brew","package":"git","sudo":false,"installedAt":"x"}]}"""));

    [Theory]
    [InlineData("""{"tool":"git","manager":"zypper","package":"git","sudo":true,"installedAt":"x"}""")]
    [InlineData("""{"tool":"emacs","manager":"brew","package":"emacs","sudo":false,"installedAt":"x"}""")]
    [InlineData("""{"tool":"git","manager":"Brew2","package":"git","sudo":false,"installedAt":"x"}""")]
    [InlineData("""{"manager":"brew","package":"git","sudo":false,"installedAt":"x"}""")]
    [InlineData("""{"tool":"git","manager":"brew","sudo":false,"installedAt":"x"}""")]
    [InlineData("""{"tool":"git","manager":"brew","package":"","sudo":false,"installedAt":"x"}""")]
    [InlineData("""{"tool":"git","manager":"brew","package":"git","installedAt":"x"}""")]
    public void An_entry_with_an_unknown_or_missing_field_is_ignored_and_the_rest_kept(string bad)
    {
        var deps = ReadWith($$"""{"version":1,"deps":[{{bad}},{"tool":"yazi","manager":"brew","package":"yazi","sudo":false,"installedAt":"x"}]}""");

        Assert.Equal(DependencyTool.Yazi, Assert.Single(deps).Tool);
    }

    [Fact]
    public void Reading_never_writes_the_manifest()
    {
        var path = Path.Combine(_configHome, "installed-deps.json");
        File.WriteAllText(path, "{not json");
        var before = File.GetLastWriteTimeUtc(path);

        new JsonDependencyManifest(_configHome).Read();

        Assert.Equal("{not json", File.ReadAllText(path));
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }
}
