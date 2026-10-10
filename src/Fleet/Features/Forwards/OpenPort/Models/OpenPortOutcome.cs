using Fleet.Features.Forwards.OpenPort.Enums;

namespace Fleet.Features.Forwards.OpenPort.Models;

public sealed record OpenPortOutcome(
    int Port, bool Listening, OpenPortRoute Route, IReadOnlyList<string> Lines, string? Command = null);
