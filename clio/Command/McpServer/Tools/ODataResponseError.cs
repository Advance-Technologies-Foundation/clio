using System;
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
	/// returns for an OData entity set that is not queryable yet. Its most common cause is the
	/// asynchronous OData rebuild that follows create-entity-schema/create-lookup, so the wording is
	/// deliberately retry-first (aligned with the core-rules guidance) and only escalates to
	/// compile/restart when a retry does not resolve it — never steering the agent to restart the
	/// whole application for what is usually a ~1-2 minute wait.
	/// </summary>
	internal const string UnregisteredEntityHint =
		"The OData entity set is not queryable yet. If it was just created with create-entity-schema or "
		+ "create-lookup, this is the expected ~1-2 min asynchronous OData rebuild: wait briefly and retry, "
		+ "do not compile or restart. Compile and restart only if it still fails after retrying (for example "
		+ "an entity deployed without compilation).";

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
		// set. Detection is deliberately locked to that shape and must never pre-empt a real payload:
		// a genuine OData response always carries another member — an @odata.context annotation
		// (present under the default OData metadata level this tool relies on), a value collection
		// wrapper, or the created record's Id — so a body whose only members are Message (+
		// MessageDetail) is an error, not data. NOTE: this safety rests on default OData metadata; a
		// single-entity read served with odata.metadata=none that selected only a Message-named column
		// would lose its distinguishing member and be misclassified. No current call site does that
		// (odata-read hits the collection endpoint; odata-create echoes an Id), so the precondition is
		// safe today — revisit this branch before adding a by-key/metadata=none read path.
		if (root.TryGetProperty("Message", out JsonElement bareMessage)
			&& bareMessage.ValueKind == JsonValueKind.String
			&& !HasNonRoutingErrorMembers(root)) {
			// Surface the most specific text: MessageDetail ("No type was found that matches the
			// controller named 'X'") when present, else the bare Message.
			string? detail = First(root, "MessageDetail");
			string primary = !string.IsNullOrEmpty(detail) ? detail! : bareMessage.GetString() ?? string.Empty;
			if (string.IsNullOrEmpty(primary)) {
				message = "Creatio returned an empty error response.";
				return true;
			}
			// The unregistered-entity hint (wait-and-retry, not compile/restart) is tied to a CONTENT
			// signal, not the bare {Message[,MessageDetail]} shape: other ASP.NET Web API HttpError
			// bodies can share that shape, and telling the agent to wait for an async rebuild on an
			// unrelated, non-transient failure would delay correct diagnosis. Append it only for the
			// genuine routing miss; otherwise surface the message alone (still success=false).
			message = IsRoutingMiss(detail) || IsRoutingMiss(bareMessage.GetString())
				? $"{primary} {UnregisteredEntityHint}"
				: primary;
			return true;
		}

		return false;
	}

	/// <summary>
	/// True when the text is one of the ASP.NET Web API routing-miss messages that identify an
	/// unregistered/uncompiled OData controller, as opposed to any other error that happens to share
	/// the bare <c>{Message[,MessageDetail]}</c> shape.
	/// </summary>
	private static bool IsRoutingMiss(string? text) =>
		!string.IsNullOrEmpty(text)
		&& (text!.Contains("No type was found that matches the controller", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("No HTTP resource was found that matches the request URI", StringComparison.OrdinalIgnoreCase));

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
