using Fleet.Cli.Enums;

namespace Fleet.Cli.Models;

public sealed record Invocation(
    FleetVerb Verb,
    string Raw,
    string? Project,
    string? Action,
    string? Text = null,
    string? Caller = null,
    string? Status = null,
    string? Title = null,
    IReadOnlyList<string>? Tail = null);
