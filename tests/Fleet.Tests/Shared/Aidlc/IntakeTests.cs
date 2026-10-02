using Fleet.Shared.Aidlc;
using Fleet.Shared.Aidlc.Enums;

namespace Fleet.Tests.Shared.Aidlc;

public class IntakeTests
{
    [Theory]
    [InlineData("feature: add oauth", Profile.Feature, "add oauth")]
    [InlineData("  Bugfix:the login loops", Profile.Bugfix, "the login loops")]
    [InlineData("research:\n how do others do it", Profile.Research, "how do others do it")]
    public void A_profile_prefix_picks_the_profile_and_is_stripped(string prompt, Profile profile, string task)
    {
        var (picked, rest) = ProfilePrefix.Split(prompt);

        Assert.Equal(profile, picked);
        Assert.Equal(task, rest);
    }

    [Theory]
    [InlineData("feature flags for the api")]
    [InlineData("fix this: the login loops")]
    [InlineData(": nothing")]
    [InlineData("1: one")]
    [InlineData("express,feature: both")]
    [InlineData("plain task")]
    public void A_prompt_without_a_profile_prefix_is_left_alone(string prompt)
    {
        var (picked, rest) = ProfilePrefix.Split(prompt);

        Assert.Null(picked);
        Assert.Equal(prompt, rest);
    }

    [Fact]
    public void The_prefix_is_the_profile_word_and_a_colon()
    {
        Assert.Equal("refactor:", ProfilePrefix.For(Profile.Refactor));
    }

    [Fact]
    public void Intake_marks_itself_done_and_keeps_the_rest_of_the_plan()
    {
        var plan = ProcessPlan.Resolve(Profile.Feature, Autonomy.Guided, AidlcPart.Review);

        var record = Intake.Start("add-oauth", plan, ProfileSource.Prefix, "2026-10-02T10:00:00Z");

        Assert.Equal("add-oauth", record.State.Slug);
        Assert.Equal(Profile.Feature, record.State.Profile);
        Assert.Equal(StageState.Done, record.State.Stages[0].State);
        Assert.Equal(StageState.Skipped, record.State.Stages.Single(s => s.Stage == Stage.Review).State);
        Assert.All(
            record.State.Stages.Where(s => s.Stage is not (Stage.Intake or Stage.Review)),
            s => Assert.Equal(StageState.Pending, s.State));
        Assert.Empty(record.State.Units);
        Assert.Equal("2026-10-02T10:00:00Z", record.State.Created);
        Assert.Equal(record.State.Created, record.State.Updated);
    }

    [Fact]
    public void Intake_logs_creation_the_profile_with_its_source_and_each_skipped_stage()
    {
        var plan = ProcessPlan.Resolve(Profile.Feature, Autonomy.Guided, AidlcPart.Verify | AidlcPart.Learn);

        var events = Intake.Start("s", plan, ProfileSource.ProjectDefault, "t").Events;

        Assert.Equal(
            [AuditEvent.IntentCreated, AuditEvent.ProfileSet, AuditEvent.StageSkipped, AuditEvent.StageSkipped],
            events.Select(e => e.Event));
        Assert.All(events, e => Assert.Equal(AuditActor.Engine, e.Actor));
        Assert.Equal("feature (the project default)", events[1].Detail);
        Assert.Equal("verify: off in settings", events[2].Detail);
        Assert.Equal("learn: off in settings", events[3].Detail);
    }
}
