namespace Clio.Command;

using System;
using Newtonsoft.Json.Linq;

/// <summary>
/// Shared parsing for Creatio DataService <c>SelectQuery</c> responses.
/// </summary>
/// <remarks>
/// DataService returns HTTP 200 even for failures — e.g. restricted <c>SysSchema</c> / <c>SysPackage</c>
/// access (called out in AGENTS.md), an invalid column path, or an auth problem — carrying a failure
/// envelope (<c>success:false</c> / <c>errorInfo</c> / <c>responseStatus</c>) instead of <c>rows</c>.
/// Reading the missing <c>rows</c> token as an empty array would silently report that failure as a
/// successful, empty result, so callers would skip every schema/section with no error surfaced. This
/// helper detects the failure envelope and throws so the command reports the real error, and returns the
/// rows only for a genuine success (including a genuinely empty result set).
/// </remarks>
internal static class DataServiceSelectResponse {

	public static JArray ReadRows(string json) {
		JObject parsed = JObject.Parse(json);
		if (TryGetFailure(parsed, out string message)) {
			throw new InvalidOperationException($"SelectQuery failed: {message}");
		}

		return parsed["rows"] as JArray ?? [];
	}

	/// <summary>
	/// Classifies a parsed DataService <c>SelectQuery</c> envelope as a failure. This is the single
	/// authoritative failure-detection policy for the endpoint — <see cref="ReadRows"/> throws on it,
	/// while tuple-returning callers (e.g. the schema-layer enumerators) surface it as an error string —
	/// so every consumer keys failure off the same signals instead of the weaker <c>success</c>-only check:
	/// an explicit <c>success:false</c>, a non-empty <c>errorInfo</c> object, a <c>responseStatus</c> error
	/// code, or — as a backstop — a body carrying no <c>rows</c> token at all.
	/// </summary>
	/// <param name="parsed">The parsed SelectQuery response envelope.</param>
	/// <param name="message">
	/// When the return value is <see langword="true"/>, the human-readable failure reason (the
	/// <c>errorInfo</c>/<c>responseStatus</c> message, or a stable fallback); otherwise <see langword="null"/>.
	/// </param>
	/// <returns><see langword="true"/> when the envelope is a failure; otherwise <see langword="false"/>.</returns>
	/// <remarks>
	/// A JSON <c>"errorInfo": null</c> — a common success shape — parses in Newtonsoft to a JValue of type
	/// Null, which is NOT C# null, so a bare <c>parsed["errorInfo"] != null</c> test misfires on an otherwise
	/// successful envelope (and then throws an opaque JValue-indexing error reading <c>["message"]</c>).
	/// <c>as JObject</c> yields C# null for both the absent and the JSON-null case, so only an actual error
	/// object is a failure signal — and only a NON-EMPTY one, so a success envelope carrying <c>errorInfo:{}</c>
	/// is not misread as a failure. <c>success</c> is read via the nullable <c>Value&lt;bool?&gt;()</c> so a
	/// <c>"success": null</c> token does not throw.
	/// </remarks>
	public static bool TryGetFailure(JObject parsed, out string message) {
		JObject errorInfo = parsed["errorInfo"] as JObject;
		// Require the error object to be non-empty: a success envelope that carries an empty "errorInfo": {} must
		// not be misread as a failure (which would turn a genuine success into a hard error with no message).
		bool hasErrorInfo = errorInfo != null && errorInfo.HasValues;
		if (parsed["success"]?.Value<bool?>() == false
			|| hasErrorInfo
			|| !string.IsNullOrEmpty(parsed["responseStatus"]?["ErrorCode"]?.Value<string>())) {
			message = errorInfo?["message"]?.Value<string>()
				?? parsed["responseStatus"]?["Message"]?.Value<string>()
				?? "Creatio DataService returned a failure response with no rows";
			return true;
		}

		// No explicit failure signal, but also no rows token at all (as opposed to an empty array): a well-formed
		// success always carries "rows". A signal-less, rows-less body (e.g. a truncated/garbled 200, or an
		// atypical failure envelope) must NOT be reported as an empty success — for the migration tools that would
		// read as "nothing to migrate" and silently skip everything. Treat it as a failure instead. A JSON
		// "rows": null parses to a JValue-Null (NOT C# null), so test both the absent token and the null token.
		JToken rows = parsed["rows"];
		if (rows == null || rows.Type == JTokenType.Null) {
			message = "Creatio DataService returned a response with no rows and no explicit success signal";
			return true;
		}

		message = null;
		return false;
	}
}
