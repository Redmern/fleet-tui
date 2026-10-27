namespace Fleet.Shared.Orchestrations;

public static class PromptLine
{
    public static string OneLine(string prompt) =>
        string.Join(
            ' ',
            prompt.Split(['\r', '\n']).Select(l => l.Trim()).Where(l => l.Length > 0));
}
