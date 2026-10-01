using Fleet.Shared.Aidlc;
using Fleet.Shared.Aidlc.Enums;

namespace Fleet.Tests.Shared.Aidlc;

public class ProcessTextTests
{
    private static string Render(Profile profile, AidlcPart off = AidlcPart.None, Autonomy autonomy = Autonomy.Guided, string? extra = null) =>
        ProcessText.Render(ProcessPlan.Resolve(profile, autonomy, off), extra);

    [Fact]
    public void The_stages_are_listed_in_order_with_the_profile_named()
    {
        var text = Render(Profile.Bugfix);

        Assert.Contains("**bugfix** profile", text);

        var order = new[] { "**Intake**", "**Discover**", "**Specify**", "**Build**", "**Verify**", "**Review**", "**Deliver**" }
            .Select(s => text.IndexOf(s, StringComparison.Ordinal))
            .ToList();

        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.Order(), order);
        Assert.DoesNotContain("**Plan**", text);
    }

    [Fact]
    public void Human_gates_are_marked_and_explained()
    {
        var text = Render(Profile.Express);

        var gated = text.Split('\n').Where(l => l.Contains(ProcessText.ApprovalMark, StringComparison.Ordinal)).ToList();

        Assert.Contains(gated, l => l.Contains("**Deliver**", StringComparison.Ordinal));
        Assert.DoesNotContain(gated, l => l.Contains("**Specify**", StringComparison.Ordinal));
        Assert.Contains("stop, give the user a short summary", text);
    }

    [Fact]
    public void A_gate_switched_off_is_no_longer_marked()
    {
        var text = Render(Profile.Express, AidlcPart.DeliverGate);

        Assert.DoesNotContain($"Gate: **{ProcessText.ApprovalMark}**", text);
        Assert.Contains("No stage needs the user's approval", text);
    }

    [Fact]
    public void Skipped_stages_are_listed_with_the_reason()
    {
        var text = Render(Profile.Feature, AidlcPart.Review | AidlcPart.Learn);

        Assert.Contains("Skipped: review (off in settings), learn (off in settings).", text);
        Assert.DoesNotContain("**Review**", text);
    }

    [Fact]
    public void The_artifacts_of_each_stage_are_named()
    {
        var text = Render(Profile.Feature);

        Assert.Contains("discover.md", text);
        Assert.Contains("spec.md", text);
        Assert.Contains("units.json", text);
        Assert.Contains("design.md", text);
        Assert.Contains("progress/<unit>.md", text);
        Assert.Contains("reviews/<unit>-<round>.md", text);
        Assert.Contains("learnings.md", text);
        Assert.Contains("state.json", text);
        Assert.Contains("audit.jsonl", text);
    }

    [Fact]
    public void The_walking_skeleton_and_autonomy_follow_the_settings()
    {
        Assert.Contains("walking skeleton", Render(Profile.Feature));
        Assert.Contains("check in with the user after each unit", Render(Profile.Feature));
        Assert.Contains("without asking", Render(Profile.Feature, autonomy: Autonomy.Automatic));
        Assert.DoesNotContain("walking skeleton", Render(Profile.Feature, AidlcPart.WalkingSkeleton));
    }

    [Fact]
    public void Research_ends_in_a_report_not_a_pr()
    {
        var text = Render(Profile.Research);

        Assert.Contains("REPORT.md with the findings", text);
        Assert.DoesNotContain("gh pr create", text);
    }

    [Fact]
    public void Project_guidance_is_appended_not_substituted()
    {
        var text = Render(Profile.Express, extra: "  Always run the e2e suite.  ");

        Assert.Contains("**Deliver**", text);
        Assert.EndsWith("### Project guidance\nAlways run the e2e suite.", text);
    }
}
