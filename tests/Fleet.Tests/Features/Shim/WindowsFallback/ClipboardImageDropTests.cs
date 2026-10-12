using System.Text.RegularExpressions;
using Fleet.Features.Shim.WindowsFallback;

namespace Fleet.Tests.Features.Shim.WindowsFallback;

[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
public sealed class ClipboardImageDropTests : IDisposable
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 7];

    private static readonly DateTimeOffset Now = new(2026, 10, 11, 18, 30, 5, 123, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fleet-drop-{Guid.NewGuid():N}");

    private readonly List<string> _fallbacks = [];

    public ClipboardImageDropTests() => Directory.CreateDirectory(_root);

    private string Folder => Path.Combine(_root, "fleet-clipboard");

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        foreach (var fallback in _fallbacks.Where(Directory.Exists))
        {
            Directory.Delete(fallback, recursive: true);
        }
    }

    [Fact]
    public void Save_writes_the_png_into_a_new_png_file_in_the_folder()
    {
        Directory.CreateDirectory(Folder);

        var path = ClipboardImageDrop.Save(Png, Folder, Now);

        Assert.Equal(Folder, Path.GetDirectoryName(path));
        Assert.EndsWith(".png", path);
        Assert.Equal(Png, File.ReadAllBytes(path));
    }

    [Fact]
    public void Saved_names_carry_random_bytes_so_two_saves_at_one_instant_differ()
    {
        Directory.CreateDirectory(Folder);

        var first = Path.GetFileName(ClipboardImageDrop.Save(Png, Folder, Now));
        var second = Path.GetFileName(ClipboardImageDrop.Save([1, 2, 3], Folder, Now));

        Assert.NotEqual(first, second);
        Assert.Matches(new Regex("^clipboard-20261011-183005-123-[0-9a-f]{16}\\.png$"), first);
        Assert.Matches(new Regex("^clipboard-20261011-183005-123-[0-9a-f]{16}\\.png$"), second);
    }

    [Fact]
    public void Save_removes_its_own_images_older_than_a_day_and_leaves_other_files()
    {
        Directory.CreateDirectory(Folder);
        var old = ClipboardImageDrop.Save(Png, Folder, Now - TimeSpan.FromDays(2));
        File.SetLastWriteTimeUtc(old, (Now - TimeSpan.FromDays(2)).UtcDateTime);
        var recent = ClipboardImageDrop.Save(Png, Folder, Now - TimeSpan.FromHours(1));
        File.SetLastWriteTimeUtc(recent, (Now - TimeSpan.FromHours(1)).UtcDateTime);
        var foreign = Path.Combine(Folder, "notes.txt");
        File.WriteAllText(foreign, "keep");
        File.SetLastWriteTimeUtc(foreign, (Now - TimeSpan.FromDays(9)).UtcDateTime);

        ClipboardImageDrop.Save(Png, Folder, Now);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(foreign));
    }

    [UnixFact]
    public void A_missing_folder_is_created_for_the_owner_only()
    {
        var folder = ClipboardImageDrop.SafeFolder(Folder, _ => { });

        Assert.Equal(Folder, folder);
        Assert.Equal(OwnerOnly, File.GetUnixFileMode(Folder));
    }

    [UnixFact]
    public void A_private_folder_this_user_already_made_is_used_again()
    {
        Directory.CreateDirectory(Folder, OwnerOnly);

        Assert.Equal(Folder, ClipboardImageDrop.SafeFolder(Folder, _ => { }));
    }

    [UnixFact]
    public void A_folder_others_can_read_is_not_used()
    {
        Directory.CreateDirectory(Folder, OwnerOnly | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        var logged = new List<string>();

        var folder = Fallback(ClipboardImageDrop.SafeFolder(Folder, logged.Add));

        Assert.NotEqual(Folder, folder);
        Assert.Equal(OwnerOnly, File.GetUnixFileMode(folder));
        Assert.Contains(logged, line => line.Contains(Folder, StringComparison.Ordinal));
    }

    [UnixFact]
    public void A_symlink_planted_in_its_place_is_not_followed()
    {
        var elsewhere = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(elsewhere, OwnerOnly);
        Directory.CreateSymbolicLink(Folder, elsewhere);

        var folder = Fallback(ClipboardImageDrop.SafeFolder(Folder, _ => { }));

        Assert.NotEqual(Folder, folder);
        Assert.NotEqual(elsewhere, folder);
    }

    [UnixFact]
    public void A_file_planted_in_its_place_is_not_used()
    {
        File.WriteAllText(Folder, "not a folder");

        var folder = Fallback(ClipboardImageDrop.SafeFolder(Folder, _ => { }));

        Assert.NotEqual(Folder, folder);
        Assert.True(Directory.Exists(folder));
    }

    [UnixFact]
    public void The_fallback_folder_is_kept_for_later_drops()
    {
        Directory.CreateDirectory(Folder, OwnerOnly | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        var first = Fallback(ClipboardImageDrop.SafeFolder(Folder, _ => { }));

        Assert.Equal(first, ClipboardImageDrop.SafeFolder(Folder, _ => { }));
    }

    [Theory]
    [InlineData("/tmp/fleet-clipboard/a.png", "/tmp/fleet-clipboard/a.png")]
    [InlineData("/tmp/fleet clipboard/a.png", "'/tmp/fleet clipboard/a.png'")]
    [InlineData("/tmp/$HOME/a.png", "'/tmp/$HOME/a.png'")]
    [InlineData("/tmp/a`id`.png", "'/tmp/a`id`.png'")]
    [InlineData("/tmp/a&b.png", "'/tmp/a&b.png'")]
    [InlineData("/tmp/a;rm.png", "'/tmp/a;rm.png'")]
    [InlineData("/tmp/it's.png", "'/tmp/it'\\''s.png'")]
    [InlineData("/tmp/日本/a.png", "/tmp/日本/a.png")]
    [InlineData("/tmp/日本 語/a.png", "'/tmp/日本 語/a.png'")]
    public void Posix_hosts_get_single_quoted_paths_when_the_shell_would_read_them(string path, string typed) =>
        Assert.Equal(typed, ClipboardImageDrop.Typed(path, windowsHost: false));

    [Theory]
    [InlineData(@"C:\Temp\a.png", @"'C:\Temp\a.png'")]
    [InlineData(@"C:\Users\Jane Doe\a.png", @"'C:\Users\Jane Doe\a.png'")]
    [InlineData(@"C:\Temp\$env.png", @"'C:\Temp\$env.png'")]
    [InlineData(@"C:\Temp\a`b.png", @"'C:\Temp\a`b.png'")]
    [InlineData(@"C:\Temp\a&b.png", @"'C:\Temp\a&b.png'")]
    [InlineData(@"C:\Temp\a;b.png", @"'C:\Temp\a;b.png'")]
    [InlineData(@"C:\Temp\it's.png", @"'C:\Temp\it''s.png'")]
    [InlineData("C:\\Temp\\it\u2019s.png", "'C:\\Temp\\it\u2019\u2019s.png'")]
    [InlineData(@"C:\Users\日本\a.png", @"'C:\Users\日本\a.png'")]
    public void Windows_hosts_get_powershell_single_quoted_paths(string path, string typed) =>
        Assert.Equal(typed, ClipboardImageDrop.Typed(path, windowsHost: true));

    [Fact]
    public void Drop_saves_and_answers_the_text_to_type()
    {
        var windowsHost = OperatingSystem.IsWindows();

        var typed = ClipboardImageDrop.Drop(Png, Folder, Now, windowsHost, _ => { });

        var saved = Assert.Single(Directory.GetFiles(Folder, "*.png"));
        Assert.Equal(Png, File.ReadAllBytes(saved));
        Assert.Equal(ClipboardImageDrop.Typed(saved, windowsHost), typed);
    }

    [UnixFact]
    public void Drop_where_a_file_blocks_the_folder_still_saves_in_a_private_fallback()
    {
        File.WriteAllText(Folder, "not a folder");

        var typed = ClipboardImageDrop.Drop(Png, Folder, Now, windowsHost: false, _ => { });

        Assert.NotNull(typed);
        Fallback(Path.GetDirectoryName(typed)!);
        Assert.Equal(Png, File.ReadAllBytes(typed));
        Assert.Equal(OwnerOnly, File.GetUnixFileMode(Path.GetDirectoryName(typed)!));
    }

    [Fact]
    public void The_default_folder_is_under_the_temp_folder() =>
        Assert.StartsWith(Path.GetTempPath(), ClipboardImageDrop.Folder);

    private string Fallback(string folder)
    {
        _fallbacks.Add(folder);
        return folder;
    }

    private sealed class UnixFactAttribute : FactAttribute
    {
        public UnixFactAttribute()
        {
            if (OperatingSystem.IsWindows())
            {
                Skip = "folder modes and symlinks are unix rules; %TEMP% is already per user on Windows";
            }
        }
    }
}
