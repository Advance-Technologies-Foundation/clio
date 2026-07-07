namespace Clio.Command {
	using System.Linq;
	using Newtonsoft.Json;
	using Newtonsoft.Json.Linq;

	/// <summary>
	/// Shared parsing for the <c>optional-properties</c> page argument: a JSON array of
	/// <c>{key, value}</c> objects merged into a Freedom UI schema's <c>optionalProperties</c>.
	/// Both <c>create-page</c> and <c>update-page</c> use this so the accepted shape and the
	/// user-facing error stay identical across the two surfaces.
	/// </summary>
	internal static class PageOptionalPropertiesHelper {
		/// <summary>
		/// Canonical user-facing error for a malformed <c>optional-properties</c> payload.
		/// </summary>
		internal const string InvalidOptionalPropertiesError =
			"optional-properties must be a valid JSON array of {key, value} objects";

		/// <summary>
		/// Parses the raw <c>optional-properties</c> argument into a <see cref="JArray"/>.
		/// A null/whitespace payload is treated as "not supplied": returns <c>true</c> with
		/// <paramref name="parsed"/> set to <c>null</c>. A non-empty payload that is not a valid
		/// JSON array of objects each carrying a non-blank <c>key</c> returns <c>false</c> with
		/// <paramref name="error"/> set to <see cref="InvalidOptionalPropertiesError"/> —
		/// element-shape validation is deliberate: a typo'd key would otherwise be written
		/// verbatim into the schema and silently break the dashboard link-back.
		/// </summary>
		/// <param name="json">The raw argument value, or <c>null</c>.</param>
		/// <param name="parsed">The parsed array, or <c>null</c> when the payload is absent.</param>
		/// <param name="error">The canonical error message when parsing fails; otherwise <c>null</c>.</param>
		/// <returns><c>true</c> when the payload is absent or well-formed; <c>false</c> when malformed.</returns>
		internal static bool TryParse(string json, out JArray parsed, out string error) {
			parsed = null;
			error = null;
			if (string.IsNullOrWhiteSpace(json)) {
				return true;
			}
			try {
				parsed = JArray.Parse(json);
			} catch (JsonReaderException) {
				error = InvalidOptionalPropertiesError;
				return false;
			}
			if (parsed.Any(token => token is not JObject item || string.IsNullOrWhiteSpace(item["key"]?.ToString()))) {
				parsed = null;
				error = InvalidOptionalPropertiesError;
				return false;
			}
			return true;
		}
	}
}
