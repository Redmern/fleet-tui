using Fleet.Ui.Models;

namespace Fleet.Ui;

public sealed record FleetPickerOptions(
    bool Filter = false,
    Func<CancellationToken, Task<PickerRefresh>>? Refresh = null,
    string Refreshing = "refreshing...");
