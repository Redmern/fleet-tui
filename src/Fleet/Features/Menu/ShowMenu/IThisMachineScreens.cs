using Fleet.Features.Menu.ShowMenu.Models;
using Fleet.Shared.Keymap.Enums;

namespace Fleet.Features.Menu.ShowMenu;

public interface IThisMachineScreens
{
    Task SwitchProjectAsync();

    Task NotificationsAsync();

    Task RemotesAsync();

    void SaveSession();

    void Keybinds();

    void Theme();

    void Toggle(FleetAction toggle);

    void Log();

    void Update();

    void Version();

    void WhatsNew();

    FleetAction ShowSettings(MenuScreen screen);
}
