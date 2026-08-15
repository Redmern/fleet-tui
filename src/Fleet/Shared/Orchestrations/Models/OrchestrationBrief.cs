namespace Fleet.Shared.Orchestrations.Models;

public sealed record OrchestrationBrief(string Project, string Slug, string Prompt, string StampUtc);
