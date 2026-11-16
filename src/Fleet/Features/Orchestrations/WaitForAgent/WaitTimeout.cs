namespace Fleet.Features.Orchestrations.WaitForAgent;

public sealed record WaitTimeout(TimeSpan Value, string ClampNote)
{
    public bool Clamped => ClampNote.Length > 0;
}
