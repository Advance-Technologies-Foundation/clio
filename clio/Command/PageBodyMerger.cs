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
	/// Actionable message emitted when a WEB page body uses the full-config form
	/// (<c>SCHEMA_VIEW_MODEL_CONFIG</c> / <c>SCHEMA_MODEL_CONFIG</c> markers) that append merge cannot process.
	/// Shared between the merge-time throw (current server body) and the up-front pre-execution guard (incoming body).
	/// </summary>
	internal const string WebFullConfigNotSupportedMessage =
		"Web append merge does not support bodies that use the full 'SCHEMA_VIEW_MODEL_CONFIG' or 'SCHEMA_MODEL_CONFIG' form. " +
		"Use 'replace' mode, or convert the body to the diff form (SCHEMA_VIEW_MODEL_CONFIG_DIFF / SCHEMA_MODEL_CONFIG_DIFF) before append.";

	/// <summary>
	/// Actionable message emitted when a MOBILE page body uses the full-config form
	/// (<c>viewModelConfig</c> / <c>modelConfig</c>) that append merge cannot process.
	/// Shared between the merge-time throw (current server body) and the up-front pre-execution guard (incoming body).
	/// </summary>
	internal const string MobileFullConfigNotSupportedMessage =
		"Mobile append merge does not support bodies that use the full 'viewModelConfig' or 'modelConfig' form. " +
		"Use 'replace' mode, or convert the body to the diff form (viewModelConfigDiff / modelConfigDiff) before append.";

	/// <summary>
	/// Detects whether <paramref name="body"/> uses the full-config form that append merge cannot process
	/// (web: <c>SCHEMA_VIEW_MODEL_CONFIG</c> / <c>SCHEMA_MODEL_CONFIG</c> markers; mobile: top-level
	/// <c>viewModelConfig</c> / <c>modelConfig</c> objects). Enables callers to surface an actionable,
	/// corrective message BEFORE attempting the merge (and, for the tool, before any server round-trip),
	/// rather than discovering the incompatibility only after <see cref="Merge"/> throws.
	/// </summary>
	/// <param name="body">The page body to inspect (incoming fragment or current server body).</param>
	/// <param name="message">
	/// On <see langword="true"/>, the surface-specific corrective message
	/// (<see cref="WebFullConfigNotSupportedMessage"/> or <see cref="MobileFullConfigNotSupportedMessage"/>);
	/// otherwise <see langword="null"/>.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when the body uses the unsupported full-config form. Fail-open
	/// (<see langword="false"/>) for a null/blank body or an unparseable mobile JSON body — those cases are
	/// left to the downstream <see cref="Merge"/> call, which surfaces the precise parse/empty-body error.
	/// </returns>
	public static bool UsesUnsupportedFullConfigForm(string body, out string message) {
		message = null;
		if (string.IsNullOrWhiteSpace(body)) {
			return false;
		}
		// Web full-config markers are unambiguous (an AMD body carrying the SCHEMA_VIEW_MODEL_CONFIG /
		// SCHEMA_MODEL_CONFIG comment markers), so check them first — independent of the leading-brace
		// heuristic — and always label the finding with the web message (ENG-93090 RC-4).
		if (ReadRawSection(body, "SCHEMA_VIEW_MODEL_CONFIG") != null ||
			ReadRawSection(body, "SCHEMA_MODEL_CONFIG") != null) {
			message = WebFullConfigNotSupportedMessage;
			return true;
		}
		if (PageSchemaTypeExtensions.FromBody(body) == PageSchemaType.Mobile) {
			JObject parsed;
			try {
				parsed = JObject.Parse(body);
			} catch (Newtonsoft.Json.JsonException) {
				// Fail-open: an unparseable mobile body is not our concern here — the merge (or the
				// upstream JSON/syntax validators) will surface the precise parse error.
				return false;
			}
			// A diff-form mobile body carries `viewModelConfigDiff` / `modelConfigDiff`; the full-config
			// keys are absent. Flag a top-level `viewModelConfig` / `modelConfig` that is present as
			// ANYTHING other than null — not only a JObject — so a malformed non-object value cannot slip
			// past detection and get silently dropped by the merge (ENG-93090 RC-8).
			if (IsPresentFullConfigToken(parsed["viewModelConfig"]) ||
				IsPresentFullConfigToken(parsed["modelConfig"])) {
				message = MobileFullConfigNotSupportedMessage;
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// A top-level mobile full-config key counts as "present" when it exists and is not JSON null,
	/// regardless of whether the value is an object, array, or scalar.
	/// </summary>
	private static bool IsPresentFullConfigToken(JToken token) =>
		token is not null && token.Type != JTokenType.Null;

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
		// Full-config detection for BOTH bodies runs here through the single shared predicate
		// (UsesUnsupportedFullConfigForm), so MergeWeb/MergeMobile no longer re-implement it (ENG-93090
		// RC-10) and incoming + current bodies share one detection path — which also closes the mobile
		// non-object gap on the current body (RC-9).
		//   - INCOMING: a full-config fragment against a diff-form current body would otherwise slip through
		//     (the merge reads only the incoming *_DIFF sections) and its full-config content be SILENTLY
		//     DROPPED — the ENG-90634 failure degraded to silent data loss on the CLI path (RC-1). The MCP
		//     tool also guards the incoming body up front (no fetch); this is the surface-agnostic backstop.
		//   - CURRENT: append merge supports only a diff-form server body; the full-config form cannot be
		//     merged without producing a mixed full-config/*Diff output.
		if (UsesUnsupportedFullConfigForm(incomingBody, out string incomingFullConfigMessage)) {
			throw new InvalidOperationException(incomingFullConfigMessage);
		}
		if (UsesUnsupportedFullConfigForm(currentBody, out string currentFullConfigMessage)) {
			throw new InvalidOperationException(currentFullConfigMessage);
		}
		return PageSchemaTypeExtensions.FromBody(currentBody) == PageSchemaType.Mobile
			? MergeMobile(currentBody, incomingBody)
			: MergeWeb(currentBody, incomingBody);
	}

	/// <summary>
	/// Merges two web (AMD) page bodies using marker-based section replacement.
	/// </summary>
	private static string MergeWeb(string currentBody, string incomingBody) {
		// Precondition: Merge() has already rejected a full-config current or incoming body via the shared
		// UsesUnsupportedFullConfigForm predicate, so this method only ever sees diff-form bodies.
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
		result = ReplaceSection(result, "SCHEMA_VIEW_CONFIG_DIFF", mergedViewConfigDiff.ToString(Newtonsoft.Json.Formatting.Indented));
		result = ReplaceSection(result, "SCHEMA_VIEW_MODEL_CONFIG_DIFF", mergedViewModelConfigDiff.ToString(Newtonsoft.Json.Formatting.Indented));
		result = ReplaceSection(result, "SCHEMA_MODEL_CONFIG_DIFF", mergedModelConfigDiff.ToString(Newtonsoft.Json.Formatting.Indented));
		result = ReplaceSection(result, "SCHEMA_HANDLERS", mergedHandlers);
		result = ReplaceSection(result, "SCHEMA_CONVERTERS", mergedConverters);
		return result;
	}

	/// <summary>
	/// Merges two mobile page bodies (plain JSON with top-level <c>viewConfigDiff</c>,
	/// <c>viewModelConfigDiff</c>, and <c>modelConfigDiff</c> arrays).
	/// </summary>
	private static string MergeMobile(string currentBody, string incomingBody) {
		JObject current;
		JObject incoming;
		try {
			current = JObject.Parse(currentBody);
		} catch (Exception ex) {
			throw new InvalidOperationException(
				$"Current mobile page body is not valid JSON: {ex.Message}", ex);
		}
		try {
			incoming = JObject.Parse(incomingBody);
		} catch (Exception ex) {
			throw new InvalidOperationException(
				$"Incoming mobile page body is not valid JSON: {ex.Message}", ex);
		}

		// Precondition: Merge() has already rejected a full-config current or incoming body via the shared
		// UsesUnsupportedFullConfigForm predicate — including a present-but-non-object viewModelConfig /
		// modelConfig on the current body (ENG-93090 RC-9) — so this method only ever sees diff-form bodies.
		JArray mergedViewConfigDiff = MergeArrayByName(
			current["viewConfigDiff"] as JArray ?? new JArray(),
			incoming["viewConfigDiff"] as JArray ?? new JArray());
		JArray mergedViewModelConfigDiff = MergeArrayAppend(
			current["viewModelConfigDiff"] as JArray ?? new JArray(),
			incoming["viewModelConfigDiff"] as JArray ?? new JArray());
		JArray mergedModelConfigDiff = MergeArrayAppend(
			current["modelConfigDiff"] as JArray ?? new JArray(),
			incoming["modelConfigDiff"] as JArray ?? new JArray());

		current["viewConfigDiff"] = mergedViewConfigDiff;
		current["viewModelConfigDiff"] = mergedViewModelConfigDiff;
		current["modelConfigDiff"] = mergedModelConfigDiff;

		return current.ToString(Newtonsoft.Json.Formatting.Indented);
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
			i = SkipSeparators(inner, i);
			if (i >= inner.Length) break;
			if (IsKeyQuote(inner[i])) {
				i = ReadQuotedEntry(inner, i, entries);
			} else if (IsIdentifierStart(inner[i])) {
				i = SkipUnquotedEntry(inner, i);
			} else {
				i++;
			}
		}
		return entries;
	}

	/// <summary>
	/// Reads one quoted key–value entry, appends it to <paramref name="entries"/>, and returns
	/// the position after the trailing comma (if present).
	/// </summary>
	private static int ReadQuotedEntry(string inner, int i, List<(string Key, string Entry)> entries) {
		int entryStart = i;
		i = ReadKey(inner, i, out string key);
		i = SkipColonAndWhitespace(inner, i);
		i = ScanValueEnd(inner, i);
		string entry = inner.Substring(entryStart, i - entryStart).Trim();
		if (!string.IsNullOrWhiteSpace(entry))
			entries.Add((key, entry));
		if (i < inner.Length && inner[i] == ',') i++;
		return i;
	}

	/// <summary>
	/// Skips an unquoted entry (e.g. ES6 method shorthand) without recording it. Consumes the
	/// entire entry — key + colon + value — so that string literals inside the function body are
	/// never mistaken for the next key. Use quoted keys per the converter guidance.
	/// </summary>
	private static int SkipUnquotedEntry(string inner, int i) {
		i = SkipUnquotedKey(inner, i);
		if (i < inner.Length && inner[i] == ':') {
			i = SkipColonAndWhitespace(inner, i);
			i = ScanValueEnd(inner, i);
		}
		if (i < inner.Length && inner[i] == ',') i++;
		return i;
	}

	/// <summary>
	/// Advances past an unquoted key name using bracket-depth tracking so that a <c>:</c> inside a
	/// complex default argument (e.g. <c>(v = {key: val})</c>) is not treated as the key separator.
	/// </summary>
	private static int SkipUnquotedKey(string inner, int i) {
		int keyDepth = 0;
		while (i < inner.Length) {
			char kc = inner[i];
			if (IsOpenBracket(kc)) { keyDepth++; i++; continue; }
			if (IsCloseBracket(kc)) {
				if (keyDepth <= 0) break;
				keyDepth--;
				i++;
				continue;
			}
			if (keyDepth == 0 && (kc == ':' || kc == ',')) break;
			i++;
		}
		return i;
	}

	private static bool IsKeyQuote(char ch) => ch is '"' or '\'';
	private static bool IsIdentifierStart(char ch) => char.IsLetterOrDigit(ch) || ch == '_' || ch == '$';

	private static int SkipSeparators(string s, int i) {
		while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ','))
			i++;
		return i;
	}

	private static int ReadKey(string s, int i, out string key) {
		char openQuote = s[i];
		i++; // skip opening quote
		int start = i;
		while (i < s.Length && s[i] != openQuote) {
			if (s[i] == '\\') i++;
			i++;
		}
		key = s.Substring(start, i - start);
		if (i < s.Length) i++; // skip closing quote
		return i;
	}

	private static int SkipColonAndWhitespace(string s, int i) {
		while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ':'))
			i++;
		return i;
	}

	/// <summary>
	/// Advances <paramref name="i"/> past the current converter value, stopping at a top-level
	/// comma or end-of-string. Tracks string literals and bracket depth to avoid false splits.
	/// </summary>
	/// <remarks>
	/// Known limitations:
	/// <list type="bullet">
	/// <item>JavaScript regex literals (<c>/pattern/flags</c>) are not tracked as string delimiters.
	/// A regex body containing an unbalanced bracket or a bare comma at depth 0 could cause a
	/// premature entry split. Converter functions in practice do not use unbalanced regex literals.</item>
	/// <item>Template literal interpolations (<c>`outer ${inner}`</c>) are not depth-tracked.
	/// <see cref="AdvanceInString"/> uses a single <c>strChar</c> so a <c>}</c> that closes a
	/// <c>${…}</c> interpolation is treated as the end of the template string, potentially causing
	/// false depth changes for any brackets that follow. Async converters that return a template
	/// literal (e.g. <c>`+${digits}`</c>) are not affected as long as the interpolation does not
	/// contain an unmatched bracket.</item>
	/// </list>
	/// </remarks>
	private static int ScanValueEnd(string s, int i) {
		int depth = 0;
		bool inStr = false;
		char strChar = '"';
		while (i < s.Length) {
			char ch = s[i];
			if (inStr) { i = AdvanceInString(i, ch, strChar, ref inStr); continue; }
			if (IsStringDelimiter(ch)) { inStr = true; strChar = ch; i++; continue; }
			if (IsOpenBracket(ch)) { depth++; i++; continue; }
			if (IsCloseBracket(ch)) {
				if (depth <= 0) break;
				depth--;
				i++;
				continue;
			}
			if (depth == 0 && ch == ',') break;
			i++;
		}
		return i;
	}

	private static int AdvanceInString(int i, char ch, char strChar, ref bool inStr) {
		if (ch == '\\') return i + 2;
		if (ch == strChar) inStr = false;
		return i + 1;
	}

	private static bool IsStringDelimiter(char ch) => ch is '"' or '\'' or '`';
	private static bool IsOpenBracket(char ch) => ch is '(' or '{' or '[';
	private static bool IsCloseBracket(char ch) => ch is ')' or '}' or ']';

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
