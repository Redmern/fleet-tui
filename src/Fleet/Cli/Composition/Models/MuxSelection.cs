using Fleet.Ports.Mux;

namespace Fleet.Cli.Composition.Models;

public sealed record MuxSelection(IMuxDriver Driver, string Name, string? Unsupported);
