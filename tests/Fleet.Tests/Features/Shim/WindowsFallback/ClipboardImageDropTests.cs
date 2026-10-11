using Fleet.Features.Shim.WindowsFallback;

namespace Fleet.Tests.Features.Shim.WindowsFallback;

public sealed class ClipboardImageDropTests : IDisposable
{
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 7];

    private static readonly DateTimeOffset Now = new(2026, 10, 11, 18, 30, 5, 123, TimeSpan.Zero);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"fleet-drop-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Save_writes_the_png_into_a_new_png_file_in_the_folder()
    {
        var path = ClipboardImageDrop.Save(Png, _folder, Now);

        Assert.Equal(_folder, Path.GetDirectoryName(path));
        Assert.EndsWith(".png", path);
        Assert.Equal(Png, File.ReadAllBytes(path));
    }

    [Fact]
    public void Two_saves_at_the_same_instant_keep_both_images()
    {
        var first = ClipboardImageDrop.Save(Png, _folder, Now);
        var second = ClipboardImageDrop.Save([1, 2, 3], _folder, Now);

        Assert.NotEqual(first, second);
        Assert.Equal(Png, File.ReadAllBytes(first));
    }

    [Fact]
    public void Save_removes_its_own_images_older_than_a_day_and_leaves_other_files()
    {
        var old = ClipboardImageDrop.Save(Png, _folder, Now - TimeSpan.FromDays(2));
        File.SetLastWriteTimeUtc(old, (Now - TimeSpan.FromDays(2)).UtcDateTime);
        var recent = ClipboardImageDrop.Save(Png, _folder, Now - TimeSpan.FromHours(1));
        File.SetLastWriteTimeUtc(recent, (Now - TimeSpan.FromHours(1)).UtcDateTime);
        var foreign = Path.Combine(_folder, "notes.txt");
        File.WriteAllText(foreign, "keep");
        File.SetLastWriteTimeUtc(foreign, (Now - TimeSpan.FromDays(9)).UtcDateTime);

        ClipboardImageDrop.Save(Png, _folder, Now);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(foreign));
    }

    [Theory]
    [InlineData(@"C:\Users\red\AppData\Local\Temp\fleet-clipboard\a.png", @"C:\Users\red\AppData\Local\Temp\fleet-clipboard\a.png")]
    [InlineData(@"C:\Users\Jane Doe\Temp\a.png", "\"C:\\Users\\Jane Doe\\Temp\\a.png\"")]
    [InlineData("/tmp/fleet-clipboard/a.png", "/tmp/fleet-clipboard/a.png")]
    public void Typed_quotes_only_paths_with_spaces(string path, string typed) =>
        Assert.Equal(typed, ClipboardImageDrop.Typed(path));

    [Fact]
    public void Drop_saves_and_answers_the_text_to_type()
    {
        var typed = ClipboardImageDrop.Drop(Png, _folder, Now, _ => { });

        Assert.NotNull(typed);
        Assert.Equal(Png, File.ReadAllBytes(typed));
    }

    [Fact]
    public void Drop_into_a_folder_it_cannot_create_logs_and_answers_nothing()
    {
        File.WriteAllText(_folder, "a file, not a folder");
        var logged = new List<string>();

        try
        {
            Assert.Null(ClipboardImageDrop.Drop(Png, _folder, Now, logged.Add));
            Assert.Contains(logged, line => line.Contains(_folder, StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(_folder);
        }
    }

    [Fact]
    public void The_default_folder_is_under_the_temp_folder() =>
        Assert.StartsWith(Path.GetTempPath(), ClipboardImageDrop.Folder);
}
