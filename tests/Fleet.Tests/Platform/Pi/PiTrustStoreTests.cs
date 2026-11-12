using Fleet.Platform.Pi;

namespace Fleet.Tests.Platform.Pi;

public class PiTrustStoreTests
{
    [Fact]
    public void A_store_that_starts_with_a_byte_order_mark_is_read()
    {
        var written = PiTrustStore.WithTrusted("\uFEFF{ \"/other\": true }\n", "/wt");

        Assert.True(written.Succeeded, written.Error);
        Assert.Equal("{\n  \"/other\": true,\n  \"/wt\": true\n}\n", written.Value);
    }

    [Fact]
    public void The_source_of_the_trust_store_has_no_invisible_character()
    {
        var source = Path.Combine(
            RepositoryRoot(), "src", "Fleet", "Platform", "Pi", "PiTrustStore.cs");

        Assert.DoesNotContain('\uFEFF', File.ReadAllText(source, new System.Text.UTF8Encoding(false)).TrimStart('\uFEFF'));
    }

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Fleet.slnx")))
        {
            folder = folder.Parent;
        }

        return folder!.FullName;
    }
}
