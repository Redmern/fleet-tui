namespace Fleet.Platform.Forwards.Models;

public sealed record ListeningPort(int Port, string Address, string? Process = null);
