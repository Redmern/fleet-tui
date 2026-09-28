using System.Text.Json.Serialization;

namespace Fleet.Platform.Mux.Embedded.Protocol;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Hello))]
[JsonSerializable(typeof(Welcome))]
[JsonSerializable(typeof(ErrorMessage))]
[JsonSerializable(typeof(KeyMessage))]
[JsonSerializable(typeof(TextMessage))]
[JsonSerializable(typeof(MouseMessage))]
[JsonSerializable(typeof(ResizeMessage))]
[JsonSerializable(typeof(CommandMessage))]
[JsonSerializable(typeof(BadgeMessage))]
[JsonSerializable(typeof(HostEffect))]
[JsonSerializable(typeof(ControlRequest))]
[JsonSerializable(typeof(ControlResponse))]
public partial class WireJsonContext : JsonSerializerContext;
