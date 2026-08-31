using System;
using System.Linq;
using System.Text.Json;

namespace Clio.Common;

/// <summary>
/// Detects Creatio error payloads returned with a non-failing HTTP status by the underlying
/// transport. Four JSON shapes are recognized: the DataService envelope
/// (<c>{"Code":-1,"Exception":...}</c>), OData v4 errors (<c>{"error":{"message":...}}</c>),
/// ASP.NET Web API exception errors (<c>{"Message":...,"ExceptionType":...,"StackTrace":...}</c>,
/// e.g. the EDM model-build NullReferenceException), and ASP.NET Web API routing errors
/// (<c>{"Message":...,"MessageDetail":...}</c>, e.g. a 404 for an unregistered/uncompiled OData
/// controller). Real OData entities and collections never carry these members, so detecting them
/// lets the odata-* tools report success=false instead of wrapping an error body as data.
///
/// The class lives in <c>Clio.Common</c> rather than next to the MCP tools because the same
/// envelopes reach the <c>call-service</c> command: every caller that has to tell a Creatio error
/// from a payload has to agree on the shapes, and a second copy would drift.
/// </summary>
internal static class CreatioResponseError {
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
			&& (TryDetectDataServiceEnvelope(root, out message)
				|| TryDetectODataV4Error(root, out message)
				|| TryDetectAspNetException(root, out message)
				|| TryDetectRoutingError(root, out message));
	}

	/// <summary>
	/// True when the body is markup rather than JSON, ignoring anything that may legally precede the
	/// first tag. <c>TrimStart()</c> alone is not enough: a UTF-8 BOM survives it, and Creatio behind
	/// IIS answers with an XML declaration before the doctype
	/// (<c>&lt;?xml ...?&gt;&lt;!DOCTYPE ...&gt;&lt;title&gt;Request Error&lt;/title&gt;</c>). Such a
	/// body used to fall through the JSON branch and be saved as a successful result.
	/// </summary>
	public static bool IsMarkup(string body) {
		string stripped = StripMarkupPreamble(body);
		return stripped.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase)
			|| stripped.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
			|| stripped.StartsWith("<title", StringComparison.OrdinalIgnoreCase)
			|| stripped.StartsWith("<body", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Removes a byte-order mark, leading whitespace and any XML declaration or processing
	/// instruction, so the first real tag is at the start of the returned span.
	/// </summary>
	public static string StripMarkupPreamble(string body) {
		if (string.IsNullOrEmpty(body)) {
			return string.Empty;
		}
		string stripped = body.TrimStart('﻿', '​').TrimStart();
		while (stripped.StartsWith("<?", StringComparison.Ordinal)) {
			int end = stripped.IndexOf("?>", StringComparison.Ordinal);
			if (end < 0) {
				break;
			}
			stripped = stripped[(end + 2)..].TrimStart();
		}
		return stripped;
	}

	/// <summary>
	/// True when the markup is one of the Creatio/IIS failure pages. The wording is the platform's
	/// own - <c>&lt;title&gt;Request Error&lt;/title&gt;</c> with a <c>Service Unavailable</c> body -
	/// and carries no HTTP status line, so a status-code match alone misses it.
	/// </summary>
	public static bool IsKnownErrorPage(string body) {
		string stripped = StripMarkupPreamble(body);
		return stripped.Contains("Request Error", StringComparison.OrdinalIgnoreCase)
			|| stripped.Contains("Service Unavailable", StringComparison.OrdinalIgnoreCase);
	}

	// DataService and AuthService envelope: { "Code": <non-zero>, "Exception"|"Message": "<text>" }.
	// Creatio answers HTTP 200 with this body for a failed DataService call, and AuthService.svc/Login
	// rejects a login as {"Code":1,...} the same way - which is why it needs detecting at all.
	// Requiring BOTH a non-zero Code and a non-empty Exception/Message keeps a payload that merely
	// happens to carry a `Code` column from being read as a failure.
	private static bool TryDetectDataServiceEnvelope(JsonElement root, out string message) {
		message = string.Empty;
		if (!(TryGetProperty(root, out JsonElement code, "Code", "code")
			&& code.ValueKind == JsonValueKind.Number
			&& code.TryGetInt32(out int codeValue)
			&& codeValue != 0)) {
			return false;
		}
		string detail = First(root, "Exception", "exception", "Message", "message");
		if (string.IsNullOrWhiteSpace(detail)) {
			return false;
		}
		message = $"Creatio returned error code {codeValue}: {detail}";
		return true;
	}

	private static bool TryGetProperty(JsonElement root, out JsonElement value, params string[] names) {
		foreach (string name in names) {
			if (root.TryGetProperty(name, out value)) {
				return true;
			}
		}
		value = default;
		return false;
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
