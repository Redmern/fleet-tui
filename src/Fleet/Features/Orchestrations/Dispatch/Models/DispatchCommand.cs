namespace Fleet.Features.Orchestrations.Dispatch.Models;

public sealed record DispatchCommand(
    string ProjectName,
    string ProjectRoot,
    string Prompt,
    string Caller = "",
    string? Profile = null,
    string? Repository = null,
    string? Branch = null,
    bool Research = false);
