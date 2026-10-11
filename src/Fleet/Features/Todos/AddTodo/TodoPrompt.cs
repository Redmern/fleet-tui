using Fleet.Features.Todos.AddTodo.Models;

namespace Fleet.Features.Todos.AddTodo;

public static class TodoPrompt
{
    public static TodoTarget Split(string prompt, Func<string, bool> isProject)
    {
        var colon = prompt.IndexOf(':', StringComparison.Ordinal);

        if (colon <= 0)
        {
            return new TodoTarget(null, prompt);
        }

        var name = prompt[..colon].Trim();
        var text = prompt[(colon + 1)..].Trim();

        return name.Length == 0 || name.Any(char.IsWhiteSpace) || text.Length == 0 || !isProject(name)
            ? new TodoTarget(null, prompt)
            : new TodoTarget(name, text);
    }
}
