using System;
using System.Linq;
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
	/// Hint appended when a write response body could not be parsed as JSON at all. Creatio's OData
	/// pipeline never returns a non-JSON body by itself - even a server error comes back as one of the
	/// shapes <see cref="TryDetect"/> recognizes. A non-JSON body (an HTML error page, a plain-text
	/// block) therefore means the request did not reach Creatio's OData controller intact: a proxy/IIS
	/// routing error, or a session redirect page the reauth executor did not recognize as expired.
	/// Retrying immediately will not help with the former, and the side effect of the write itself is
	/// unverified either way - the caller must confirm the actual state before retrying.
	/// </summary>
	internal const string NonJsonResponseHint =
		"The response was not JSON, which Creatio's OData pipeline never returns by itself (even a server error "
		+ "is one of the recognized JSON error shapes). This points to the request not reaching Creatio intact - "
		+ "a proxy/IIS/routing error, or a session redirect - rather than a problem with the request's OData/ESQ "
		+ "shape. Whether the change was actually applied is unverified; confirm with odata-read before retrying.";

	/// <summary>
	/// Builds the failure text for a write response body that failed to parse as JSON.
	/// </summary>
	internal static string DescribeNonJsonResponse(string body) =>
		$"Creatio did not return a JSON response. {NonJsonResponseHint} Response: {Truncate(body)}";

	/// <summary>
	/// Attempts to classify the IIS HTML 404 returned when an entity set has no OData controller.
	/// </summary>
	/// <param name="body">The raw response body returned by the OData request.</param>
	/// <param name="entityName">The requested OData entity set name.</param>
	/// <param name="message">The actionable failure message when the body is an IIS 404.</param>
	/// <returns><see langword="true"/> when the response is an IIS-style 404 page.</returns>
	internal static bool TryDescribeMissingEntitySet(string body, string entityName, out string message) {
		message = string.Empty;
		if (!LooksLikeIisNotFoundPage(body)) {
			return false;
		}

		message = $"OData entity set '{entityName}' could not be reached and may not be exposed over OData. "
			+ "Use execute-esq to read schemas that do not have an OData entity set. "
			+ "The server returned an IIS 404 page instead of an OData response.";
		return true;
	}

	/// <summary>
	/// Builds a concise failure for a read response that is not JSON, without echoing the raw body.
	/// </summary>
	/// <returns>An actionable non-JSON response message.</returns>
	/// <summary>
	/// The fixed, locally authored diagnostic for a read whose body IS JSON and reports an error.
	/// </summary>
	/// <remarks>
	/// The extracted detail is deliberately not part of it. <c>TryDetect</c> pulls
	/// <c>error.message</c>, <c>ExceptionMessage</c> or <c>MessageDetail</c> - all server-controlled -
	/// and <c>SensitiveErrorTextRedactor</c> removes known secret shapes only: it cannot remove forged
	/// instructions, opaque tokens, tenant data or embedded line breaks. This text lands in an MCP
	/// transcript that a model reads as trusted content, so no server prose is copied into it.
	/// </remarks>
	internal static string DescribeServerReportedReadError(bool includeUnregisteredEntityHint = false) {
		const string classification =
			"Creatio reported an error for this OData read. The server's own wording is not reproduced "
			+ "here, because a service or proxy response is not trusted text in an MCP transcript; check "
			+ "the environment's own logs for the server-side cause, then verify the entity name, the "
			+ "filter and the credentials.";
		//The hint is locally authored, so it is the one piece of detail that may be added.
		return includeUnregisteredEntityHint
			? $"{classification} {UnregisteredEntityHint}"
			: classification;
	}

	/// <summary>
	/// True when the body is a recognized Creatio error, reporting only whether it is the routing miss
	/// the locally authored <see cref="UnregisteredEntityHint"/> applies to.
	/// </summary>
	/// <remarks>
	/// The extracted text is deliberately not an output. Callers that put their result into an MCP
	/// transcript must use <see cref="DescribeServerReportedReadError"/> instead of
	/// <see cref="TryDetect"/>, whose message carries server-controlled prose.
	/// </remarks>
	internal static bool TryClassify(JsonElement root, out bool isUnregisteredEntity) {
		isUnregisteredEntity = false;
		if (!TryDetect(root, out string detected)) {
			return false;
		}
		isUnregisteredEntity = detected.Contains(UnregisteredEntityHint, StringComparison.Ordinal);
		return true;
	}

	internal static string DescribeNonJsonReadResponse() =>
		"Creatio did not return a JSON OData response. This points to an IIS, proxy, routing, or session "
		+ "problem rather than an OData query-shape problem; verify the environment and retry only after the "
		+ "endpoint is returning JSON.";

	private static bool LooksLikeIisNotFoundPage(string body) {
		string trimmedBody = body.TrimStart();
		return trimmedBody.StartsWith("<", StringComparison.Ordinal)
		&& body.Contains("404", StringComparison.OrdinalIgnoreCase)
		&& body.Contains("not found", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Truncates a raw response body to a safe preview length for error messages.</summary>
	internal static string Truncate(string value) {
		if (string.IsNullOrEmpty(value)) {
			return "<empty>";
		}
		return value.Length > 500 ? value[..500] + "..." : value;
	}

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
		return root.ValueKind == JsonValueKind.Object
			&& (TryDetectODataV4Error(root, out message)
				|| TryDetectAspNetException(root, out message)
				|| TryDetectRoutingError(root, out message));
	}

	// OData v4 error envelope: { "error": { "message": ... } }.
	private static bool TryDetectODataV4Error(JsonElement root, out string message) {
		message = string.Empty;
		if (!(root.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.Object)) {
			return false;
		}
		message = error.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.String
			? m.GetString()!
			: error.GetRawText();
		return true;
	}

	// ASP.NET Web API HttpError envelope (ExceptionType / ExceptionMessage never appear on real entities).
	private static bool TryDetectAspNetException(JsonElement root, out string message) {
		message = string.Empty;
		bool isAspNetError = root.TryGetProperty("ExceptionType", out _)
			|| root.TryGetProperty("ExceptionMessage", out _)
			|| root.TryGetProperty("StackTrace", out _);
		if (!isAspNetError) {
			return false;
		}
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
	private static bool TryDetectRoutingError(JsonElement root, out string message) {
		message = string.Empty;
		if (!(root.TryGetProperty("Message", out JsonElement bareMessage)
			&& bareMessage.ValueKind == JsonValueKind.String
			&& !HasNonRoutingErrorMembers(root))) {
			return false;
		}
		// Surface the most specific text: MessageDetail ("No type was found that matches the
		// controller named 'X'") when present, else the bare Message.
		string? detail = First(root, "MessageDetail");
		string primary = !string.IsNullOrEmpty(detail) ? detail : bareMessage.GetString() ?? string.Empty;
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

	/// <summary>
	/// True when the text is one of the ASP.NET Web API routing-miss messages that identify an
	/// unregistered/uncompiled OData controller, as opposed to any other error that happens to share
	/// the bare <c>{Message[,MessageDetail]}</c> shape.
	/// </summary>
	private static bool IsRoutingMiss(string? text) =>
		!string.IsNullOrEmpty(text)
		&& (text.Contains("No type was found that matches the controller", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("No HTTP resource was found that matches the request URI", StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// Returns true when the object carries any member other than the routing-error keys
	/// (<c>Message</c> / <c>MessageDetail</c>), which indicates a real OData payload (metadata,
	/// <c>value</c>, or entity columns) rather than a bare error body.
	/// </summary>
	private static bool HasNonRoutingErrorMembers(JsonElement root) =>
		root.EnumerateObject().Any(property =>
			!property.NameEquals("Message") && !property.NameEquals("MessageDetail"));

	private static string? First(JsonElement root, params string[] names) {
		foreach (string name in names) {
			if (root.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String) {
				return el.GetString();
			}
		}
		return null;
	}
}
