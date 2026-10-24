using Terminal.Gui.Input;

namespace Fleet.Ui.Models;

public sealed record TabbedAction(Key Key, string Hint, string Icon, int Tab);
