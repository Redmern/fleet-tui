namespace Fleet.Ports.Orchestrations;

public interface ISlugNamer
{
    Task<string?> NameAsync(string prompt, CancellationToken ct = default);
}
