using System.Text.Json;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Detects Creatio error payloads returned with a non-failing HTTP status by the underlying
/// transport. Three shapes are recognized: OData v4 errors (<c>{"error":{"message":...}}</c>),
/// ASP.NET Web API exception errors (<c>{"Message":...,"ExceptionType":...,"StackTrace":...}</c>,
/// e.g. the EDM model-build NullReferenceException), and ASP.NET Web API routing errors
/// (<c>{"Message":...,"MessageDetail":...}</c>, e.g. a 404 for an unregistered/uncompiled OData
/// controller). Real OData entities and collections never carry these members, so detecting them
/// lets the odata-* tools report success=false instead of wrapping an error body as data.
/// </summary>
internal static class ODataResponseError {
	/// <summary>
	/// Hint appended to a detected routing error. A 404 "no controller found" is the shape Creatio
	/// returns for an OData entity set that is not registered yet — most commonly a freshly-created
	/// custom object or lookup whose schema has not been compiled/published, so it must not be read
	/// as a data gap.
	/// </summary>
	internal const string UnregisteredEntityHint =
		"The OData entity set may not be registered: a freshly-created custom object or lookup is not "
		+ "queryable by OData entity name until its schema is compiled and the application is restarted.";

	/// <summary>
	/// Attempts to recognize a Creatio error body that the transport returned with a non-failing
	/// HTTP status, so the odata-* tools can report <c>success=false</c> instead of wrapping the
	/// error as data.
	/// </summary>
	/// <param name="root">The parsed root JSON element of the response body.</param>
	/// <param name="message">
	/// When the method returns <see langword="true"/>, receives the extracted error text (for a
	/// routing error the unregistered-entity hint is appended); otherwise an empty string.
	/// </param>
	/// <returns><see langword="true"/> when <paramref name="root"/> is a recognized error body.</returns>
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

		// ASP.NET Web API routing error: { "Message": ..., "MessageDetail": ... } with no exception
		// members. This is a 404 "No HTTP resource / no controller found" for an unregistered entity
		// set. A real OData response always carries other members (@odata.context, value, entity
		// columns), so a body whose only members are Message (+ MessageDetail) is an error, not data.
		// The detection is deliberately locked to that shape: it must never pre-empt a real payload.
		if (root.TryGetProperty("Message", out JsonElement bareMessage)
			&& bareMessage.ValueKind == JsonValueKind.String
			&& !HasNonRoutingErrorMembers(root)) {
			// MessageDetail ("No type was found that matches the controller named 'X'") is the routing
			// discriminator, so the unregistered-entity hint is appended only when it is present; a bare
			// Message body is surfaced verbatim to avoid misattributing an unrelated error's cause.
			message = root.TryGetProperty("MessageDetail", out JsonElement detail)
				&& detail.ValueKind == JsonValueKind.String
				? $"{detail.GetString()} {UnregisteredEntityHint}"
				: bareMessage.GetString()!;
			return true;
		}

		return false;
	}

	/// <summary>
	/// Returns true when the object carries any member other than the routing-error keys
	/// (<c>Message</c> / <c>MessageDetail</c>), which indicates a real OData payload (metadata,
	/// <c>value</c>, or entity columns) rather than a bare error body.
	/// </summary>
	private static bool HasNonRoutingErrorMembers(JsonElement root) {
		foreach (JsonProperty property in root.EnumerateObject()) {
			if (!property.NameEquals("Message") && !property.NameEquals("MessageDetail")) {
				return true;
			}
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
