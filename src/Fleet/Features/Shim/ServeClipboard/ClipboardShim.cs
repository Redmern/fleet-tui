using System.Text;
using Fleet.Features.Shim.ServeClipboard.Enums;
using Fleet.Features.Shim.ServeClipboard.Models;
using Fleet.Ports;

namespace Fleet.Features.Shim.ServeClipboard;

public static class ClipboardShim
{
    public const string Xclip = "xclip";

    public const string WlPaste = "wl-paste";

    public const string Png = "image/png";

    public const string Targets = "TARGETS";

    public static IReadOnlyList<string> Tools { get; } = [Xclip, WlPaste];

    public static ShimCall Parse(string tool, IReadOnlyList<string> args) => tool switch
    {
        Xclip => ParseXclip(args),
        WlPaste => ParseWlPaste(args),
        _ => new ShimCall(tool, ShimRead.RealTool),
    };

    public static async Task<int> RunAsync(
        ShimCall call, IClipboardImageSource clipboard, Stream stdout, TextWriter stderr, CancellationToken ct = default)
    {
        if (call.Read is ShimRead.RealTool or ShimRead.OtherImage)
        {
            return await NoImageAsync(call, stderr).ConfigureAwait(false);
        }

        var image = call.Read == ShimRead.Png
            ? await clipboard.ReadPngAsync(ct).ConfigureAwait(false)
            : await clipboard.ProbePngAsync(ct).ConfigureAwait(false);
        if (image.Refused is { } refused)
        {
            await stderr.WriteLineAsync($"fleet: no clipboard image: {refused}").ConfigureAwait(false);
            return 1;
        }

        if (!image.HasPng || (call.Read == ShimRead.Png && image.Png is not { Length: > 0 }))
        {
            return await NoImageAsync(call, stderr).ConfigureAwait(false);
        }

        var output = call.Read == ShimRead.Png ? image.Png! : Encoding.UTF8.GetBytes(Listed(call.Tool));
        await stdout.WriteAsync(output, ct).ConfigureAwait(false);
        await stdout.FlushAsync(ct).ConfigureAwait(false);
        return 0;
    }

    private static string Listed(string tool) => tool == Xclip ? $"{Targets}\n{Png}\n" : $"{Png}\n";

    private static async Task<int> NoImageAsync(ShimCall call, TextWriter stderr)
    {
        await stderr.WriteLineAsync(call.Tool == Xclip
            ? $"Error: target {(call.Read == ShimRead.Types ? Targets : call.Mime)} not available"
            : "Nothing is copied").ConfigureAwait(false);
        return 1;
    }

    private static ShimCall ParseXclip(IReadOnlyList<string> args)
    {
        string? selection = null;
        string? target = null;
        var output = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (Abbreviates(arg, "-selection", 3) && i + 1 < args.Count)
            {
                selection = args[++i];
            }
            else if (Abbreviates(arg, "-target", 2) && i + 1 < args.Count)
            {
                target = args[++i];
            }
            else if (Abbreviates(arg, "-display", 2) && i + 1 < args.Count)
            {
                i++;
            }
            else if (Abbreviates(arg, "-out", 2))
            {
                output = true;
            }
            else if (!Abbreviates(arg, "-quiet", 2) && !Abbreviates(arg, "-silent", 3) && !Abbreviates(arg, "-noutf8", 2))
            {
                return new ShimCall(Xclip, ShimRead.RealTool);
            }
        }

        return output && selection is ['c', ..] && target is not null
            ? new ShimCall(Xclip, Classify(target), target)
            : new ShimCall(Xclip, ShimRead.RealTool);
    }

    private static ShimCall ParseWlPaste(IReadOnlyList<string> args)
    {
        string? type = null;
        var list = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg is "-l" or "--list-types")
            {
                list = true;
            }
            else if (arg is "-t" or "--type" && i + 1 < args.Count)
            {
                type = args[++i];
            }
            else if (arg.StartsWith("--type=", StringComparison.Ordinal))
            {
                type = arg["--type=".Length..];
            }
            else if (arg is not ("-n" or "--no-newline"))
            {
                return new ShimCall(WlPaste, ShimRead.RealTool);
            }
        }

        return (list, type) switch
        {
            (true, null) => new ShimCall(WlPaste, ShimRead.Types),
            (false, { } mime) => new ShimCall(WlPaste, Classify(mime), mime),
            _ => new ShimCall(WlPaste, ShimRead.RealTool),
        };
    }

    private static ShimRead Classify(string mime) => mime switch
    {
        Targets => ShimRead.Types,
        Png => ShimRead.Png,
        _ when mime.StartsWith("image/", StringComparison.Ordinal) => ShimRead.OtherImage,
        _ => ShimRead.RealTool,
    };

    private static bool Abbreviates(string arg, string option, int shortest) =>
        arg.Length >= shortest && option.StartsWith(arg, StringComparison.Ordinal);
}
