using Fleet.Platform.Mux.Embedded.Pty;

namespace Fleet.Tests.Platform.Mux.Embedded;

public class WindowsCommandLineTests
{
    private static string Line(string program, params string[] args) =>
        WindowsCommandLine.For(program, args, p => p);

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("", "\"\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"C:\dir with space\", "\"C:\\dir with space\\\\\"")]
    [InlineData(@"a\\b", @"a\\b")]
    public void Arguments_are_quoted_the_way_the_c_runtime_splits_them(string arg, string expected)
    {
        Assert.Equal(expected, WindowsCommandLine.Quote(arg));
    }

    [Fact]
    public void A_program_and_its_arguments_are_joined_with_quoting()
    {
        Assert.Equal(
            "\"C:\\Program Files\\x\\claude.exe\" --resume \"my session\"",
            Line(@"C:\Program Files\x\claude.exe", "--resume", "my session"));
    }

    [Fact]
    public void After_cmd_slash_c_the_command_is_passed_as_written()
    {
        Assert.Equal(
            "cmd.exe /d /c set A=1&& \"C:\\x y\\fleet.exe\" attach",
            Line("cmd.exe", "/d", "/c", "set A=1&& \"C:\\x y\\fleet.exe\" attach"));
    }

    [Fact]
    public void A_batch_shim_runs_through_cmd()
    {
        var line = Line(@"C:\npm\claude.cmd", "--flag", "two words");

        Assert.EndsWith("/d /c \"C:\\npm\\claude.cmd --flag \"two words\"\"", line);
    }
}
