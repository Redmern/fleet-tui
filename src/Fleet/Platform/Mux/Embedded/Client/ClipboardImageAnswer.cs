using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Ports;

namespace Fleet.Platform.Mux.Embedded.Client;

public static class ClipboardImageAnswer
{
    public static async Task<ClipboardImageResponse> ForAsync(
        ClipboardImageRequest request, IClipboardImageSource clipboard, Action<string>? notice = null, CancellationToken ct = default)
    {
        try
        {
            var image = request.Probe
                ? await clipboard.ProbePngAsync(ct).ConfigureAwait(false)
                : await clipboard.ReadPngAsync(ct).ConfigureAwait(false);
            var answer = image.Refused is { } refused ? new ClipboardImageResponse { Id = request.Id, Error = refused }
                : request.Probe ? ClipboardImageResponse.Probed(request.Id, image.HasPng)
                : ClipboardImageResponse.For(request.Id, image.Png);

            if (answer.Data is { Length: > 0 } png)
            {
                notice?.Invoke(Notice(request.Pane, png.Length));
            }
            else if (request.Paste)
            {
                notice?.Invoke(answer.Error is { } error
                    ? $"cannot paste an image into {request.Pane}: {error}"
                    : $"no image on your clipboard to paste into {request.Pane}");
            }

            return answer;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException
                                      or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            return new ClipboardImageResponse { Id = request.Id, Error = $"could not read the clipboard: {e.Message}" };
        }
    }

    public static string Notice(string pane, int bytes) =>
        $"remote pane {pane} read your clipboard image ({Math.Max(1, bytes / 1024)} KiB)";
}
