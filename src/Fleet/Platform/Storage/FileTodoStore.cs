using System.Text;
using Fleet.Ports.Todos;
using Fleet.Shared.Results;
using Fleet.Shared.Todos;
using Fleet.Shared.Todos.Models;

namespace Fleet.Platform.Storage;

public sealed class FileTodoStore : ITodoStore
{
    private const int Attempts = 100;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public IReadOnlyList<Ticket> List(string projectRoot)
    {
        var folder = TodoPaths.Folder(projectRoot);

        if (!Directory.Exists(folder))
        {
            return [];
        }

        return [.. Stems(folder).Select(stem => Get(projectRoot, stem)).OfType<Ticket>()];
    }

    public Ticket? Get(string projectRoot, string id)
    {
        var file = TodoPaths.File(projectRoot, id);

        try
        {
            return File.Exists(file)
                ? TicketFile.Parse(id, File.ReadAllText(file), File.GetLastWriteTimeUtc(file))
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public Result<Ticket> Add(string projectRoot, Ticket draft)
    {
        var folder = TodoPaths.Folder(projectRoot);

        try
        {
            Directory.CreateDirectory(folder);

            var counter = TicketName.NextCounter(Stems(folder));

            for (var attempt = 0; attempt < Attempts; attempt++, counter++)
            {
                var ticket = draft with { Id = TicketName.For(counter, draft.Title) };

                if (TryCreate(TodoPaths.File(projectRoot, ticket.Id), TicketFile.Format(ticket)))
                {
                    return Result<Ticket>.Ok(ticket);
                }
            }

            return Result<Ticket>.Fail($"fleet found no free ticket name in {folder}.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Result<Ticket>.Fail($"fleet could not write a ticket in {folder}: {e.Message}");
        }
    }

    public Result Save(string projectRoot, Ticket ticket)
    {
        var file = TodoPaths.File(projectRoot, ticket.Id);

        return BusyFiles.Replace(file, temp => File.WriteAllText(temp, TicketFile.Format(ticket), Utf8))
            ? Result.Ok()
            : Result.Fail($"fleet could not write {file}.");
    }

    public Ticket? FindByOrchestration(string projectRoot, string orchestration) =>
        orchestration.Length == 0
            ? null
            : List(projectRoot).FirstOrDefault(
                t => string.Equals(t.Orchestration, orchestration, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> Stems(string folder) =>
        Directory.EnumerateFiles(folder, "*" + TodoPaths.Extension).Select(Path.GetFileNameWithoutExtension).OfType<string>();

    private static bool TryCreate(string file, string text)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream, Utf8);
            writer.Write(text);
            return true;
        }
        catch (IOException) when (File.Exists(file))
        {
            return false;
        }
    }
}
