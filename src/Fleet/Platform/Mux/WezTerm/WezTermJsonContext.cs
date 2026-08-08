using System.Text.Json.Serialization;
using Fleet.Platform.Mux.WezTerm.Models;

namespace Fleet.Platform.Mux.WezTerm;

[JsonSerializable(typeof(WezTermPaneJson[]))]
public partial class WezTermJsonContext : JsonSerializerContext;
