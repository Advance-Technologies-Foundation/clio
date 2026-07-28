using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ClioRing.Ipc;

/// <summary>
/// Redacts credential-shaped values from a JSON request before it is shown in the UI, written to a
/// log, or put in a review summary. Never let a password / secret / token reach a surface. Matches
/// keys case-insensitively by substring (password, secret, login, token, -p, clientSecret, …).
/// AOT-safe (JsonDocument + Utf8JsonWriter, no reflection).
/// </summary>
public static class SecretRedactor {
	private static readonly string[] SecretKeyMarkers =
		{ "password", "secret", "login", "token", "credential", "apikey", "api-key" };

	private const string Mask = "****";

	/// <summary>
	/// Returns <paramref name="json"/> with any credential-shaped string values replaced by <c>****</c>.
	/// If the input is not valid JSON it is returned unchanged (callers pass constructed JSON).
	/// </summary>
	public static string Redact(string? json) {
		if (string.IsNullOrWhiteSpace(json)) {
			return string.Empty;
		}
		try {
			using JsonDocument doc = JsonDocument.Parse(json);
			var buffer = new MemoryStream();
			using (var writer = new Utf8JsonWriter(buffer)) {
				WriteRedacted(doc.RootElement, writer);
			}
			return Encoding.UTF8.GetString(buffer.ToArray());
		}
		catch (JsonException) {
			return json;
		}
	}

	/// <summary>True when a property name looks like a credential field.</summary>
	public static bool IsSecretKey(string name) {
		foreach (string marker in SecretKeyMarkers) {
			if (name.Contains(marker, StringComparison.OrdinalIgnoreCase)) {
				return true;
			}
		}
		// Bare "-p" / "p" short password flags.
		return string.Equals(name, "-p", StringComparison.OrdinalIgnoreCase);
	}

	private static void WriteRedacted(JsonElement element, Utf8JsonWriter writer, string? propertyName = null) {
		switch (element.ValueKind) {
			case JsonValueKind.Object:
				writer.WriteStartObject();
				foreach (JsonProperty prop in element.EnumerateObject()) {
					writer.WritePropertyName(prop.Name);
					if (IsSecretKey(prop.Name) && prop.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number) {
						writer.WriteStringValue(Mask);
					}
					else {
						WriteRedacted(prop.Value, writer, prop.Name);
					}
				}
				writer.WriteEndObject();
				break;
			case JsonValueKind.Array:
				writer.WriteStartArray();
				foreach (JsonElement item in element.EnumerateArray()) {
					WriteRedacted(item, writer);
				}
				writer.WriteEndArray();
				break;
			default:
				element.WriteTo(writer);
				break;
		}
	}
}
