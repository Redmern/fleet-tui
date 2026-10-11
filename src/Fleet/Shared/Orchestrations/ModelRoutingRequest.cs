namespace Fleet.Shared.Orchestrations;

public sealed record ModelRoutingRequest(string Task, string? Profile, bool Research, string? Repository);
