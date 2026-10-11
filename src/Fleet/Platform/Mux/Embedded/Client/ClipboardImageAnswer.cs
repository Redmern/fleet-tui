using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Ports;

namespace Fleet.Platform.Mux.Embedded.Client;

public static class ClipboardImageAnswer
{
    public static async Task<ClipboardImageResponse> ForAsync(
        ClipboardImageRequest request, IClipboardImageSource clipboard, CancellationToken ct = default)
    {
        try
        {
            var image = request.Probe
                ? await clipboard.ProbePngAsync(ct).ConfigureAwait(false)
                : await clipboard.ReadPngAsync(ct).ConfigureAwait(false);
            return image.Refused is { } refused ? new ClipboardImageResponse { Id = request.Id, Error = refused }
                : request.Probe ? ClipboardImageResponse.Probed(request.Id, image.HasPng)
                : ClipboardImageResponse.For(request.Id, image.Png);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException
                                      or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            return new ClipboardImageResponse { Id = request.Id, Error = $"could not read the clipboard: {e.Message}" };
        }
    }
}
