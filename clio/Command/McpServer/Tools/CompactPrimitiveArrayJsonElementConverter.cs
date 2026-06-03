using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Renders <see cref="JsonElement"/> values through <see cref="Utf8JsonWriter"/>
/// with one tweak relative to the built-in <see cref="JsonElement.WriteTo"/>:
/// arrays whose elements are all primitives (string, number, true/false, null)
/// are emitted on a single line, even when the surrounding writer is set to
/// <c>WriteIndented = true</c>. Everything else (objects, arrays containing
/// nested objects or arrays) keeps the writer's indentation.
///
/// Motivation: the curated component registry stores a lot of small enum
/// arrays (e.g. <c>"values": ["none","default","extra-small", …]</c>) that
/// break flow when each value lives on its own line — the surrounding type
/// definition becomes unreadable at a glance. Inlining the primitive bag
/// keeps the JSON pipe-friendly for `jq` callers while not collapsing the
/// nested object structure that humans need indented.
/// </summary>
internal sealed class CompactPrimitiveArrayJsonElementConverter : JsonConverter<JsonElement> {
	private static readonly JsonSerializerOptions CompactOptions = new() {
		WriteIndented = false,
	};

	public override JsonElement Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options) =>
		JsonElement.ParseValue(ref reader);

	public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options) {
		WriteElement(writer, value);
	}

	private static void WriteElement(Utf8JsonWriter writer, JsonElement element) {
		switch (element.ValueKind) {
			case JsonValueKind.Object:
				WriteObject(writer, element);
				return;
			case JsonValueKind.Array:
				WriteArray(writer, element);
				return;
			default:
				element.WriteTo(writer);
				return;
		}
	}

	private static void WriteObject(Utf8JsonWriter writer, JsonElement element) {
		writer.WriteStartObject();
		foreach (JsonProperty property in element.EnumerateObject()) {
			writer.WritePropertyName(property.Name);
			WriteElement(writer, property.Value);
		}
		writer.WriteEndObject();
	}

	private static void WriteArray(Utf8JsonWriter writer, JsonElement element) {
		if (IsPrimitiveOnly(element)) {
			// Re-serialise the primitive-only array with WriteIndented=false to drop
			// any whitespace the producer may have placed between elements
			// (`["a", "b"]` vs `["a","b"]`), then push the bytes through WriteRawValue
			// so the surrounding writer's indentation does not apply to them.
			// GetRawText() alone would preserve the producer's whitespace verbatim.
			string compactJson = JsonSerializer.Serialize(element, CompactOptions);
			writer.WriteRawValue(compactJson, skipInputValidation: true);
			return;
		}
		writer.WriteStartArray();
		foreach (JsonElement item in element.EnumerateArray()) {
			WriteElement(writer, item);
		}
		writer.WriteEndArray();
	}

	private static bool IsPrimitiveOnly(JsonElement arrayElement) {
		foreach (JsonElement item in arrayElement.EnumerateArray()) {
			switch (item.ValueKind) {
				case JsonValueKind.Object:
				case JsonValueKind.Array:
					return false;
			}
		}
		return true;
	}
}
