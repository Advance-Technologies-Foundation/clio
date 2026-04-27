namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

/// <summary>
/// Merges incoming schema body fragments into an existing schema body so <c>update-page</c>
/// can run in incremental "append" mode. Enables single-shot AI page modifications without
/// forcing the caller to resend the full existing body (which tends to fail with the
/// "Object vs Array" backend error when existing merges are re-applied).
/// </summary>
internal static class PageBodyMerger {

	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

	/// <summary>
	/// Returns a merged body string that combines <paramref name="currentBody"/> (the schema's
	/// existing body on the server) with <paramref name="incomingBody"/> (the new fragment the
	/// caller wants to add). The returned string has the same marker envelope as the current
	/// body. Throws <see cref="InvalidOperationException"/> if either body is missing required
	/// markers that the merger needs.
	/// </summary>
	public static string Merge(string currentBody, string incomingBody) {
		if (string.IsNullOrWhiteSpace(currentBody)) {
			throw new InvalidOperationException("Current body is empty — cannot perform append merge.");
		}
		if (string.IsNullOrWhiteSpace(incomingBody)) {
			throw new InvalidOperationException("Incoming body is empty — pass the new viewConfigDiff/handlers fragment.");
		}

		JArray mergedViewConfigDiff = MergeArrayByName(
			ReadJsonArray(currentBody, "SCHEMA_VIEW_CONFIG_DIFF"),
			ReadJsonArray(incomingBody, "SCHEMA_VIEW_CONFIG_DIFF"));
		JArray mergedViewModelConfigDiff = MergeArrayAppend(
			ReadJsonArray(currentBody, "SCHEMA_VIEW_MODEL_CONFIG_DIFF"),
			ReadJsonArray(incomingBody, "SCHEMA_VIEW_MODEL_CONFIG_DIFF"));
		JArray mergedModelConfigDiff = MergeArrayAppend(
			ReadJsonArray(currentBody, "SCHEMA_MODEL_CONFIG_DIFF"),
			ReadJsonArray(incomingBody, "SCHEMA_MODEL_CONFIG_DIFF"));
		string mergedHandlers = MergeHandlersRaw(
			ReadRawSection(currentBody, "SCHEMA_HANDLERS") ?? "[]",
			ReadRawSection(incomingBody, "SCHEMA_HANDLERS") ?? "[]");

		string result = currentBody;
		result = ReplaceSection(result, "SCHEMA_VIEW_CONFIG_DIFF", mergedViewConfigDiff.ToString(Newtonsoft.Json.Formatting.None));
		result = ReplaceSection(result, "SCHEMA_VIEW_MODEL_CONFIG_DIFF", mergedViewModelConfigDiff.ToString(Newtonsoft.Json.Formatting.None));
		result = ReplaceSection(result, "SCHEMA_MODEL_CONFIG_DIFF", mergedModelConfigDiff.ToString(Newtonsoft.Json.Formatting.None));
		result = ReplaceSection(result, "SCHEMA_HANDLERS", mergedHandlers);
		return result;
	}

	private static JArray MergeArrayByName(JArray current, JArray incoming) {
		var byName = new Dictionary<string, JToken>(StringComparer.Ordinal);
		var order = new List<string>();
		var unnamed = new List<JToken>();
		foreach (JToken item in current.Concat(incoming)) {
			string name = (item as JObject)?["name"]?.ToString();
			if (string.IsNullOrEmpty(name)) {
				unnamed.Add(item);
				continue;
			}
			if (!byName.ContainsKey(name)) {
				order.Add(name);
			}
			byName[name] = item;
		}
		var merged = new JArray();
		foreach (string name in order) {
			merged.Add(byName[name]);
		}
		foreach (JToken item in unnamed) {
			merged.Add(item);
		}
		return merged;
	}

	private static JArray MergeArrayAppend(JArray current, JArray incoming) {
		var merged = new JArray();
		foreach (JToken item in current) {
			merged.Add(item);
		}
		foreach (JToken item in incoming) {
			merged.Add(item);
		}
		return merged;
	}

	private static string MergeHandlersRaw(string current, string incoming) {
		string currentTrim = current.Trim();
		string incomingTrim = incoming.Trim();
		if (currentTrim == "[]" || string.IsNullOrEmpty(currentTrim)) {
			return incomingTrim;
		}
		if (incomingTrim == "[]" || string.IsNullOrEmpty(incomingTrim)) {
			return currentTrim;
		}
		string currentInner = StripArrayBrackets(currentTrim);
		string incomingInner = StripArrayBrackets(incomingTrim);
		HashSet<string> incomingRequests = ExtractHandlerRequestStrings(incomingInner);
		string filteredCurrent = RemoveHandlersWithRequests(currentInner, incomingRequests);
		string joined;
		if (string.IsNullOrWhiteSpace(filteredCurrent)) {
			joined = incomingInner;
		} else if (string.IsNullOrWhiteSpace(incomingInner)) {
			joined = filteredCurrent;
		} else {
			joined = filteredCurrent.TrimEnd(',', ' ', '\t', '\n', '\r') + "," + incomingInner;
		}
		return "[" + joined + "]";
	}

	private static string StripArrayBrackets(string value) {
		string trimmed = value.Trim();
		if (trimmed.StartsWith('[')) trimmed = trimmed.Substring(1);
		if (trimmed.EndsWith(']')) trimmed = trimmed.Substring(0, trimmed.Length - 1);
		return trimmed.Trim();
	}

	private static HashSet<string> ExtractHandlerRequestStrings(string handlersInner) {
		var result = new HashSet<string>(StringComparer.Ordinal);
		Regex regex = new(@"request\s*:\s*[""']([^""']+)[""']", RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);
		foreach (Match match in regex.Matches(handlersInner)) {
			result.Add(match.Groups[1].Value);
		}
		return result;
	}

	private static string RemoveHandlersWithRequests(string handlersInner, HashSet<string> existingRequests) {
		if (existingRequests.Count == 0) {
			return handlersInner;
		}
		var blocks = SplitTopLevelObjects(handlersInner);
		var kept = new List<string>();
		Regex requestRegex = new(@"request\s*:\s*[""']([^""']+)[""']", RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);
		foreach (string block in blocks) {
			Match match = requestRegex.Match(block);
			if (match.Success && existingRequests.Contains(match.Groups[1].Value)) {
				continue;
			}
			kept.Add(block);
		}
		return string.Join(",", kept);
	}

	private static List<string> SplitTopLevelObjects(string value) {
		var result = new List<string>();
		int depth = 0;
		int start = -1;
		for (int i = 0; i < value.Length; i++) {
			char ch = value[i];
			if (ch == '{') {
				if (depth == 0) start = i;
				depth++;
			} else if (ch == '}') {
				depth--;
				if (depth == 0 && start >= 0) {
					result.Add(value.Substring(start, i - start + 1));
					start = -1;
				}
			}
		}
		return result;
	}

	private static JArray ReadJsonArray(string body, string marker) {
		if (!PageSchemaSectionReader.TryRead(body, out string content, marker)) {
			return new JArray();
		}
		string trimmed = content.Trim();
		if (string.IsNullOrEmpty(trimmed) || trimmed == "[]") {
			return new JArray();
		}
		try {
			return JArray.Parse(trimmed);
		} catch (Exception ex) {
			throw new InvalidOperationException(
				$"Section '{marker}' is not valid JSON array: {ex.Message}", ex);
		}
	}

	private static string ReadRawSection(string body, string marker) {
		return PageSchemaSectionReader.TryRead(body, out string content, marker) ? content.Trim() : null;
	}

	private static string ReplaceSection(string body, string marker, string newContent) {
		string pattern = $@"/\*\*{Regex.Escape(marker)}\*/([\s\S]*?)/\*\*{Regex.Escape(marker)}\*/";
		Regex regex = new(pattern, RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexTimeout);
		string replacement = $"/**{marker}*/{newContent}/**{marker}*/";
		return regex.Replace(body, _ => replacement, 1);
	}
}
