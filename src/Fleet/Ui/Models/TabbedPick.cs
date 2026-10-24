namespace Fleet.Ui.Models;

public sealed record TabbedPick(int Tab, int Index, bool NewWindow, TabbedAction? Action = null);
