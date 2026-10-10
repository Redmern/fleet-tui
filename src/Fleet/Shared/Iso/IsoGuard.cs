using Fleet.Shared.Iso.Models;
using Fleet.Shared.Results;

namespace Fleet.Shared.Iso;

public static class IsoGuard
{
    public const string Ssh = "open ssh connections to other machines";

    public const string UpdateCheck = "check for updates";

    public const string Push = "push";

    public const string Sync = "sync to other machines";

    public const string Forward = "forward ports from other machines";

    public static string Refusal(string what) => $"ISO mode is on: fleet does not {what} from this machine.";

    public static Result Outbound(IsoConfig config, string what) => config.On ? Result.Fail(Refusal(what)) : Result.Ok();
}
