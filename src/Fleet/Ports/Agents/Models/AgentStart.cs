namespace Fleet.Ports.Agents.Models;

public sealed record AgentStart(string Project, string Repository, string Branch, string Task);
