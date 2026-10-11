using Fleet.Platform.Jira;

namespace Fleet.Tests.Platform.Jira;

[Collection(ConfigHomeCollection.Name)]
public sealed class FileJiraTokenStoreTests : ConfigHomeFixture
{
    private const string Token = "s3cret-token";

    private string TokenFile => Path.Combine(ConfigHome, "secrets", "jira-techweb");

    private void Write(string text, UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TokenFile)!);
        File.WriteAllText(TokenFile, text);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(TokenFile, mode);
        }
    }

    [Fact]
    public void The_token_is_read_from_the_project_secret_file_and_trimmed()
    {
        Write("  " + Token + "\n");

        var token = new FileJiraTokenStore().Read("techweb");

        Assert.True(token.Succeeded, token.Error);
        Assert.Equal(Token, token.Value);
    }

    [Fact]
    public void A_missing_token_file_says_where_to_put_it()
    {
        var token = new FileJiraTokenStore().Read("techweb");

        Assert.False(token.Succeeded);
        Assert.Contains(TokenFile, token.Error);
    }

    [Fact]
    public void An_empty_token_file_is_refused()
    {
        Write("\n");

        Assert.False(new FileJiraTokenStore().Read("techweb").Succeeded);
    }

    [Fact]
    public void A_token_file_others_can_read_is_refused_without_showing_the_token()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Write(Token, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        var token = new FileJiraTokenStore().Read("techweb");

        Assert.False(token.Succeeded);
        Assert.Contains("chmod 600", token.Error);
        Assert.DoesNotContain(Token, token.Error);
    }

    [Fact]
    public void A_project_name_cannot_reach_outside_the_secrets_folder()
    {
        var token = new FileJiraTokenStore().Read("../../etc");

        Assert.False(token.Succeeded);
        Assert.DoesNotContain("..", token.Error);
    }
}
