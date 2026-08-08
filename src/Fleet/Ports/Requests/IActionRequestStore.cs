using Fleet.Shared.Keymap.Enums;

namespace Fleet.Ports.Requests;

public interface IActionRequestStore
{
    void Submit(string project, FleetAction action);

    FleetAction TakePending(string project);
}
