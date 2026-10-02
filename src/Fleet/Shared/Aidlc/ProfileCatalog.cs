using Fleet.Shared.Aidlc.Enums;

namespace Fleet.Shared.Aidlc;

public static class ProfileCatalog
{
    public const Profile Default = Profile.Express;

    public static IReadOnlyList<Profile> All { get; } = Enum.GetValues<Profile>();

    public static IReadOnlyList<Stage> StagesOf(Profile profile) => profile switch
    {
        Profile.Express =>
            [Stage.Intake, Stage.Specify, Stage.Build, Stage.Verify, Stage.Review, Stage.Deliver],
        Profile.Bugfix =>
            [Stage.Intake, Stage.Discover, Stage.Specify, Stage.Build, Stage.Verify, Stage.Review, Stage.Deliver],
        Profile.Refactor =>
            [Stage.Intake, Stage.Discover, Stage.Plan, Stage.Build, Stage.Verify, Stage.Review, Stage.Deliver],
        Profile.Research =>
            [Stage.Intake, Stage.Discover, Stage.Deliver],
        _ => Enum.GetValues<Stage>(),
    };

    public static IReadOnlyList<Stage> GatesOf(Profile profile) => profile switch
    {
        Profile.Express => [Stage.Deliver],
        Profile.Bugfix => [Stage.Specify, Stage.Deliver],
        Profile.Refactor => [Stage.Plan, Stage.Deliver],
        Profile.Research => [Stage.Deliver],
        _ => [Stage.Specify, Stage.Plan, Stage.Build, Stage.Deliver],
    };

    public static bool IsGated(Profile profile, Stage stage) => GatesOf(profile).Contains(stage);

    public static string Describe(Profile profile) => profile switch
    {
        Profile.Express => "a small, well-understood change in one repository",
        Profile.Bugfix => "a defect with a reproduction",
        Profile.Refactor => "a change that keeps behaviour the same",
        Profile.Research => "investigate and recommend, no code",
        _ => "new behaviour, possibly across repositories",
    };
}
