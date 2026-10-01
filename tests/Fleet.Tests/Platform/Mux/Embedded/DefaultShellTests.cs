using Fleet.Platform.Mux.Embedded.Host;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class DefaultShellTests
{
    // Windows Terminal writes JSON with comments and trailing commas.
    private const string Settings = """
        {
            // the one new tabs open
            "defaultProfile": "{771d8af3-bc33-41e8-a761-e0875d661369}",
            "profiles": {
                "list": [
                    { "guid": "{0caa0dad-35be-5f56-a8ff-afceeeaa6101}", "name": "Command Prompt", "commandline": "%SystemRoot%\\System32\\cmd.exe" },
                    { "guid": "{771d8af3-bc33-41e8-a761-e0875d661369}", "name": "Fleet", "commandline": "\"C:\\Program Files\\PowerShell\\7\\pwsh.exe\" -NoLogo", },
                    { "guid": "{574e775e-4f2a-5b96-ac1e-a2962a402336}", "name": "PowerShell", "source": "Windows.Terminal.PowershellCore" },
                ],
            },
        }
        """;

    private static IReadOnlyList<string> Resolve(
        bool windows, Dictionary<string, string>? vars = null, string[]? onPath = null, string? settings = null) =>
        DefaultShell.Resolve(
            name => (vars ?? []).GetValueOrDefault(name),
            program => (onPath ?? []).Contains(program),
            () => settings,
            windows);

    [Fact]
    public void Windows_terminals_default_profile_is_what_a_new_pane_opens()
    {
        Assert.Equal([@"C:\Program Files\PowerShell\7\pwsh.exe", "-NoLogo"], Resolve(true, settings: Settings));
    }

    [Fact]
    public void FLEET_SHELL_wins_over_everything()
    {
        Assert.Equal(["nu", "--login"], Resolve(true, new() { ["FLEET_SHELL"] = "nu --login" }, ["pwsh"], Settings));
    }

    [Fact]
    public void A_default_profile_without_a_command_line_falls_back_to_pwsh_then_comspec()
    {
        var generated = Settings.Replace(
            "\"defaultProfile\": \"{771d8af3-bc33-41e8-a761-e0875d661369}\"",
            "\"defaultProfile\": \"{574e775e-4f2a-5b96-ac1e-a2962a402336}\"",
            StringComparison.Ordinal);

        Assert.Equal(["pwsh.exe", "-NoLogo"], Resolve(true, onPath: ["pwsh"], settings: generated));
        Assert.Equal([@"C:\Windows\system32\cmd.exe"], Resolve(true, new() { ["COMSPEC"] = @"C:\Windows\system32\cmd.exe" }));
    }

    [Fact]
    public void Unix_uses_SHELL()
    {
        Assert.Equal(["/bin/zsh"], Resolve(false, new() { ["SHELL"] = "/bin/zsh" }));
        Assert.Equal(["/bin/sh"], Resolve(false));
    }

    [Fact]
    public void Broken_settings_are_ignored()
    {
        Assert.Null(DefaultShell.DefaultProfileCommand("{ not json"));
        Assert.Null(DefaultShell.DefaultProfileCommand(null));
    }

    [Fact]
    public void A_command_line_splits_on_spaces_outside_quotes()
    {
        Assert.Equal([@"C:\Program Files\x.exe", "-a", "b c", ""], DefaultShell.Split("\"C:\\Program Files\\x.exe\" -a \"b c\" \"\""));
    }
}
