namespace Fleet.Features.Forwards.OpenPort.Models;

public sealed record OpenPortOutcome(int Port, bool Listening, IReadOnlyList<string> Lines);
