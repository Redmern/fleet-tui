using Fleet.Features.Forwards.ForwardPorts.Enums;

namespace Fleet.Features.Forwards.ForwardPorts.Models;

public sealed record ForwardOrder(
    ForwardVerb Verb,
    string? Host = null,
    int Port = 0,
    int? Local = null,
    string? Project = null,
    bool Open = false);
