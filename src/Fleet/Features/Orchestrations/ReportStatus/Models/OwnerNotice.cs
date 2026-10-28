using Fleet.Ports.Agents.Models;

namespace Fleet.Features.Orchestrations.ReportStatus.Models;

public sealed record OwnerNotice(AgentRecord? Owner, string Name, string Folder, string Message);
