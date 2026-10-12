namespace Fleet.Ports.Relay.Models;

public sealed record QueuedPrompt(string Text, DateTimeOffset QueuedAt);
