using System;
using System.Text.Json;
using Clio.Command.McpServer;
using Clio.Common;
using Newtonsoft.Json.Linq;

namespace Clio.Package;

/// <summary>
/// Raised when a Creatio service answered with a body that is not usable JSON — an HTML error/login page,
/// an empty body, or truncated/garbage text.
/// <para>
/// Derives from <see cref="InvalidOperationException"/> so callers that already treat a failed service call
/// as such keep working unchanged. Callers that must tell "the server rejected the request" apart from
/// "the response was not JSON at all" catch this type specifically: the second case carries no statement
/// about the requested data, so a soft-degrading caller must not turn it into a claim about the record
/// (for example not-found or no-access).
/// </para>
/// <para>
/// Implements <see cref="IAuthoritativeErrorMessage"/> so the MCP boundary surfaces this classified message
/// instead of unwrapping to the inner parser exception, whose text is the very thing this guard replaces.
/// </para>
/// </summary>
internal sealed class NonJsonServiceResponseException : InvalidOperationException, IAuthoritativeErrorMessage
{
	/// <summary>Initializes the exception with a message and the underlying parser failure, when any.</summary>
	/// <param name="message">Human-readable description of the non-JSON response.</param>
	/// <param name="innerException">The parser failure, or <see langword="null"/> for an empty body.</param>
	internal NonJsonServiceResponseException(string message, Exception? innerException = null)
		: base(message, innerException)
	{
	}
}

/// <summary>
/// Deserializes a Creatio service response body and converts a non-JSON body (an HTML error/login
/// page, an empty body, or truncated/garbage text) into a typed <see cref="InvalidOperationException"/>
/// carrying the endpoint URL, the parser detail, and a bounded response preview.
/// <para>
/// Without this guard the raw <see cref="JsonException"/> text reaches the caller — for an MCP agent
/// that is <c>"'&lt;' is an invalid start of a value. LineNumber: 0 | BytePositionInLine: 0."</c>, which
/// names neither the endpoint nor the actual body and gives the agent nothing to act on (ENG-93365).
/// </para>
/// <para>
/// Classification happens AFTER a failed parse rather than by sniffing the first byte, because a
/// truncated body that starts with <c>{</c> is just as unparseable as an HTML page and must produce the
/// same typed error. Mirrors <c>SchemaTemplateCatalog.BuildNonJsonErrorMessage</c>, the shape the
/// <c>list-page-templates</c> path already surfaces.
/// </para>
/// </summary>
internal static class ServiceResponseJsonGuard
{
	/// <summary>Upper bound on the response preview embedded in the error message.</summary>
	private const int ResponsePreviewMaxLength = 200;

	/// <summary>UTF-8 byte-order mark, which <see cref="char.IsWhiteSpace(char)"/> does not report as whitespace.</summary>
	private const char ByteOrderMark = '\uFEFF';

	/// <summary>
	/// Deserializes <paramref name="responseBody"/> into <typeparamref name="T"/>, or throws a typed
	/// <see cref="InvalidOperationException"/> describing the non-JSON body.
	/// </summary>
	/// <typeparam name="T">Response contract to deserialize into.</typeparam>
	/// <param name="operationName">Human-readable operation label used to open the error message (for example <c>SelectQuery</c>).</param>
	/// <param name="url">Endpoint the body came from, included so the caller can tell which request failed.</param>
	/// <param name="responseBody">Raw response body as returned by the application client.</param>
	/// <param name="jsonOptions">Serializer options to deserialize with.</param>
	/// <returns>The deserialized response.</returns>
	/// <exception cref="NonJsonServiceResponseException">
	/// The body is empty, is an HTML page, deserializes to <see langword="null"/>, or is not parseable JSON.
	/// </exception>
	internal static T Deserialize<T>(
		string operationName,
		string url,
		string? responseBody,
		JsonSerializerOptions jsonOptions)
	{
		if (string.IsNullOrWhiteSpace(responseBody))
		{
			throw new NonJsonServiceResponseException(BuildEmptyBodyMessage(operationName, url));
		}

		T? response;
		try
		{
			response = JsonSerializer.Deserialize<T>(responseBody, jsonOptions);
		}
		catch (JsonException parseException)
		{
			throw new NonJsonServiceResponseException(
				BuildNonJsonMessage(operationName, url, responseBody, parseException),
				parseException);
		}

		return response ?? throw new NonJsonServiceResponseException(BuildEmptyBodyMessage(operationName, url));
	}

	/// <summary>
	/// Parses <paramref name="responseBody"/> into a Newtonsoft <see cref="JObject"/>, or reports a
	/// classified failure instead of letting the raw parser text reach the caller.
	/// <para>
	/// The Newtonsoft counterpart of <see cref="Deserialize{T}"/>, added because the schema-designer
	/// helpers work on dynamic <c>JObject</c> payloads and were calling <c>JObject.Parse</c> directly.
	/// An empty body there surfaced Newtonsoft's own
	/// <c>"Error reading JObject from JsonReader. Path '', line 0, position 0."</c>, which names neither
	/// the service nor the route (issue #1322).
	/// </para>
	/// <para>
	/// It reports through an out-parameter rather than throwing, because those helpers already return
	/// <c>(result, error)</c> tuples and a thrown exception would be flattened into
	/// <c>Exception.Message</c> by the command's catch-all, losing the distinction between "parse
	/// failed" and any other failure.
	/// </para>
	/// </summary>
	/// <param name="operationName">Human-readable operation label used to open the error message.</param>
	/// <param name="url">Endpoint the body came from.</param>
	/// <param name="responseBody">Raw response body as returned by the application client.</param>
	/// <param name="hint">Optional caller-authored tail appended to the message; locally authored text only.</param>
	/// <param name="parsed">The parsed object, or <see langword="null"/> when parsing failed.</param>
	/// <param name="error">The classified failure message, or <see langword="null"/> on success.</param>
	/// <returns><see langword="true"/> when the body parsed into a JSON object.</returns>
	internal static bool TryParseJObject(
		string operationName,
		string url,
		string? responseBody,
		string? hint,
		out JObject? parsed,
		out string? error)
	{
		parsed = null;
		if (string.IsNullOrWhiteSpace(responseBody))
		{
			error = Append(BuildEmptyBodyMessage(operationName, url), hint);
			return false;
		}

		try
		{
			parsed = JObject.Parse(responseBody);
		}
		catch (Newtonsoft.Json.JsonException parseException)
		{
			// Newtonsoft, not System.Text.Json: JObject.Parse throws JsonReaderException, which does not
			// derive from System.Text.Json.JsonException, so catching the latter would let it escape.
			error = Append(BuildNonJsonMessage(operationName, url, responseBody, parseException), hint);
			return false;
		}

		error = null;
		return true;
	}

	/// <summary>Appends a locally authored hint to a message when one was supplied.</summary>
	private static string Append(string message, string? hint) =>
		string.IsNullOrWhiteSpace(hint) ? message : $"{message} {hint}";

	/// <summary>
	/// Builds the message for a body that could not be parsed as JSON, choosing between the HTML case
	/// (hints, no body preview) and the generic unparseable case (parser detail plus a bounded preview).
	/// </summary>
	/// <param name="operationName">Human-readable operation label.</param>
	/// <param name="url">Endpoint the body came from.</param>
	/// <param name="responseBody">Raw, non-empty response body.</param>
	/// <param name="parseException">The parser failure being reported.</param>
	/// <returns>The error message to surface.</returns>
	internal static string BuildNonJsonMessage(
		string operationName,
		string url,
		string responseBody,
		Exception parseException)
	{
		if (LooksLikeMarkup(responseBody))
		{
			// The body is deliberately NOT previewed here: a login page or an ASP.NET error page can carry
			// session cookies, request tokens, and stack traces, and this text is copied verbatim into an
			// agent transcript.
			return $"{operationName} returned an HTML page instead of JSON (URL: {url}). "
				+ "The request was most likely redirected to a login page, or the server raised an unhandled "
				+ "error and answered with an HTML error page. Verify that: "
				+ "1) the environment is registered with valid credentials (reg-web-app, then healthcheck); "
				+ "2) the IsNetCore flag matches the target instance (omit for .NET Framework, add --IsNetCore for .NET Core); "
				+ "3) the request is retried — a transient server-side failure on this endpoint answers with an HTML error page. "
				+ "The HTML body is omitted from this message because an error or login page can carry session tokens.";
		}

		// The parser message is redacted too, not just the preview: System.Text.Json quotes the offending
		// VALUE in its text (for example "The JSON value 'hunter2' could not be converted … Path: $.password"),
		// so an unredacted parser message leaks the same secrets the preview redaction exists to catch. The MCP
		// boundary redacts again, but the CLI path (logger warnings, terminal output) has no second pass.
		return $"{operationName} returned an unparseable response (URL: {url}). "
			+ $"Parser error: {SensitiveErrorTextRedactor.Redact(parseException.Message)}. "
			+ $"Response preview: {BuildResponsePreview(responseBody)}";
	}

	/// <summary>
	/// Builds the message for a body that is missing or whitespace-only, or that deserializes to
	/// <see langword="null"/>.
	/// </summary>
	/// <param name="operationName">Human-readable operation label.</param>
	/// <param name="url">Endpoint the body came from.</param>
	/// <returns>The error message to surface.</returns>
	internal static string BuildEmptyBodyMessage(string operationName, string url) =>
		$"{operationName} returned an empty response (URL: {url}). "
		+ "The response body was empty, and clio's synchronous client does not expose the HTTP status, so "
		+ "whether the request was accepted is unknown — an unrouted endpoint answers 404 with an empty body "
		+ "just as a served endpoint can answer 200 with one. Retry the request, and if it persists check the "
		+ "route, the environment health (healthcheck) and the Creatio server log for that endpoint.";

	/// <summary>
	/// Returns whether the body starts with markup (an HTML page, an XML/SOAP fault, or a doctype),
	/// skipping any leading whitespace and byte-order marks in any order so neither hides it. A single
	/// chained trim would not do: a BOM followed by whitespace (<c>BOM + "  &lt;html&gt;"</c>) leaves the
	/// post-BOM whitespace behind, misclassifying an HTML login page as a generic unparseable body and
	/// previewing it.
	/// </summary>
	/// <param name="responseBody">Raw response body.</param>
	/// <returns><see langword="true"/> when the body opens with markup.</returns>
	private static bool LooksLikeMarkup(string responseBody)
	{
		int index = 0;
		while (index < responseBody.Length
			&& (char.IsWhiteSpace(responseBody[index]) || responseBody[index] == ByteOrderMark))
		{
			index++;
		}

		return index < responseBody.Length && responseBody[index] == '<';
	}

	/// <summary>
	/// Produces a bounded, redacted single-line preview of the body. Redaction runs here rather than at
	/// the MCP boundary so the CLI path — which logs the exception message with no redactor — is covered
	/// too, and so a token inside a garbage body never reaches a transcript.
	/// <para>
	/// Redaction runs BEFORE the length cap: the redactor matches a bare JWT only as a complete
	/// three-segment token, so truncating first would cut a token that straddles the cap into a prefix the
	/// patterns no longer recognise, and that prefix would be surfaced.
	/// </para>
	/// </summary>
	/// <param name="responseBody">Raw, non-empty response body.</param>
	/// <returns>The preview to embed in the error message.</returns>
	private static string BuildResponsePreview(string responseBody)
	{
		// Byte-order marks are trimmed alongside whitespace: char.IsWhiteSpace does not report a BOM, so a
		// BOM-only body survives the IsNullOrWhiteSpace gate in Deserialize and would otherwise produce a
		// preview made entirely of invisible characters.
		string collapsed = responseBody
			.Replace("\r", " ", StringComparison.Ordinal)
			.Replace("\n", " ", StringComparison.Ordinal)
			.Trim()
			.Trim(ByteOrderMark)
			.Trim();
		if (collapsed.Length == 0)
		{
			return "<empty body>";
		}

		string redacted = SensitiveErrorTextRedactor.Redact(collapsed);
		return redacted.Length > ResponsePreviewMaxLength
			? redacted[..ResponsePreviewMaxLength] + "…"
			: redacted;
	}
}
