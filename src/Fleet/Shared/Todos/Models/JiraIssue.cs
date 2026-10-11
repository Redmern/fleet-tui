namespace Fleet.Shared.Todos.Models;

public sealed record JiraIssue(string Key, string Summary, string Description, double? StoryPoints, bool Done);
