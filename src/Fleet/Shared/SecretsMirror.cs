namespace Fleet.Shared;

public static class SecretsMirror
{
    public const string Folder = ".config";

    public static string Root(string projectRoot, string repository, string branch) =>
        Path.Combine(projectRoot, Folder, repository, BranchSlug.Of(branch));

    public static IReadOnlyList<string> Files(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase),
        ];
    }

    public static int CopyInto(string root, string worktree)
    {
        if (!Directory.Exists(root) || !Directory.Exists(worktree))
        {
            return 0;
        }

        var copied = 0;

        foreach (var relative in Files(root))
        {
            var target = Path.Combine(worktree, relative);
            var folder = Path.GetDirectoryName(target);

            if (folder is { Length: > 0 })
            {
                Directory.CreateDirectory(folder);
            }

            try
            {
                File.Copy(Path.Combine(root, relative), target, overwrite: true);
                copied++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        return copied;
    }
}
