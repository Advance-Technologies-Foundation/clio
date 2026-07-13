using System.Text.Json.Serialization;

namespace ClioRing.Models;

/// <summary>
/// System.Text.Json source-generation context for all launcher configuration types.
/// Using the generated <see cref="JsonSerializerContext"/> keeps every (de)serialization
/// path free of reflection so the app publishes clean under NativeAOT (no IL2026/IL3050).
/// </summary>
[JsonSourceGenerationOptions(
	WriteIndented = true,
	PropertyNameCaseInsensitive = true,
	UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(ExperimentSettings))]
[JsonSerializable(typeof(ClioIpcSettingsDto))]
[JsonSerializable(typeof(EnvState))]
[JsonSerializable(typeof(WindowPlacement))]
[JsonSerializable(typeof(ActionCatalog))]
[JsonSerializable(typeof(RingAction))]
[JsonSerializable(typeof(ClioCommandSpec))]
[JsonSerializable(typeof(OpenUrlSpec))]
[JsonSerializable(typeof(OpenPathSpec))]
[JsonSerializable(typeof(ParameterDescriptor))]
[JsonSerializable(typeof(string))]
internal partial class RingJsonContext : JsonSerializerContext
{
}
