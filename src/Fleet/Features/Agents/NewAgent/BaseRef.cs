namespace Fleet.Features.Agents.NewAgent;

public static class BaseRef
{
    private const string OriginPrefix = "origin/";

    public static string Choose(string branch, bool hasLocal, bool hasRemote, int ahead)
    {
        if (!hasRemote)
        {
            return branch;
        }

        if (!hasLocal)
        {
            return OriginPrefix + branch;
        }

        return ahead > 0 ? branch : OriginPrefix + branch;
    }

    public static string DefaultBranch(string originHead, string anchorHead)
    {
        if (originHead.Length > 0)
        {
            return originHead.StartsWith(OriginPrefix, StringComparison.Ordinal)
                ? originHead[OriginPrefix.Length..]
                : originHead;
        }

        return anchorHead.Length > 0 ? anchorHead : "main";
    }

    public static int Ahead(string revListCounts)
    {
        var parts = revListCounts.Split(
            ['\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length > 0 && int.TryParse(parts[0], out var ahead) ? ahead : 0;
    }
}
