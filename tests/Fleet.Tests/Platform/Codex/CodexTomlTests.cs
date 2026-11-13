using Fleet.Platform.Codex;
using Fleet.Ports.Claude.Models;

namespace Fleet.Tests.Platform.Codex;

public class CodexTomlTests
{
    private const string Launch =
        "command = \"/bin/fleet\"\nargs = [\"mcp\", \"--caller\", \"sub\"]\n";

    private const string Section =
        "[mcp_servers.fleet]\n" + Launch + "default_tools_approval_mode = \"approve\"\n";

    private static readonly McpServerEntry Fleet = new("fleet", "/bin/fleet", ["mcp", "--caller", "sub"]);

    private static string WithServer(string text)
    {
        var written = CodexToml.WithServer(text, Fleet);

        Assert.True(written.Succeeded, written.Error);

        return written.Value!;
    }

    private static string Trusting(string text, string folder)
    {
        var written = CodexToml.WithTrustedProject(text, folder);

        Assert.True(written.Succeeded, written.Error);

        return written.Value!;
    }

    [Fact]
    public void An_empty_file_gets_the_server_section()
    {
        Assert.Equal(Section, WithServer(string.Empty));
    }

    [Fact]
    public void The_keys_the_user_added_to_the_fleet_server_are_kept()
    {
        var text =
            "[mcp_servers.fleet]\n"
            + "command = \"old\" # stale\n"
            + "startup_timeout_sec = 30\n"
            + "args = [\"x\"]\n"
            + "env = { TOKEN = \"1\" }\n";

        Assert.Equal(
            Section + "startup_timeout_sec = 30\nenv = { TOKEN = \"1\" }\n",
            WithServer(text));
    }

    [Fact]
    public void A_sub_table_of_the_fleet_server_is_kept()
    {
        var text = "[mcp_servers.fleet]\ncommand = \"old\"\n\n[mcp_servers.fleet.env]\nX = \"1\"\n";

        Assert.Equal(Section + "\n[mcp_servers.fleet.env]\nX = \"1\"\n", WithServer(text));
    }

    [Fact]
    public void A_sub_table_without_the_server_table_gets_the_section_after_it()
    {
        var text = "[mcp_servers.fleet.env]\nX = \"1\"\n";

        Assert.Equal(text + "\n" + Section, WithServer(text));
    }

    [Fact]
    public void A_comment_above_the_next_table_is_kept()
    {
        var text =
            "# top\n[mcp_servers.fleet]\ncommand = \"old\"\nargs = []\n\n"
            + "# my notes on playwright\n[mcp_servers.playwright]\ncommand = \"npx\"\n";

        Assert.Equal(
            "# top\n" + Section + "\n# my notes on playwright\n[mcp_servers.playwright]\ncommand = \"npx\"\n",
            WithServer(text));
    }

    [Fact]
    public void A_comment_inside_the_fleet_server_is_kept()
    {
        var text = "[mcp_servers.fleet]\n# why this server\ncommand = \"old\"\n";

        Assert.Equal(Section + "# why this server\n", WithServer(text));
    }

    [Fact]
    public void An_approval_mode_the_user_chose_is_kept()
    {
        var text = "[mcp_servers.fleet]\ndefault_tools_approval_mode = \"prompt\"\n";

        Assert.Equal(
            "[mcp_servers.fleet]\n" + Launch + "default_tools_approval_mode = \"prompt\"\n",
            WithServer(text));
    }

    [Fact]
    public void Arguments_written_over_several_lines_are_replaced_as_a_whole()
    {
        var text = "[mcp_servers.fleet]\nargs = [\n  \"a\", # one\n  \"b\",\n]\ncommand = \"old\"\nnote = 1\n";

        Assert.Equal(Section + "note = 1\n", WithServer(text));
    }

    [Fact]
    public void A_quoted_server_name_in_the_header_is_the_same_server()
    {
        var text = "[ mcp_servers . \"fleet\" ] # mine\ncommand = \"old\"\n";

        Assert.Equal(
            "[ mcp_servers . \"fleet\" ] # mine\n" + Launch + "default_tools_approval_mode = \"approve\"\n",
            WithServer(text));
    }

    [Fact]
    public void A_file_with_windows_newlines_keeps_them_and_gets_them_on_new_lines()
    {
        var text = "model = \"o3\"\r\n\r\n[mcp_servers.fleet]\r\ncommand = \"old\"\r\nnote = 1\r\n";

        Assert.Equal(
            "model = \"o3\"\r\n\r\n" + Section.Replace("\n", "\r\n") + "note = 1\r\n",
            WithServer(text));
    }

    [Fact]
    public void A_file_with_mixed_newlines_keeps_each_line_as_it_was()
    {
        var text = "a = 1\r\nb = 2\n\n[mcp_servers.other]\ncommand = \"o\"\r\n";

        Assert.Equal(text + "\r\n" + Section.Replace("\n", "\r\n"), WithServer(text));
    }

    [Fact]
    public void Blank_lines_at_the_end_are_kept()
    {
        var text = "[mcp_servers.fleet]\ncommand = \"old\"\n\n\n";

        Assert.Equal(Section + "\n\n", WithServer(text));
    }

    [Fact]
    public void A_last_line_without_a_newline_is_ended_before_the_section()
    {
        Assert.Equal("model = \"o3\"\n\n" + Section, WithServer("model = \"o3\""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("model = \"o3\"\n\n[mcp_servers.other]\ncommand = \"o\"\n\n")]
    [InlineData("[mcp_servers.fleet]\n# c\ncommand = \"old\"\nenv = { X = \"1\" }\r\n\r\n[tui]\r\nx = 1")]
    public void A_second_run_changes_nothing(string text)
    {
        var once = WithServer(text);

        Assert.Equal(once, WithServer(once));
    }

    [Theory]
    [InlineData("{ not toml")]
    [InlineData("[mcp_servers.fleet\ncommand = \"x\"\n")]
    [InlineData("s = \"unterminated\nx = 1\n")]
    [InlineData("a = [1, 2\n")]
    [InlineData("mcp_servers = { fleet = { command = \"x\" } }\n")]
    [InlineData("[mcp_servers]\nfleet = { command = \"x\" }\n")]
    [InlineData("[mcp_servers]\nfleet.command = \"x\"\n")]
    [InlineData("mcp_servers.fleet.command = \"x\"\n")]
    [InlineData("path = \"C:\\Users\\me\"\n")]
    public void A_file_fleet_cannot_edit_safely_is_refused(string text)
    {
        Assert.False(CodexToml.WithServer(text, Fleet).Succeeded);
    }

    [Fact]
    public void A_new_trust_entry_goes_after_the_projects_already_there()
    {
        var text = "[projects.\"/other\"]\ntrust_level = \"trusted\"\n\n[projects.'/third']\ntrust_level = \"untrusted\"\n";

        Assert.Equal(
            text + "\n[projects.\"/wt\"]\ntrust_level = \"trusted\"\n",
            Trusting(text, "/wt"));
    }

    [Theory]
    [InlineData("[projects]\n\"/wt\".trust_level = \"trusted\"\n")]
    [InlineData("projects.\"/wt\".trust_level = 'trusted'\n")]
    [InlineData("[projects.\"/wt\"]\r\n# c\r\ntrust_level = \"trusted\"\r\n")]
    public void A_folder_trusted_in_any_table_form_is_left_byte_for_byte(string text)
    {
        Assert.Equal(text, Trusting(text, "/wt"));
    }

    [Theory]
    [InlineData("projects = { \"/wt\" = { trust_level = \"trusted\" } }\n")]
    [InlineData("[projects]\n\"/wt\" = { note = 1 }\n")]
    [InlineData("[projects.\"/wt\"]\ntrust_level = \"untrusted\"\n")]
    [InlineData("[projects\n")]
    [InlineData("[projects.\"C:\\Users\\me\\wt\"]\ntrust_level = \"trusted\"\n")]
    [InlineData("[projects.\"/a\\qb\"]\n")]
    [InlineData("[projects.\"/a\\u00zz\"]\n")]
    [InlineData("[projects.\"/a\\u 41 \"]\n")]
    [InlineData("[projects.\"/a\\x4 \"]\n")]
    [InlineData("[projects.\"/a\\uD800\"]\n")]
    public void A_trust_entry_fleet_cannot_add_safely_is_refused(string text)
    {
        Assert.False(CodexToml.WithTrustedProject(text, "/wt").Succeeded);
    }

    [Theory]
    [InlineData("\"C:\\\\Users\\\\me\\\\wt\"", "C:\\Users\\me\\wt")]
    [InlineData("'C:\\Users\\me\\wt'", "C:\\Users\\me\\wt")]
    [InlineData("\"/home/\\u0041\\U00000042/w\\\"t\"", "/home/AB/w\"t")]
    [InlineData("\"/a\\tb\\bc\\fd\"", "/a\tb\bc\fd")]
    [InlineData("\"/a\\eb\\x41\"", "/a\u001BbA")]
    public void A_quoted_path_key_is_read_with_its_escapes(string key, string folder)
    {
        var text = $"[projects.{key}]\ntrust_level = \"trusted\"\n";

        Assert.Equal(text, Trusting(text, folder));
    }

    [Fact]
    public void A_path_with_backslashes_is_written_escaped_and_found_again()
    {
        var once = Trusting(string.Empty, "C:\\Users\\me\\w\"t");

        Assert.Equal("[projects.\"C:\\\\Users\\\\me\\\\w\\\"t\"]\ntrust_level = \"trusted\"\n", once);
        Assert.Equal(once, Trusting(once, "C:\\Users\\me\\w\"t"));
    }

    [Fact]
    public void A_trust_entry_added_to_a_windows_file_uses_its_newlines_and_keeps_the_end()
    {
        var text = "[features]\r\nhooks = true\r\n\r\n";

        Assert.Equal(
            text + "[projects.\"/wt\"]\r\ntrust_level = \"trusted\"\r\n",
            Trusting(text, "/wt"));
    }

    [Fact]
    public void A_project_table_without_a_trust_level_gets_one_and_keeps_its_keys()
    {
        var text = "[projects.\"/wt\"]\r\nnote = 1";

        Assert.Equal("[projects.\"/wt\"]\r\ntrust_level = \"trusted\"\r\nnote = 1", Trusting(text, "/wt"));
    }
}
