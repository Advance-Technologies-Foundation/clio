using System.Text.Json;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Detects Creatio error payloads returned with a non-failing HTTP status by the underlying
/// transport. Two shapes are recognized: OData v4 errors (<c>{"error":{"message":...}}</c>) and
/// ASP.NET Web API errors (<c>{"Message":...,"ExceptionType":...,"StackTrace":...}</c>, e.g. the
/// EDM model-build NullReferenceException). Real OData entities and collections never carry these
/// members, so detecting them lets the odata-* tools report success=false instead of wrapping an
/// error body as data.
/// </summary>
internal static class ODataResponseError {
	public static bool TryDetect(JsonElement root, out string message) {
		message = string.Empty;
		if (root.ValueKind != JsonValueKind.Object) {
			return false;
		}

		// OData v4 error envelope.
		if (root.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.Object) {
			message = error.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.String
				? m.GetString()!
				: error.GetRawText();
			return true;
		}

		// ASP.NET Web API HttpError envelope (ExceptionType / ExceptionMessage never appear on real entities).
		bool isAspNetError = root.TryGetProperty("ExceptionType", out _)
			|| root.TryGetProperty("ExceptionMessage", out _)
			|| root.TryGetProperty("StackTrace", out _);
		if (isAspNetError) {
			message = First(root, "ExceptionMessage", "Message") ?? "Creatio returned a server error.";
			return true;
		}

		return false;
	}

	private static string? First(JsonElement root, params string[] names) {
		foreach (string name in names) {
			if (root.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String) {
				return el.GetString();
			}
		}
		return null;
	}
}
