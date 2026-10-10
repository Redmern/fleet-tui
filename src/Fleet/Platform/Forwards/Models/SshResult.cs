namespace Fleet.Platform.Forwards.Models;

public sealed record SshResult(int Exit, string Output, string Errors)
{
    public bool Ok => Exit == 0;
}
