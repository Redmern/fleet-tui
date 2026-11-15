using Fleet.Features.Menu.ShowMenu;
using Fleet.Shared.Viewing.Models;

namespace Fleet.Cli.Composition;

public static class ViewerWiring
{
    public static ViewedFrom? Current() =>
        Environment.GetEnvironmentVariable(ViewedFrom.Variable) is { Length: > 0 } mark
            ? For(mark, Adapters.Iso().Load().On)
            : null;

    public static ViewedFrom? For(string? mark, bool iso) =>
        ViewedFrom.Parse(mark) is { } viewer ? iso ? RemoteMenus.Unnamed(viewer) : viewer : null;
}
