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
/// <summary>
/// Which endpoint produced the body being classified. The detectors are not interchangeable
/// between the two: a bare <c>{"Message":...}</c> is a routing miss on an OData controller but a
/// perfectly ordinary payload from a custom service, and Creatio's own <c>BaseResponse</c> envelope
/// never comes out of an OData endpoint, where a <c>Success</c> member is an entity column.
/// </summary>
internal enum CreatioResponseContext {

	/// <summary>
	/// Any Creatio service body: <c>call-service</c>, AuthService, the configuration services. Their
	/// payload shape is defined by whoever wrote the endpoint, so only wording-anchored and
	/// structurally unambiguous error shapes may claim one.
	/// </summary>
	Service,

	/// <summary>
	/// An OData v4 endpoint body. The payload shape is fixed by the protocol, which is what makes
	/// the bare-<c>Message</c> routing shape a reliable error signal here.
	/// </summary>
	ODataPayload

}

internal static class CreatioResponseError {

	/// <summary>
	/// The JSON property name carrying the human-readable error text across every envelope shape this
	/// class recognizes (the DataService/AuthService envelope, <c>BaseResponse.errorInfo</c>, the
	/// ASP.NET exception envelope, and the routing-error shape). Centralized so the six call sites stay
	/// in sync instead of drifting if the casing or the set of aliased names ever changes.
	/// </summary>
	private const string MessagePropertyName = "Message";

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
	/// <param name="context">Which endpoint produced the body; it selects the applicable detectors.</param>
	/// <param name="message">
	/// When the method returns <see langword="true"/>, receives the extracted error text (for a
	/// routing error the unregistered-entity hint is appended); otherwise an empty string.
	/// </param>
	/// <returns><see langword="true"/> when <paramref name="root"/> is a recognized error body.</returns>
	public static bool TryDetect(JsonElement root, CreatioResponseContext context, out string message) {
		message = string.Empty;
		if (root.ValueKind != JsonValueKind.Object) {
			return false;
		}
		//BaseResponse is a Creatio service envelope and never an OData body, where `success` is an
		//ordinary entity column: claiming it there reported a created record as failed AFTER the write
		//had happened, which invites a duplicate retry.
		bool isService = context == CreatioResponseContext.Service;
		//An @odata.context annotation on an OData body proves what the payload IS: the service emitted
		//an entity or a collection, not an error. The remaining two detectors decide by member name
		//alone, so an ordinary business column called ExceptionMessage, StackTrace, Message or
		//MessageDetail made a created record read as a server error - after the row existed, which
		//invites a duplicate retry. Explicit error envelopes are unaffected: the DataService and OData
		//v4 error shapes are checked before this and still win.
		bool hasProvenODataIdentity = !isService && HasODataContextAnnotation(root);
		return TryDetectDataServiceEnvelope(root, out message)
			|| (isService && TryDetectBaseResponse(root, out message))
			|| TryDetectODataV4Error(root, out message)
			|| (!hasProvenODataIdentity
				&& (TryDetectAspNetException(root, out message)
					|| TryDetectRoutingError(root, context, out message)));
	}

	/// <summary>
	/// True when the body is markup rather than JSON, ignoring anything that may legally precede the
	/// first tag. <c>TrimStart()</c> alone is not enough: a UTF-8 BOM survives it, and Creatio behind
	/// IIS answers with an XML declaration before the doctype
	/// (<c>&lt;?xml ...?&gt;&lt;!DOCTYPE ...&gt;&lt;title&gt;Request Error&lt;/title&gt;</c>). Such a
	/// body used to fall through the JSON branch and be saved as a successful result.
	/// </summary>
	public static bool IsMarkup(string body) {
		ReadOnlySpan<char> stripped = TrimMarkupPreamble(body);
		return stripped.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase)
			|| stripped.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
			|| stripped.StartsWith("<title", StringComparison.OrdinalIgnoreCase)
			|| stripped.StartsWith("<body", StringComparison.OrdinalIgnoreCase);
	}

	// A byte-order mark and a zero-width space, which a proxy can prepend to an error page.
	private static readonly char[] ZeroWidthPreambleChars = ['﻿', '​'];

	/// <summary>
	/// Removes a byte-order mark, leading whitespace and any XML declaration or processing
	/// instruction, so the first real tag is at the start of the returned span.
	/// </summary>
	public static string StripMarkupPreamble(string body) {
		ReadOnlySpan<char> stripped = TrimMarkupPreamble(body);
		return stripped.IsEmpty ? string.Empty : new string(stripped);
	}

	/// <summary>
	/// The same preamble skip as <see cref="StripMarkupPreamble"/>, over the original characters.
	/// </summary>
	/// <remarks>
	/// Slicing a span moves an offset; slicing a string copies the remainder. The string form copied the
	/// whole rest of the body once per processing instruction, so a 25,027-character answer carrying 5,000
	/// <c>&lt;?...?&gt;</c> prefixes allocated about 125 MB - and call-service normalizes the same body again
	/// for the markup checks, so a small crafted response cost quadratic time and allocation before it was
	/// even classified. Callers that only test the stripped text use this and allocate nothing.
	/// </remarks>
	internal static ReadOnlySpan<char> TrimMarkupPreamble(string body) {
		if (string.IsNullOrEmpty(body)) {
			return ReadOnlySpan<char>.Empty;
		}
		ReadOnlySpan<char> stripped = body.AsSpan();
		//Whitespace and zero-width characters interleave in any order, so trimming each once - or trimming
		//only whitespace after a processing instruction - leaves the other kind in front of the first tag.
		//" ﻿<!DOCTYPE html>" kept its BOM and "<?xml ?>﻿<html>" kept one after the declaration,
		//and in both cases IsMarkup returned false and an IIS error page was saved as a successful answer.
		stripped = TrimLeadingBlanks(stripped);
		while (stripped.StartsWith("<?", StringComparison.Ordinal)) {
			int end = stripped.IndexOf("?>", StringComparison.Ordinal);
			if (end < 0) {
				break;
			}
			stripped = TrimLeadingBlanks(stripped[(end + 2)..]);
		}
		return stripped;
	}

	/// <summary>
	/// Trims leading whitespace and zero-width characters until neither kind remains, whatever order they
	/// arrive in.
	/// </summary>
	private static ReadOnlySpan<char> TrimLeadingBlanks(ReadOnlySpan<char> value) {
		int length;
		do {
			length = value.Length;
			value = value.TrimStart().TrimStart(ZeroWidthPreambleChars);
		} while (value.Length != length);
		return value;
	}

	/// <summary>
	/// True when the markup is one of the Creatio/IIS failure pages. The wording is the platform's
	/// own - <c>&lt;title&gt;Request Error&lt;/title&gt;</c> with a <c>Service Unavailable</c> body -
	/// and carries no HTTP status line, so a status-code match alone misses it.
	/// </summary>
	public static bool IsKnownErrorPage(string body) {
		ReadOnlySpan<char> stripped = TrimMarkupPreamble(body);
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
		if (HasODataControlAnnotation(root)) {
			return false;
		}
		if (!(TryGetProperty(root, out JsonElement code, "Code", "code")
			&& code.ValueKind == JsonValueKind.Number
			&& code.TryGetInt32(out int codeValue)
			&& codeValue != 0)) {
			return false;
		}
		string detail = First(root, "Exception", "exception", MessagePropertyName, "message");
		if (string.IsNullOrWhiteSpace(detail)) {
			return false;
		}
		message = $"Creatio returned error code {codeValue}: {detail}";
		return true;
	}

	// Creatio's own BaseResponse envelope: { "success": false, "errorInfo": { "message": ... } }.
	// Configuration and application services answer HTTP 200 with it, and the detail-less
	// { "success": false } is just as common. Without this branch call-service beautified such a
	// failure, wrote it to --destination and exited 0 - the same false success this contract removes
	// for the other envelopes. Only an explicit boolean false counts, so a payload carrying
	// success=true, or a `success` string column, is left alone.
	private static bool TryDetectBaseResponse(JsonElement root, out string message) {
		message = string.Empty;
		//No OData-payload guard here, unlike the loose Code/Message detector: ValueResponse<T> and the
		//insert-derived BaseResponse DTOs keep `value` or `id` on a failure, so guarding on those
		//members let {"success":false,"errorInfo":{...},"value":null} exit 0 and be saved. An explicit
		//boolean success:false is unambiguous and wins over any incidental payload member.
		bool hasSuccessFlag = TryGetProperty(root, out JsonElement success, "success", "Success");
		bool explicitFailure = hasSuccessFlag && success.ValueKind == JsonValueKind.False;
		bool explicitSuccess = hasSuccessFlag && success.ValueKind == JsonValueKind.True;

		string? detail = null;
		bool hasPopulatedErrorInfo = false;
		if (TryGetProperty(root, out JsonElement errorInfo, "errorInfo", "ErrorInfo")
			&& errorInfo.ValueKind == JsonValueKind.Object) {
			detail = First(errorInfo, "message", MessagePropertyName, "errorMessage", "ErrorMessage");
			string? errorCode = First(errorInfo, "errorCode", "ErrorCode", "code", "Code");
			hasPopulatedErrorInfo = !string.IsNullOrWhiteSpace(detail) || !string.IsNullOrWhiteSpace(errorCode);
			if (string.IsNullOrWhiteSpace(detail) && !string.IsNullOrWhiteSpace(errorCode)) {
				detail = errorCode;
			}
		}

		//A populated errorInfo is a failure on its own: a real permission rejection arrives as
		//{"errorInfo":{"errorCode":"AccessDenied","message":"..."}} with no success member at all, and
		//ignoring it saved that page and exited 0. A null or empty errorInfo, and an explicit
		//success:true, are both left alone.
		if (!explicitFailure && !(hasPopulatedErrorInfo && !explicitSuccess)) {
			return false;
		}
		detail ??= First(root, "errorMessage", "ErrorMessage", "message", MessagePropertyName);
		message = string.IsNullOrWhiteSpace(detail)
			? "Creatio reported the request as failed without an error message."
			: $"Creatio reported the request as failed: {detail}";
		return true;
	}

	/// <summary>
	/// The OData control annotations - the only members a body cannot carry unless an OData endpoint
	/// produced it. They are what stops the structurally loose DataService/AuthService envelope from
	/// claiming a real payload: a successful create answered as
	/// <c>{"@odata.context":"...","Id":"&lt;guid&gt;","Code":200,"Message":"Created"}</c> was
	/// otherwise reported as <c>Success=false</c> by <c>ODataCreateTool.ParseCreated</c> after the
	/// write had already happened, which invites a duplicate retry.
	///
	/// <c>Id</c>, <c>id</c> and <c>value</c> are deliberately NOT here. Any envelope may carry them,
	/// so treating them as proof of a payload let an explicit error such as
	/// <c>{"Code":-1,"Exception":"...","Id":"&lt;guid&gt;"}</c> be saved and exit 0 - an explicit
	/// non-zero <c>Code</c> with an <c>Exception</c> is the stronger signal and now wins. The
	/// narrow-shape detectors (OData v4 <c>error</c>, the ASP.NET exception envelope, the routing
	/// error) need no guard: their members never appear on an entity.
	/// </summary>
	private static readonly string[] ODataControlAnnotations = [
		"@odata.context", "@odata.id", "@odata.etag"
	];

	private static bool HasODataControlAnnotation(JsonElement root) =>
		root.EnumerateObject().Any(property => ODataControlAnnotations.Any(property.NameEquals));

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
			? m.GetString()
			: error.GetRawText();
		return true;
	}

	// ASP.NET Web API HttpError envelope (ExceptionType / ExceptionMessage never appear on real entities).
	/// <summary>
	/// True when the body carries a non-empty <c>@odata.context</c> control annotation, which an OData
	/// service emits only on a genuine entity or collection payload - never on an error body.
	/// </summary>
	private static bool HasODataContextAnnotation(JsonElement root) =>
		root.TryGetProperty("@odata.context", out JsonElement context)
		&& context.ValueKind == JsonValueKind.String
		&& !string.IsNullOrWhiteSpace(context.GetString());

	private static bool TryDetectAspNetException(JsonElement root, out string message) {
		message = string.Empty;
		bool isAspNetError = root.TryGetProperty("ExceptionType", out _)
			|| root.TryGetProperty("ExceptionMessage", out _)
			|| root.TryGetProperty("StackTrace", out _);
		if (!isAspNetError) {
			return false;
		}
		// NOTE: deliberately no "does the body carry other members?" guard here. ASP.NET Web API's
		// HttpError populates InnerException whenever error detail is enabled, so such a guard would
		// classify a genuine unhandled server exception as NOT an error - and every caller
		// (ODataKeyedWrite.ValidateWriteResponse, ODataReadTool, ODataCreateTool,
		// Branding/SetBackgroundImageCommand) would then report success on it. The false positive it
		// would have prevented - a probed record whose own columns are named ExceptionMessage /
		// ExceptionType / StackTrace - is already handled upstream by
		// ODataFieldValidation.IsSelectedRecord, which returns before TryDetect is ever reached.
		message = First(root, "ExceptionMessage", MessagePropertyName) ?? "Creatio returned a server error.";
		return true;
	}

	// ASP.NET Web API routing error: { "Message": ..., "MessageDetail": ... } with no exception
	// members. This is a 404 "No HTTP resource / no controller found" for an unregistered entity
	// set. Detection is deliberately locked to that shape and must never pre-empt a real payload:
	// a genuine OData response always carries another member — an @odata.context annotation
	// (present under the default OData metadata level this tool relies on), a value collection
	// wrapper, or the created record's Id — so a body whose only members are Message (+
	// MessageDetail) is an error, not data. NOTE: this safety rests on default OData metadata. A
	// by-key read path now exists (the odata-update pre-write field probe: $select=Id,<fields> on
	// one record); under default metadata that keyed read always carries @odata.context, a
	// non-routing member, so this guard holds. A keyed read served with odata.metadata=none that
	// selected only a Message-named column would still lose its distinguishing member and be
	// misclassified — no current call site does that (the probe relies on default metadata); if a
	// metadata=none by-key read is ever added, revisit this branch. The ASP.NET-exception branch
	// deliberately carries NO such guard - see the note in TryDetectAspNetException.
	private static bool TryDetectRoutingError(JsonElement root, CreatioResponseContext context,
		out string message) {
		message = string.Empty;
		if (!(root.TryGetProperty(MessagePropertyName, out JsonElement bareMessage)
			&& bareMessage.ValueKind == JsonValueKind.String
			&& !HasNonRoutingErrorMembers(root))) {
			return false;
		}
		//The bare {Message[,MessageDetail]} shape is only an error signal where the protocol fixes the
		//payload shape. A custom endpoint reached through call-service owns its own contract and may
		//legitimately answer {"Message":"OK"}; claiming that as a failure exited 1 and refused to save a
		//valid response. Outside OData only the routing-miss WORDING counts.
		if (context != CreatioResponseContext.ODataPayload
			&& !IsRoutingMiss(First(root, "MessageDetail"))
			&& !IsRoutingMiss(bareMessage.GetString())) {
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
			!property.NameEquals(MessagePropertyName) && !property.NameEquals("MessageDetail"));

	private static string? First(JsonElement root, params string[] names) {
		foreach (string name in names) {
			if (root.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String) {
				return el.GetString();
			}
		}
		return null;
	}
}
