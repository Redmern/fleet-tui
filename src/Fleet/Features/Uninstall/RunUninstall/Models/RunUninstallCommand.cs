namespace Fleet.Features.Uninstall.RunUninstall.Models;

public sealed record RunUninstallCommand(bool Yes, bool InteractiveInput, bool RemoveDeps = false);
