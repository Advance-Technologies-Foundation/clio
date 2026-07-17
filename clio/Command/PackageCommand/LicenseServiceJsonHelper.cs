namespace Clio.Command.PackageCommand
{
	using System;
	using System.Text.Json;

	/// <summary>
	/// Shared JSON traversal for LicenseManagerProxyService.svc responses, which wrap their payload
	/// array under a service-specific key ("licenses", "users", ...) rather than one fixed shape.
	/// </summary>
	internal static class LicenseServiceJsonHelper
	{
		private static readonly string[] FallbackWrapperKeys = { "collection", "value", "result", "data", "items", "rows" };

		/// <summary>
		/// Returns the payload array: <paramref name="root"/> itself when it is already an array,
		/// otherwise <paramref name="preferredKey"/> when present, then a small set of common wrapper
		/// keys, then the first array-valued property found.
		/// </summary>
		public static JsonElement UnwrapArray(JsonElement root, string preferredKey) {
			if (root.ValueKind == JsonValueKind.Array) {
				return root;
			}
			if (root.ValueKind == JsonValueKind.Object) {
				if (root.TryGetProperty(preferredKey, out JsonElement preferred) && preferred.ValueKind == JsonValueKind.Array) {
					return preferred;
				}
				foreach (string key in FallbackWrapperKeys) {
					if (root.TryGetProperty(key, out JsonElement candidate) && candidate.ValueKind == JsonValueKind.Array) {
						return candidate;
					}
				}
				foreach (JsonProperty property in root.EnumerateObject()) {
					if (property.Value.ValueKind == JsonValueKind.Array) {
						return property.Value;
					}
				}
			}
			throw new InvalidOperationException(
				"Response did not contain a recognizable array under a known wrapper key.");
		}

		/// <summary>
		/// Case-insensitive lookup of a string property directly on <paramref name="obj"/>.
		/// </summary>
		public static string TryGetString(JsonElement obj, string propertyName) {
			if (obj.ValueKind != JsonValueKind.Object) {
				return null;
			}
			foreach (JsonProperty property in obj.EnumerateObject()) {
				if (property.Value.ValueKind == JsonValueKind.String &&
					string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)) {
					return property.Value.GetString();
				}
			}
			return null;
		}
	}
}
