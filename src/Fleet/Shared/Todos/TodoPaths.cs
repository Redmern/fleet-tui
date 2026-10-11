namespace Fleet.Shared.Todos;

public static class TodoPaths
{
    public const string Extension = ".md";

    public static string Folder(string projectRoot) => Path.Combine(projectRoot, ".fleet", "todos");

    public static string File(string projectRoot, string id) => Path.Combine(Folder(projectRoot), id + Extension);
}
