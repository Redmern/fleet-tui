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
        Assert.Contains("status \"working\" and a summary such as \"waiting on user\"", text);
    }

    [Fact]
    public void A_gate_switched_off_is_no_longer_marked()
    {
        var text = Render(Profile.Express, AidlcPart.DeliverGate);

        Assert.DoesNotContain($"Gate: **{ProcessText.ApprovalMark}**", text);
        Assert.Contains("No stage needs the user's approval", text);
        Assert.DoesNotContain("waiting on user", text);
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

    [Theory]
    [InlineData(Profile.Feature, "Skill: fl-discuss. Interview the user")]
    [InlineData(Profile.Express, "Skill: fl-discuss, only if the task is vague.")]
    [InlineData(Profile.Bugfix, "Skill: fl-discuss, only if the task is vague.")]
    public void The_specify_stage_names_fl_discuss_for_its_profile(Profile profile, string line)
    {
        var text = Render(profile);

        Assert.Contains($"\n   {line}", text);
        Assert.True(text.IndexOf(line, StringComparison.Ordinal) > text.IndexOf("**Specify**", StringComparison.Ordinal));
    }

    [Fact]
    public void A_refactor_has_no_specify_stage_so_its_plan_stage_names_fl_discuss()
    {
        var text = Render(Profile.Refactor);

        Assert.Contains("\n   Skill: fl-discuss. Interview the user with it before you write design.md.", text);
        Assert.Null(ProcessText.Skill(Stage.Plan, Profile.Feature));
    }

    [Fact]
    public void Build_review_deliver_and_learn_name_their_skill()
    {
        var text = Render(Profile.Feature);

        Assert.Contains("\n   Skill: fl-tdd. Tell each agent to use it.", text);
        Assert.Contains("\n   Skill: fl-review.", text);
        Assert.Contains("\n   Skill: fl-pr. Tell each agent to write its PR with it.", text);
        Assert.Contains("\n   Skill: fl-pr's retro.", text);
    }

    [Fact]
    public void A_research_delivery_names_no_pr_skill()
    {
        Assert.Null(ProcessText.Skill(Stage.Deliver, Profile.Research));
    }

    [Fact]
    public void Stages_without_a_skill_get_no_extra_line()
    {
        Assert.Null(ProcessText.Skill(Stage.Intake, Profile.Feature));
        Assert.Null(ProcessText.Skill(Stage.Discover, Profile.Feature));
        Assert.Null(ProcessText.Skill(Stage.Plan, Profile.Feature));
        Assert.Null(ProcessText.Skill(Stage.Verify, Profile.Feature));
    }

    private static string StageLine(string text, string stage)
    {
        var start = text.IndexOf($"**{stage}**", StringComparison.Ordinal);
        Assert.True(start >= 0, $"no {stage} stage in the text");

        var next = text.IndexOf("\n", start, StringComparison.Ordinal);

        return text[start..(next < 0 ? text.Length : next)];
    }

    [Theory]
    [InlineData(Profile.Feature, "Discover")]
    [InlineData(Profile.Feature, "Plan")]
    [InlineData(Profile.Feature, "Build")]
    [InlineData(Profile.Feature, "Review")]
    [InlineData(Profile.Bugfix, "Discover")]
    [InlineData(Profile.Bugfix, "Build")]
    [InlineData(Profile.Bugfix, "Review")]
    [InlineData(Profile.Express, "Build")]
    public void A_stage_that_reads_or_diffs_code_says_to_fetch_and_use_origin_never_local_default(
        Profile profile, string stage)
    {
        var line = StageLine(Render(profile), stage);

        Assert.Contains("`git fetch`", line);
        Assert.Contains("`origin/<default>`", line);
        Assert.Contains("the unit's recorded base", line);
        Assert.Contains("never local `<default>`", line);
    }

    [Theory]
    [InlineData(Profile.Feature)]
    [InlineData(Profile.Refactor)]
    public void Build_with_units_says_to_pass_the_parent_units_branch_as_base_for_a_unit_that_depends_on_an_unmerged_one(
        Profile profile)
    {
        var line = StageLine(Render(profile), "Build");

        Assert.Contains("`base: <parent unit's branch>`", line);
        Assert.Contains("depends on another unit that is not merged yet", line);
        Assert.Contains("new_agent", line);
    }

    [Fact]
    public void Build_without_units_says_nothing_about_a_base()
    {
        Assert.DoesNotContain("base:", StageLine(Render(Profile.Express), "Build"));
    }

    [Fact]
    public void Stages_that_do_not_read_or_diff_code_carry_no_fetch_text()
    {
        var text = Render(Profile.Feature);

        Assert.DoesNotContain("git fetch", StageLine(text, "Specify"));
        Assert.DoesNotContain("git fetch", StageLine(text, "Deliver"));
        Assert.DoesNotContain("git fetch", StageLine(text, "Learn"));
    }

    [Theory]
    [InlineData(Profile.Feature)]
    [InlineData(Profile.Refactor)]
    public void Plan_asks_before_its_gate_about_the_release_the_version_and_the_release_notes_author_and_records_it(
        Profile profile)
    {
        var line = StageLine(Render(profile), "Plan");

        Assert.Contains("before the plan gate", line);
        Assert.Contains("ships in a release", line);
        Assert.Contains("which version", line);
        Assert.Contains("who writes the `RELEASE_NOTES.md` entry", line);
        Assert.Contains("record the answer in design.md", line);
    }

    [Theory]
    [InlineData(Profile.Feature)]
    [InlineData(Profile.Refactor)]
    [InlineData(Profile.Bugfix)]
    [InlineData(Profile.Express)]
    public void Review_requires_a_review_of_the_combined_stack_in_dependency_order_before_deliver(Profile profile)
    {
        var line = StageLine(Render(profile), "Review");

        Assert.Contains("review each unit's actual diff", line);
        Assert.Contains("combined stack", line);
        Assert.Contains("all units merged in dependency order", line);
        Assert.Contains("before Deliver", line);
        Assert.Contains("in addition to the per-unit reviews", line);
    }

    [Theory]
    [InlineData(Profile.Feature)]
    [InlineData(Profile.Express)]
    [InlineData(Profile.Research)]
    public void The_text_names_every_profile_and_says_to_propose_a_better_one_at_first_contact_and_change_it_only_once_the_user_agrees(
        Profile profile)
    {
        var text = Render(profile);

        foreach (var each in ProfileCatalog.All)
        {
            Assert.Contains($"{Words.Of(each)} ({ProfileCatalog.Describe(each)})", text);
        }

        Assert.Contains("first contact with the user", text);
        Assert.Contains($"does not fit, name the profile that fits better", text);
        Assert.Contains("only after the user agrees", text);
        Assert.Contains("set_profile", text);
    }
}
