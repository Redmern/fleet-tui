using System.Text;
using System.Text.RegularExpressions;
using Fleet.Features.Shim.Mac.Enums;
using Fleet.Features.Shim.Mac.Models;
using Fleet.Ports;

namespace Fleet.Features.Shim.Mac;

public static partial class MacClipboardShim
{
    public const string Osascript = "osascript";

    public const string Pngpaste = "pngpaste";

    public const string NoPngf = "execution error: Can’t make the clipboard into type «class PNGf». (-1700)";

    public const string NoPng = "No PNG data found on the clipboard!";

    public static IReadOnlyList<string> Tools { get; } = [Osascript, Pngpaste];

    public static MacShimCall Parse(string tool, IReadOnlyList<string> args) => tool switch
    {
        Osascript => ParseOsascript(args),
        Pngpaste => ParsePngpaste(args),
        _ => new MacShimCall(tool, MacRead.RealTool),
    };

    public static async Task<int> RunAsync(
        MacShimCall call, IClipboardImageSource clipboard, Stream stdout, TextWriter stderr, CancellationToken ct = default)
    {
        if (call.Read == MacRead.RealTool)
        {
            return await NoImageAsync(call, stderr).ConfigureAwait(false);
        }

        var image = call.Read == MacRead.Probe
            ? await clipboard.ProbePngAsync(ct).ConfigureAwait(false)
            : await clipboard.ReadPngAsync(ct).ConfigureAwait(false);
        if (image.Refused is { } refused)
        {
            await stderr.WriteLineAsync($"fleet: no clipboard image: {refused}").ConfigureAwait(false);
            return 1;
        }

        if (!image.HasPng || (call.Read != MacRead.Probe && image.Png is not { Length: > 0 }))
        {
            return await NoImageAsync(call, stderr).ConfigureAwait(false);
        }

        switch (call.Read)
        {
            case MacRead.File:
                try
                {
                    await File.WriteAllBytesAsync(call.File!, image.Png!, ct).ConfigureAwait(false);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    await stderr.WriteLineAsync($"fleet: cannot write {call.File}: {e.Message}").ConfigureAwait(false);
                    return 1;
                }

                break;
            case MacRead.Stdout:
                await stdout.WriteAsync(image.Png!, ct).ConfigureAwait(false);
                break;
            case MacRead.Base64:
                await stdout.WriteAsync(Encoding.ASCII.GetBytes(Convert.ToBase64String(image.Png!)), ct).ConfigureAwait(false);
                break;
        }

        await stdout.FlushAsync(ct).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> NoImageAsync(MacShimCall call, TextWriter stderr)
    {
        await stderr.WriteLineAsync(call.Tool == Osascript ? NoPngf : NoPng).ConfigureAwait(false);
        return 1;
    }

    private static MacShimCall ParseOsascript(IReadOnlyList<string> args)
    {
        var lines = new List<string>();
        for (var i = 0; i < args.Count; i += 2)
        {
            if (args[i] != "-e" || i + 1 >= args.Count)
            {
                return new MacShimCall(Osascript, MacRead.RealTool);
            }

            lines.AddRange(args[i + 1].Split('\n', '\r').Select(Normalize).Where(line => line.Length > 0));
        }

        if (lines is [var single] && ProbeLine().IsMatch(single))
        {
            return new MacShimCall(Osascript, MacRead.Probe);
        }

        if (lines is [var read, var open, var write, var close]
            && ReadLine().Match(read) is { Success: true } data
            && OpenLine().Match(open) is { Success: true } file
            && write == $"write {data.Groups["data"].Value} to {file.Groups["handle"].Value}"
            && close == $"close access {file.Groups["handle"].Value}")
        {
            return new MacShimCall(Osascript, MacRead.File, Unescape(file.Groups["path"].Value));
        }

        return new MacShimCall(Osascript, MacRead.RealTool);
    }

    private static MacShimCall ParsePngpaste(IReadOnlyList<string> args)
    {
        var base64 = false;
        string? target = null;

        foreach (var arg in args)
        {
            if (arg == "-b")
            {
                base64 = true;
            }
            else if (target is null && (arg == "-" || !arg.StartsWith('-')))
            {
                target = arg;
            }
            else
            {
                return new MacShimCall(Pngpaste, MacRead.RealTool);
            }
        }

        return (base64, target) switch
        {
            (_, null) => new MacShimCall(Pngpaste, MacRead.RealTool),
            (true, _) => new MacShimCall(Pngpaste, MacRead.Base64),
            (false, "-") => new MacShimCall(Pngpaste, MacRead.Stdout),
            (false, { } file) when Path.GetExtension(file) is "" || Path.GetExtension(file).Equals(".png", StringComparison.OrdinalIgnoreCase)
                => new MacShimCall(Pngpaste, MacRead.File, file),
            _ => new MacShimCall(Pngpaste, MacRead.RealTool),
        };
    }

    private static string Normalize(string line) =>
        Spaces().Replace(line.Replace("<<", "«", StringComparison.Ordinal).Replace(">>", "»", StringComparison.Ordinal), " ").Trim();

    private static string Unescape(string text) => Escaped().Replace(text, "$1");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\\(.)")]
    private static partial Regex Escaped();

    [GeneratedRegex(@"^\(?(get )?the clipboard as «class PNGf»\)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ProbeLine();

    [GeneratedRegex(@"^set (?<data>\w+) to \(?the clipboard as «class PNGf»\)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReadLine();

    [GeneratedRegex(@"^set (?<handle>\w+) to open for access (POSIX file )?""(?<path>(?:[^""\\]|\\.)*)"" with write permission$", RegexOptions.IgnoreCase)]
    private static partial Regex OpenLine();
}
