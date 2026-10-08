using Fleet.Ports.Agents;
using Fleet.Ports.Mux;

namespace Fleet.Features.Agents.HideAgent;

public sealed class HideAllAgentsHandler(IMuxDriver mux, IAgentStore store)
{
    public async Task<int> HandleAsync(string project, CancellationToken ct = default)
    {
        var hider = new HideAgentHandler(mux, store);
        var hidden = 0;

        foreach (var agent in store.List(project).ToList())
        {
            var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);

            if (!AgentPanes.Shown(agent, panes))
            {
                continue;
            }

            var outcome = await hider
                .HandleAsync(project, agent with { Hidden = false }, dashboardWindow: null, panes, ct)
                .ConfigureAwait(false);

            if (outcome.Succeeded)
            {
                hidden++;
            }
        }

        return hidden;
    }

    public static string Summary(int hidden) =>
        hidden == 0
            ? "no agents are showing in the terminal."
            : $"hid {hidden} agent(s); they are still listed in the dashboard.";
}
