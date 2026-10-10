namespace Fleet.Features.Iso.ToggleIso.Models;

public sealed record IsoCaller(bool InteractiveInput, bool OverSsh, bool InAgent);
