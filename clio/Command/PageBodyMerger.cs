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
	/// <remarks>
	/// Each section is merged only when its marker pair already exists in <paramref name="currentBody"/>.
	/// If <paramref name="currentBody"/> pre-dates a section (e.g. an older page schema without a
	/// <c>SCHEMA_CONVERTERS</c> or <c>SCHEMA_VALIDATORS</c> block), the computed merge result for that
	/// section is silently discarded and the body is returned unchanged for that section. To add a new
	/// section to an older page, first manually insert the empty marker pair into the body, then call
	/// <c>Merge</c> with the desired content.
	/// </remarks>
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
		string mergedConverters = MergeConvertersRaw(
			ReadRawSection(currentBody, "SCHEMA_CONVERTERS") ?? "{}",
			ReadRawSection(incomingBody, "SCHEMA_CONVERTERS") ?? "{}");

		string result = currentBody;
		result = ReplaceSection(result, "SCHEMA_VIEW_CONFIG_DIFF", mergedViewConfigDiff.ToString(Newtonsoft.Json.Formatting.None));
		result = ReplaceSection(result, "SCHEMA_VIEW_MODEL_CONFIG_DIFF", mergedViewModelConfigDiff.ToString(Newtonsoft.Json.Formatting.None));
		result = ReplaceSection(result, "SCHEMA_MODEL_CONFIG_DIFF", mergedModelConfigDiff.ToString(Newtonsoft.Json.Formatting.None));
		result = ReplaceSection(result, "SCHEMA_HANDLERS", mergedHandlers);
		result = ReplaceSection(result, "SCHEMA_CONVERTERS", mergedConverters);
		return result;
	}

	/// <summary>
	/// Merges two JavaScript converter object strings by key — incoming keys win over current keys
	/// with the same name. Preserves non-JSON function bodies by using raw text extraction rather
	/// than JSON parsing.
	/// </summary>
	private static string MergeConvertersRaw(string current, string incoming) {
		string currentTrim = current.Trim();
		string incomingTrim = incoming.Trim();
		if (currentTrim == "{}" || string.IsNullOrEmpty(currentTrim)) {
			return incomingTrim;
		}
		if (incomingTrim == "{}" || string.IsNullOrEmpty(incomingTrim)) {
			return currentTrim;
		}
		string currentInner = StripObjectBraces(currentTrim);
		string incomingInner = StripObjectBraces(incomingTrim);
		List<(string Key, string Entry)> currentEntries = ParseConverterEntries(currentInner);
		List<(string Key, string Entry)> incomingEntries = ParseConverterEntries(incomingInner);
		var incomingKeys = new HashSet<string>(incomingEntries.Select(e => e.Key), StringComparer.Ordinal);
		var kept = currentEntries
			.Where(e => !incomingKeys.Contains(e.Key))
			.Select(e => e.Entry)
			.Concat(incomingEntries.Select(e => e.Entry))
			.ToList();
		return kept.Count == 0 ? "{}" : "{" + string.Join(",", kept) + "}";
	}

	/// <summary>
	/// Parses a JavaScript object body (without outer braces) into a list of top-level key–value
	/// entry pairs. Each entry preserves the raw text of "key": value so the function body is
	/// never mangled. Stops at top-level commas — depth tracking covers nested {}, [], and ().
	/// </summary>
	/// <remarks>
	/// Limitation: JavaScript regex literals (<c>/pattern/flags</c>) are not tracked as string
	/// delimiters. A regex body containing an unbalanced <c>{</c>, <c>[</c>, or a bare <c>,</c>
	/// at depth 0 could cause a premature entry split. In practice, converter functions are simple
	/// formatters that do not use regex literals, so this edge case is not expected to occur.
	/// </remarks>
	private static List<(string Key, string Entry)> ParseConverterEntries(string inner) {
		var entries = new List<(string Key, string Entry)>();
		int i = 0;
		while (i < inner.Length) {
			// Skip inter-entry whitespace and commas.
			while (i < inner.Length && (char.IsWhiteSpace(inner[i]) || inner[i] == ',')) {
				i++;
			}
			if (i >= inner.Length) {
				break;
			}
			// Every entry must start with a quoted key.
			if (inner[i] != '"') {
				i++;
				continue;
			}
			int entryStart = i;
			// Read the key (between the two surrounding quotes).
			i++; // opening quote
			int keyStart = i;
			while (i < inner.Length && inner[i] != '"') {
				if (inner[i] == '\\') {
					i++;
				}
				i++;
			}
			string key = inner.Substring(keyStart, i - keyStart);
			if (i < inner.Length) {
				i++; // closing quote
			}
			// Skip the colon (and any surrounding whitespace).
			while (i < inner.Length && (char.IsWhiteSpace(inner[i]) || inner[i] == ':')) {
				i++;
			}
			// Read the value by tracking bracket depth.
			// The value ends at a top-level comma (depth == 0) or end of string.
			int depth = 0;
			bool inStr = false;
			char strChar = '"';
			while (i < inner.Length) {
				char ch = inner[i];
				if (inStr) {
					if (ch == '\\') {
						i += 2;
						continue;
					}
					if (ch == strChar) {
						inStr = false;
					}
					i++;
					continue;
				}
				if (ch is '"' or '\'' or '`') {
					inStr = true;
					strChar = ch;
					i++;
					continue;
				}
				if (ch is '(' or '{' or '[') {
					depth++;
					i++;
					continue;
				}
				if (ch is ')' or '}' or ']') {
					depth--;
					i++;
					continue;
				}
				if (depth == 0 && ch == ',') {
					break; // top-level comma ends this entry
				}
				i++;
			}
			string entry = inner.Substring(entryStart, i - entryStart).Trim();
			if (!string.IsNullOrWhiteSpace(entry)) {
				entries.Add((key, entry));
			}
			// Skip the trailing comma so the outer loop advances past it.
			if (i < inner.Length && inner[i] == ',') {
				i++;
			}
		}
		return entries;
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

	private static string StripObjectBraces(string value) {
		string trimmed = value.Trim();
		if (trimmed.StartsWith('{')) trimmed = trimmed.Substring(1);
		if (trimmed.EndsWith('}')) trimmed = trimmed.Substring(0, trimmed.Length - 1);
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
