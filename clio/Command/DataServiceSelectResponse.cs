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
	/// so every consumer keys failure off the same three signals instead of the weaker <c>success</c>-only
	/// check.
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
	/// object is a failure signal. <c>success</c> is read via the nullable <c>Value&lt;bool?&gt;()</c> so a
	/// <c>"success": null</c> token does not throw.
	/// </remarks>
	public static bool TryGetFailure(JObject parsed, out string message) {
		JObject errorInfo = parsed["errorInfo"] as JObject;
		if (parsed["success"]?.Value<bool?>() == false
			|| errorInfo != null
			|| !string.IsNullOrEmpty(parsed["responseStatus"]?["ErrorCode"]?.Value<string>())) {
			message = errorInfo?["message"]?.Value<string>()
				?? parsed["responseStatus"]?["Message"]?.Value<string>()
				?? "Creatio DataService returned a failure response with no rows";
			return true;
		}

		message = null;
		return false;
	}
}
