using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Platform.Mux.Embedded.Daemon;

public sealed class ViewerAnswer(string viewer, string? build)
{
    public string Viewer { get; } = viewer;

    public string? Build { get; } = build;

    public string? Fallback { get; set; }

    public TaskCompletionSource<ForwardResultDto> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
