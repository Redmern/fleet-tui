namespace Fleet.Shared.Evidence;

public sealed record ReportEvidence(
    string Worktree,
    string BaseRef,
    string MergeBase,
    string Head,
    bool Dirty,
    string VerifyCommand = "",
    int? VerifyExitCode = null,
    string VerifyOutput = "")
{
    public const int ShortLength = 7;

    public string ShortHead => Head.Length > ShortLength ? Head[..ShortLength] : Head;

    public Verification Verification => VerifyExitCode switch
    {
        null => Verification.Unverified,
        0 => Verification.Ok,
        _ => Verification.Failed,
    };
}
