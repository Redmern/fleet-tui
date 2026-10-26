using Fleet.Ports.Uninstall.Enums;

namespace Fleet.Ports.Uninstall.Models;

public sealed record UninstallItem(UninstallKind Kind, string Path, string Label);
