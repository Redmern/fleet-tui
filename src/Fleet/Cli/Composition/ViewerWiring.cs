using Fleet.Features.Menu.ShowMenu;
using Fleet.Shared.Viewing.Models;

namespace Fleet.Cli.Composition;

public static class ViewerWiring
{
    public static string? Mark() => Mark(Environment.GetEnvironmentVariable);

    public static string? Mark(Func<string, string?> environment) =>
        environment(ViewedFrom.Variable) is { Length: > 0 } mark ? mark : null;

    public static ViewedFrom? Current() => Current(Environment.GetEnvironmentVariable, () => Adapters.Iso().Load().On);

    public static ViewedFrom? Current(Func<string, string?> environment, Func<bool> iso) =>
        Mark(environment) is { } mark ? For(mark, iso()) : null;

    public static ViewedFrom? For(string? mark, bool iso) =>
        ViewedFrom.Parse(mark) is { } viewer ? iso ? RemoteMenus.Unnamed(viewer) : viewer : null;
}
