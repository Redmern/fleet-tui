using System.Text.Json;
using System.Text.Json.Serialization;
using Fleet.Shared.Keybinds.Models;

namespace Fleet.Shared.Keybinds;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(KeybindsFile))]
public sealed partial class KeybindsJsonContext : JsonSerializerContext;
