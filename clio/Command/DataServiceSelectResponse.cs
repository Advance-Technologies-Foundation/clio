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
		// A JSON `"errorInfo": null` — a common success shape — parses in Newtonsoft to a JValue of type Null,
		// which is NOT C# null, so a bare `parsed["errorInfo"] != null` test misfires on an otherwise successful
		// envelope and takes the failure branch (and then throws an opaque JValue-indexing error reading
		// `["message"]`). Only an actual error object is a failure signal; `as JObject` yields C# null for both
		// the absent and the JSON-null case, so the failure gate keys on `success == false` / a real errorInfo
		// object / a responseStatus error — matching the `success == false` convention every other consumer uses.
		JObject errorInfo = parsed["errorInfo"] as JObject;
		if (parsed["success"]?.Value<bool?>() == false
			|| errorInfo != null
			|| !string.IsNullOrEmpty(parsed["responseStatus"]?["ErrorCode"]?.Value<string>())) {
			string message = errorInfo?["message"]?.Value<string>()
				?? parsed["responseStatus"]?["Message"]?.Value<string>()
				?? "Creatio DataService returned a failure response with no rows";
			throw new InvalidOperationException($"SelectQuery failed: {message}");
		}

		return parsed["rows"] as JArray ?? [];
	}
}
