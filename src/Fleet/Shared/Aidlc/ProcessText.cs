using System.Text;
using Fleet.Shared.Aidlc.Enums;
using Fleet.Shared.Aidlc.Models;

namespace Fleet.Shared.Aidlc;

public static class ProcessText
{
    public const string ApprovalMark = "the user approves";

    public static string Render(EffectivePlan plan, string? extra = null)
    {
        var text = new StringBuilder();

        text.AppendLine(
            $"This task runs under fleet's Ai-DLC process with the **{Words.Of(plan.Profile)}** profile "
            + $"({ProfileCatalog.Describe(plan.Profile)}).");
        text.AppendLine(
            "fleet keeps the record in this folder: state.json holds the stages and their states, and "
            + "audit.jsonl logs what happened. Both belong to fleet: read them, never edit them.");
        text.AppendLine();
        text.AppendLine(
            "Work through these stages in order, and say which stage you are in each time you update REPORT.md:");

        var number = 0;

        foreach (var stage in plan.Running)
        {
            number++;
            var gate = stage.HumanGate ? $" Gate: **{ApprovalMark}**." : string.Empty;
            text.AppendLine($"{number}. **{Title(stage.Stage)}** — {What(stage.Stage, plan)}{gate}");
        }

        if (plan.Skipped.Any())
        {
            text.AppendLine();
            text.AppendLine(
                "Skipped: "
                + string.Join(", ", plan.Skipped.Select(s => $"{Words.Of(s.Stage)} ({s.Reason})"))
                + ".");
        }

        text.AppendLine();
        text.AppendLine(Gates(plan));
        text.AppendLine();
        text.Append(
            "A failure always stops the run and goes to the user, whatever the autonomy setting. "
            + "If the profile looks wrong for this task, say so at the first point you talk to the user.");

        if (!string.IsNullOrWhiteSpace(extra))
        {
            text.AppendLine();
            text.AppendLine();
            text.AppendLine("### Project guidance");
            text.Append(extra.Trim());
        }

        return text.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public static string Title(Stage stage) => stage.ToString();

    private static string Gates(EffectivePlan plan)
    {
        if (!plan.Gated.Any())
        {
            return "No stage needs the user's approval: move on once a stage's work is done.";
        }

        return
            $"Stages marked \"{ApprovalMark}\" are human gates. At each one: stop, give the user a short "
            + "summary and the path of the stage's artifact, ask them to approve it or request changes, and "
            + "wait for their reply. Do not start the next stage, or any agent for it, until they approve. "
            + "If they request changes, revise and ask again. Note each decision in REPORT.md.";
    }

    private static string What(Stage stage, EffectivePlan plan) => stage switch
    {
        Stage.Intake => "done by fleet: TASK.md holds the request verbatim, state.json and audit.jsonl are created.",
        Stage.Discover =>
            "read the repositories the task touches (list_repositories, each repository's CLAUDE.md and docs) "
            + "and write discover.md: which repositories are involved, how each is built and tested, and the "
            + "files that matter.",
        Stage.Specify => Specify(plan.Profile),
        Stage.Plan => Plan(plan.Profile),
        Stage.Build => Build(plan),
        Stage.Verify =>
            "for each unit, make sure its verify command passed at the unit's HEAD commit. Ask the agent for "
            + "the command, the commit SHA and the tail of the output, and record them in REPORT.md. An agent "
            + "saying \"tests pass\" is not evidence.",
        Stage.Review =>
            "review each unit's actual diff (git diff against its base, not the agent's summary) against "
            + "the task and spec.md: every acceptance criterion met with evidence (a test name or file:line), "
            + "correctness bugs, new code where existing code could be reused, files changed outside what the "
            + "unit owns, tests that would pass without the change, and the repository's own rules. Write "
            + "reviews/<unit>-<round>.md and send the findings back with tell_agent. After two rejected rounds, "
            + "stop and ask the user.",
        Stage.Deliver => Deliver(plan.Profile),
        Stage.Learn =>
            "write learnings.md: the corrections, surprises and rules worth keeping from this task, as a list. "
            + "Ask the user which ones to keep.",
        _ => string.Empty,
    };

    private static string Specify(Profile profile) => profile switch
    {
        Profile.Express =>
            "write a short spec.md (about five lines): the problem, what is in scope, and numbered, testable "
            + "acceptance criteria (AC-1, AC-2, ...).",
        Profile.Bugfix =>
            "write spec.md: the reproduction (exact steps or a failing input), the expected behaviour, the "
            + "actual behaviour, and numbered, testable acceptance criteria (AC-1, AC-2, ...), one of which is "
            + "that the reproduction now behaves as expected.",
        _ =>
            "write spec.md: the problem, scope and non-scope, numbered, testable acceptance criteria (AC-1, "
            + "AC-2, ...; EARS style: \"When <trigger>, the <system> shall <response>\"), assumptions, and open "
            + "questions. Resolve every open question or mark it explicitly deferred.",
    };

    private static string Plan(Profile profile)
    {
        var plan =
            "write design.md (the decisions, and the options you rejected) and units.json: one unit per "
            + "coherent PR, each with id, title, repository, branch, dependsOn, acceptance (AC ids), owns "
            + "(file globs), verify (the command that proves it) and skeleton. Every acceptance criterion "
            + "belongs to at least one unit; units that can run at the same time must not own the same "
            + "files. Include the test strategy and the verify command for each repository.";

        return profile == Profile.Refactor
            ? plan + " Behaviour must not change: say how the tests will show that."
            : plan;
    }

    private static string Build(EffectivePlan plan)
    {
        var text = new StringBuilder(
            plan.For(Stage.Plan) is { Runs: true }
                ? "start one agent per unit with new_agent, in dependency order (independent units may run in "
                  + "parallel). "
                : "start an agent with new_agent for the change, usually one repository and one branch. ");

        text.Append(
            "Give each agent a self-contained brief: the goal, its acceptance criteria word for word, the "
            + "files it owns, what is not its job, and the definition of done (tests first, small conventional "
            + "commits, report done). Keep progress/<unit>.md with interpretations, deviations, trade-offs and "
            + "open questions.");

        if (plan.Profile == Profile.Bugfix)
        {
            text.Append(" A failing test that reproduces the bug comes first.");
        }

        if (plan.Profile == Profile.Refactor)
        {
            text.Append(" Characterisation tests that pin today's behaviour come first.");
        }

        if (plan.WalkingSkeleton)
        {
            text.Append(
                " The first unit is a walking skeleton: run it alone and end to end; the user approves it "
                + "before any other unit starts.");
        }

        if (plan.For(Stage.Plan) is { Runs: true })
        {
            text.Append(plan.Autonomy == Autonomy.Automatic
                ? " After that, go on from unit to unit without asking."
                : " After that, check in with the user after each unit.");
        }

        return text.ToString();
    }

    private static string Deliver(Profile profile) => profile switch
    {
        Profile.Research =>
            "write the final report: REPORT.md with the findings and the recommendation, longer material in "
            + "reports/.",
        Profile.Feature =>
            "have each repository's agent push its branch and open a PR (gh pr create), with spec.md copied "
            + "into the repository as docs/specs/<this folder's name>.md. Never merge: merging is the user's. "
            + "Finish REPORT.md with what shipped, the PR links, and what did not ship.",
        _ =>
            "have each repository's agent push its branch and open a PR (gh pr create). Never merge: merging "
            + "is the user's. Finish REPORT.md with what shipped, the PR links, and what did not ship.",
    };
}
