namespace Fleet.Ports.Uninstall.Models;

public sealed record DependencyRemoval(bool Succeeded, string Message);
