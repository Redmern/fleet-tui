using Fleet.Shared.Aidlc;
using Fleet.Shared.Aidlc.Enums;
using Fleet.Shared.Orchestrations.Enums;
using Fleet.Shared.Orchestrations.Models;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Shared.Orchestrations;

public static class DispatchRouting
{
    public static DispatchRoute Decide(
        string prompt,
        AidlcMode mode,
        Profile projectDefault,
        Profile? argument = null,
        string? repository = null,
        bool research = false)
    {
        var (task, aidlc) = ResolveAidlc(prompt, mode, projectDefault, argument);
        var isResearch = research || aidlc?.Profile == Profile.Research;

        var direct = !string.IsNullOrWhiteSpace(repository) && aidlc is null && !isResearch;

        return new DispatchRoute(
            direct ? DispatchTarget.RepoAgent : DispatchTarget.SubOrchestrator,
            task,
            aidlc,
            isResearch);
    }

    public static (string Prompt, (Profile Profile, ProfileSource Source)? Aidlc) ResolveAidlc(
        string prompt, AidlcMode mode, Profile projectDefault, Profile? argument)
    {
        if (mode == AidlcMode.Off)
        {
            return (prompt, null);
        }

        var (prefixed, task) = ProfilePrefix.Split(prompt);

        if (prefixed is { } fromPrefix)
        {
            return (task, (fromPrefix, ProfileSource.Prefix));
        }

        if (mode != AidlcMode.On)
        {
            return (prompt, null);
        }

        return argument is { } fromArgument
            ? (prompt, (fromArgument, ProfileSource.Argument))
            : (prompt, (projectDefault, ProfileSource.ProjectDefault));
    }
}
