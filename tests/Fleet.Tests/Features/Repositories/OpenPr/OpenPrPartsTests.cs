using System.Text;
using Fleet.Features.Repositories.OpenPr;
using Fleet.Ports.Forge.Enums;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Repositories.OpenPr;

public sealed class OpenPrPartsTests
{
    // AC-24: the order is override, then a github.com push URL, then the probe.
    [Theory]
    [InlineData("forgejo", "git@github.com:acme/api.git", ForgeProbeResult.NotAForge, ForgeKind.Forgejo, 0)]
    [InlineData("gitea", "https://github.com/acme/api", ForgeProbeResult.NotAForge, ForgeKind.Gitea, 0)]
    [InlineData("github", "git@git.example.test:acme/api.git", ForgeProbeResult.ForgejoOrGitea, ForgeKind.GitHub, 0)]
    [InlineData("", "git@github.com:acme/api.git", ForgeProbeResult.ForgejoOrGitea, ForgeKind.GitHub, 0)]
    [InlineData("", "https://github.com/acme/api.git", ForgeProbeResult.ForgejoOrGitea, ForgeKind.GitHub, 0)]
    [InlineData("", "ssh://git@GitHub.com/acme/api.git", ForgeProbeResult.ForgejoOrGitea, ForgeKind.GitHub, 0)]
    [InlineData("", "git@git.example.test:acme/api.git", ForgeProbeResult.ForgejoOrGitea, ForgeKind.Forgejo, 1)]
    [InlineData("", "https://git.example.test/acme/api.git", ForgeProbeResult.ForgejoOrGitea, ForgeKind.Forgejo, 1)]
    public async Task The_forge_is_the_override_then_github_then_what_the_host_answers(
        string setting, string pushUrl, ForgeProbeResult answer, ForgeKind expected, int probes)
    {
        var probe = new FakeForgeProbe(answer);

        var forge = await ForgeDecision.DecideAsync(
            "api", RepositorySettings.From(setting, null), pushUrl, probe);

        Assert.True(forge.Succeeded, forge.Error);
        Assert.Equal(expected, forge.Value);
        Assert.Equal(probes, probe.Hosts.Count);
    }

    [Fact]
    public async Task The_probe_is_asked_for_the_host_of_the_push_url()
    {
        var probe = new FakeForgeProbe(ForgeProbeResult.ForgejoOrGitea);

        await ForgeDecision.DecideAsync("api", RepositorySettings.Default, "git@Git.Example.test:acme/api.git", probe);

        Assert.Equal(["git.example.test"], probe.Hosts);
    }

    [Theory]
    [InlineData("git@192.168.0.213:redmer/fleet-tui.git", ForgeProbeResult.Unreachable)]
    [InlineData("https://git.example.test/acme/api.git", ForgeProbeResult.NotAForge)]
    [InlineData("/srv/git/api.git", ForgeProbeResult.ForgejoOrGitea)]
    public async Task A_forge_fleet_cannot_tell_fails_naming_the_override_setting_and_its_values(
        string pushUrl, ForgeProbeResult answer)
    {
        var forge = await ForgeDecision.DecideAsync(
            "fleet-tui", RepositorySettings.Default, pushUrl, new FakeForgeProbe(answer));

        Assert.False(forge.Succeeded);
        Assert.Contains("repositories.fleet-tui.forge", forge.Error);
        Assert.Contains("\"forge\"", forge.Error);
        Assert.Contains("github, forgejo, gitea", forge.Error);
        Assert.Contains("Nothing was pushed", forge.Error);
    }

    [Theory]
    [InlineData("git@github.com:acme/api.git", "github.com", "acme/api", "github.com")]
    [InlineData("https://github.com/acme/api", "github.com", "acme/api", "github.com")]
    [InlineData("https://git.home.example/redmer/fleet-tui.git", "git.home.example", "redmer/fleet-tui", "git.home.example")]
    [InlineData("https://user:secret@Git.Example.test:3000/acme/api.git", "git.example.test", "acme/api", "git.example.test:3000")]
    [InlineData("ssh://git@git.example.test:2222/acme/api.git", "git.example.test", "acme/api", "git.example.test")]
    [InlineData("git@192.168.0.213:redmer/fleet-tui.git", "192.168.0.213", "redmer/fleet-tui", "192.168.0.213")]
    public void A_remote_url_gives_its_host_its_repository_and_where_to_ask_for_the_forge(
        string url, string host, string slug, string probed)
    {
        var address = RemoteAddress.Parse(url)!;

        Assert.Equal((host, slug, probed), (address.Host, address.Slug, address.ProbeTarget));
    }

    [Fact]
    public void A_web_remote_on_another_port_keeps_the_port_in_its_web_address_and_for_gh()
    {
        var address = RemoteAddress.Parse("https://ghe.example.test:8443/acme/api.git")!;

        Assert.Equal("https://ghe.example.test:8443/acme/api", address.WebBase);
        Assert.Equal("ghe.example.test:8443/acme/api", address.GitHubRepository);
        Assert.Equal("acme/api", RemoteAddress.Parse("git@github.com:acme/api.git")!.GitHubRepository);
        Assert.Null(RemoteAddress.Parse("git@git.example.test:acme/api.git")!.WebBase);
    }

    [Theory]
    [InlineData("https://user:secret@git.example.test/acme/api.git", "https://git.example.test/acme/api.git")]
    [InlineData("https://git.example.test/acme/api.git", "https://git.example.test/acme/api.git")]
    [InlineData("git@git.example.test:acme/api.git", "git@git.example.test:acme/api.git")]
    [InlineData("https://git.example.test/a@b/api.git", "https://git.example.test/a@b/api.git")]
    public void A_password_in_a_push_url_is_left_out_of_what_fleet_says(string url, string shown) =>
        Assert.Equal(shown, RemoteAddress.Redacted(url));

    [Fact]
    public async Task A_forge_on_another_port_is_asked_on_that_port_and_no_error_shows_a_password()
    {
        var probe = new FakeForgeProbe(ForgeProbeResult.NotAForge);

        var forge = await ForgeDecision.DecideAsync(
            "api", RepositorySettings.Default, "https://user:secret@git.example.test:3000/acme/api.git", probe);

        Assert.Equal(["git.example.test:3000"], probe.Hosts);
        Assert.DoesNotContain("secret", forge.Error);

        var path = await ForgeDecision.DecideAsync(
            "api", RepositorySettings.Default, "file://user:secret@/srv/api.git", probe);

        Assert.DoesNotContain("secret", path.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("DISABLED")]
    [InlineData("/srv/git/api.git")]
    [InlineData(@"C:\repos\api.git")]
    [InlineData("../api.git")]
    [InlineData("file:///srv/git/api.git")]
    public void A_path_or_a_disabled_url_names_no_host(string url) => Assert.Null(RemoteAddress.Parse(url));

    [Theory]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("DISABLED", false)]
    [InlineData("disabled", false)]
    [InlineData(" Disabled ", false)]
    [InlineData("git@git.example.test:acme/api.git", true)]
    public void An_empty_or_disabled_push_url_is_never_pushed_to(string url, bool pushable) =>
        Assert.Equal(pushable, PushRemote.Pushable(url));

    [Fact]
    public void The_agit_push_names_the_base_the_topic_the_title_and_the_description()
    {
        var args = AgitPush.Args("origin", "feat/open-pr", "main", "feat: open pull requests", "One line.");

        Assert.Equal(
            [
                "push", "origin", "refs/heads/feat/open-pr:refs/for/main",
                "-o", "topic=feat/open-pr",
                "-o", "title=feat: open pull requests",
                "-o", "description=One line.",
            ],
            args);
    }

    [Fact]
    public void A_body_of_several_lines_goes_as_base64_and_decodes_to_the_same_text()
    {
        const string Body = "## Summary\nOpens pull requests.\n\n## Evidence\n- Tests: 6700 passed ✓\n";

        var description = AgitPush.Args("origin", "feat/x", "main", "t", Body)[^1];

        Assert.StartsWith("description={base64}", description);

        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(description["description={base64}".Length..]));

        Assert.Equal(Body.TrimEnd('\n'), decoded);
        Assert.DoesNotContain('\n', description);
    }

    [Fact]
    public void Windows_line_endings_in_the_body_become_plain_newlines()
    {
        var description = AgitPush.Description("a\r\nb\r\n");

        Assert.Equal("a\nb", Encoding.UTF8.GetString(Convert.FromBase64String(description["{base64}".Length..])));
    }

    [Fact]
    public void A_one_line_body_that_looks_like_a_marker_is_encoded_too() =>
        Assert.StartsWith("{base64}", AgitPush.Description("{base64}abc"));

    [Fact]
    public void A_title_is_kept_on_one_line() =>
        Assert.Equal("title=fix: a b", AgitPush.Args("origin", "b", "main", " fix: a\r\n b \n", "x")[6]);

    // Fixtures: rebuilt from the lines earlier Deliver pushes of this repository recorded in their progress files
    // ("Forgejo answered \"Visit the existing pull request: …/pulls/62\" and `* [new reference] … -> refs/pull/62/head`",
    // "PR #52 https://git.home.redmonet.nl/redmer/fleet-tui/pulls/52"). The two-line shape is how Forgejo and Gitea
    // print it from their post-receive hook. No full capture of a push exists; the live shape is checked at Deliver.
    private const string TwoLines =
        """
        remote:
        remote: Visit the existing pull request:
        remote:   https://git.home.redmonet.nl/redmer/fleet-tui/pulls/52
        remote:
        To 192.168.0.213:redmer/fleet-tui.git
         * [new reference]     feat/sandbox-smoke -> refs/for/main
        """;

    private const string OneLine =
        """
        remote: Visit the existing pull request: https://git.home.redmonet.nl/redmer/fleet-tui/pulls/62
        To 192.168.0.213:redmer/fleet-tui.git
         * [new reference]     HEAD -> refs/pull/62/head
        """;

    [Theory]
    [InlineData(TwoLines, "https://git.home.redmonet.nl/redmer/fleet-tui/pulls/52")]
    [InlineData(OneLine, "https://git.home.redmonet.nl/redmer/fleet-tui/pulls/62")]
    [InlineData("https://github.com/acme/api/pull/7\n", "https://github.com/acme/api/pull/7")]
    [InlineData(
        "remote: Create a new pull request for 'feat/x':\nremote:   https://git.example.test/a/b/compare/main...feat/x\n"
        + "remote: Visit the existing pull request:\nremote:   https://git.example.test/a/b/pulls/3\n",
        "https://git.example.test/a/b/pulls/3")]
    public void The_pull_request_url_is_read_from_what_the_forge_printed(string output, string url) =>
        Assert.Equal(url, PrUrl.Find(output));

    [Theory]
    [InlineData("To 192.168.0.213:redmer/fleet-tui.git\n * [new reference]     feat/x -> refs/for/main\n")]
    [InlineData("remote: Create a new pull request for 'feat/x':\nremote:   https://git.example.test/a/b/compare/main...feat/x\n")]
    [InlineData("")]
    public void Output_without_a_pull_request_url_gives_none(string output) => Assert.Null(PrUrl.Find(output));

    [Fact]
    public void The_pull_request_number_is_read_from_the_pull_ref()
    {
        Assert.Equal("62", PrUrl.Number(" * [new reference]     HEAD -> refs/pull/62/head"));
        Assert.Null(PrUrl.Number(" * [new reference]     feat/x -> refs/for/main"));
    }

    [Theory]
    [InlineData("feat/open-pr", true)]
    [InlineData("main", true)]
    [InlineData("", false)]
    [InlineData("-f", false)]
    [InlineData("+feat/x", false)]
    [InlineData("--force", false)]
    [InlineData("a b", false)]
    [InlineData("a:b", false)]
    [InlineData("a\nb", false)]
    public void A_name_that_git_could_read_as_an_option_or_a_refspec_is_refused(string name, bool usable) =>
        Assert.Equal(usable, GitNames.Refusal("branch", name) is null);
}
