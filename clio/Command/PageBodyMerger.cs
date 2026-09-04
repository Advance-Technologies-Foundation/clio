namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Acornima;
using Acornima.Ast;
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
	/// Which side of the append merge a body belongs to, so the full-config rejection message can name the
	/// correct actor. The corrective advice differs by role: the caller can convert an <see cref="Incoming"/>
	/// body they authored, but a <see cref="Current"/> (server-side) body is not theirs to convert — for that
	/// one the only path is <c>--mode replace</c> (ENG-94422). Append is not supported against a full-config
	/// current body by design: a <c>*_DIFF</c> is a list of operations relative to a base and cannot be
	/// losslessly derived from an already-resolved full-config body without that base — the same reason
	/// ENG-93090 rejected auto-converting a full-config body on append.
	/// </summary>
	internal enum PageBodyRole {

		/// <summary>The fragment the caller passed to append (authored by the caller, convertible by the caller).</summary>
		Incoming,

		/// <summary>The schema's existing body fetched from the server (not authored by the caller).</summary>
		Current
	}

	/// <summary>
	/// Thrown by <see cref="Merge"/> when an append merge is rejected because one of the two bodies uses the
	/// full-config form rather than the diff form. Carrying a dedicated type (rather than a bare
	/// <see cref="InvalidOperationException"/>) lets a caller that wraps the failure classify it by TYPE instead
	/// of re-parsing <see cref="Exception.Message"/> against the four rejection constants: the CLI wrapper uses
	/// this to suppress the generic "body must contain valid marker pairs" hint, which does not apply to a
	/// full-config body — it HAS valid markers, it is simply the wrong form (ENG-94422). Derives from
	/// <see cref="InvalidOperationException"/> so existing <c>catch (InvalidOperationException)</c> sites are
	/// unaffected. The <see cref="Exception.Message"/> is still one of the four full-config rejection constants.
	/// </summary>
	internal sealed class FullConfigAppendNotSupportedException : InvalidOperationException {

		/// <summary>Creates the exception with one of the four full-config rejection messages.</summary>
		/// <param name="message">The role- and surface-specific full-config rejection message.</param>
		public FullConfigAppendNotSupportedException(string message) : base(message) { }
	}

	/// <summary>
	/// Actionable message emitted when the caller's <em>incoming</em> WEB body uses the full-config form
	/// (<c>SCHEMA_VIEW_MODEL_CONFIG</c> / <c>SCHEMA_MODEL_CONFIG</c> markers) that append merge cannot process.
	/// The caller authored this body, so converting it to the diff form is a valid corrective action.
	/// </summary>
	internal const string WebIncomingFullConfigNotSupportedMessage =
		"Web append merge does not support an incoming body that uses the full 'SCHEMA_VIEW_MODEL_CONFIG' or 'SCHEMA_MODEL_CONFIG' form. " +
		"Use 'replace' mode, or convert the incoming body to the diff form (SCHEMA_VIEW_MODEL_CONFIG_DIFF / SCHEMA_MODEL_CONFIG_DIFF) before append.";

	/// <summary>
	/// Actionable message emitted when the <em>current</em> server-side WEB body uses the full-config form
	/// (<c>SCHEMA_VIEW_MODEL_CONFIG</c> / <c>SCHEMA_MODEL_CONFIG</c> markers) — e.g. every page
	/// <c>create-app-section</c> generates. The caller did not author this body and cannot convert it, so the
	/// message points at <c>--mode replace</c> instead of telling them to convert their own body (ENG-94422).
	/// </summary>
	internal const string WebCurrentFullConfigNotSupportedMessage =
		"Web append merge cannot run because the page on the server is stored in the full 'SCHEMA_VIEW_MODEL_CONFIG' or 'SCHEMA_MODEL_CONFIG' form. " +
		"Use 'replace' mode — the server-side body is not authored by the caller, so it cannot be converted to the diff form (SCHEMA_VIEW_MODEL_CONFIG_DIFF / SCHEMA_MODEL_CONFIG_DIFF) from here.";

	/// <summary>
	/// Actionable message emitted when the caller's <em>incoming</em> MOBILE body uses the full-config form
	/// (<c>viewModelConfig</c> / <c>modelConfig</c>) that append merge cannot process. The caller authored this
	/// body, so converting it to the diff form is a valid corrective action.
	/// </summary>
	internal const string MobileIncomingFullConfigNotSupportedMessage =
		"Mobile append merge does not support an incoming body that uses the full 'viewModelConfig' or 'modelConfig' form. " +
		"Use 'replace' mode, or convert the incoming body to the diff form (viewModelConfigDiff / modelConfigDiff) before append.";

	/// <summary>
	/// Actionable message emitted when the <em>current</em> server-side MOBILE body uses the full-config form
	/// (<c>viewModelConfig</c> / <c>modelConfig</c>). The caller did not author this body and cannot convert it,
	/// so the message points at <c>--mode replace</c> instead of telling them to convert their own body (ENG-94422).
	/// </summary>
	internal const string MobileCurrentFullConfigNotSupportedMessage =
		"Mobile append merge cannot run because the page on the server is stored in the full 'viewModelConfig' or 'modelConfig' form. " +
		"Use 'replace' mode — the server-side body is not authored by the caller, so it cannot be converted to the diff form (viewModelConfigDiff / modelConfigDiff) from here.";

	/// <summary>
	/// Detects whether <paramref name="body"/> uses the full-config form that append merge cannot process
	/// (web: <c>SCHEMA_VIEW_MODEL_CONFIG</c> / <c>SCHEMA_MODEL_CONFIG</c> markers; mobile: top-level
	/// <c>viewModelConfig</c> / <c>modelConfig</c> objects). Enables callers to surface an actionable,
	/// corrective message BEFORE attempting the merge (and, for the tool, before any server round-trip),
	/// rather than discovering the incompatibility only after <see cref="Merge"/> throws.
	/// </summary>
	/// <param name="body">The page body to inspect (incoming fragment or current server body).</param>
	/// <param name="message">
	/// On <see langword="true"/>, the surface-specific corrective message for an <see cref="PageBodyRole.Incoming"/>
	/// body (<see cref="WebIncomingFullConfigNotSupportedMessage"/> or <see cref="MobileIncomingFullConfigNotSupportedMessage"/>);
	/// otherwise <see langword="null"/>.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when the body uses the unsupported full-config form. Fail-open
	/// (<see langword="false"/>) for a null/blank body or an unparseable mobile JSON body — those cases are
	/// left to the downstream <see cref="Merge"/> call, which surfaces the precise parse/empty-body error.
	/// </returns>
	/// <remarks>
	/// This overload assumes the <see cref="PageBodyRole.Incoming"/> role (the body the caller authored), which
	/// is the correct default for the up-front MCP guard that only ever inspects the incoming body. Use the
	/// <see cref="UsesUnsupportedFullConfigForm(string, PageBodyRole, out string)"/> overload to get a message
	/// tailored to a <see cref="PageBodyRole.Current"/> server-side body (ENG-94422).
	/// </remarks>
	public static bool UsesUnsupportedFullConfigForm(string body, out string message) =>
		UsesUnsupportedFullConfigForm(body, PageBodyRole.Incoming, out message);

	/// <summary>
	/// Role-aware overload of <see cref="UsesUnsupportedFullConfigForm(string, out string)"/>: the emitted
	/// message names the correct actor and corrective action for <paramref name="role"/>. An
	/// <see cref="PageBodyRole.Incoming"/> body can be converted by the caller; a <see cref="PageBodyRole.Current"/>
	/// (server-side) body cannot, so its message points at <c>--mode replace</c> rather than telling the caller
	/// to convert a body they did not author (ENG-94422).
	/// </summary>
	/// <param name="body">The page body to inspect.</param>
	/// <param name="role">Whether <paramref name="body"/> is the caller's incoming fragment or the server's current body.</param>
	/// <param name="message">On <see langword="true"/>, the role- and surface-specific corrective message; otherwise <see langword="null"/>.</param>
	/// <returns>Same detection semantics as <see cref="UsesUnsupportedFullConfigForm(string, out string)"/>.</returns>
	public static bool UsesUnsupportedFullConfigForm(string body, PageBodyRole role, out string message) {
		message = null;
		if (string.IsNullOrWhiteSpace(body)) {
			return false;
		}
		// Web full-config markers are unambiguous (an AMD body carrying the SCHEMA_VIEW_MODEL_CONFIG /
		// SCHEMA_MODEL_CONFIG comment markers), so check them first — independent of the leading-brace
		// heuristic — and always label the finding with the web message (ENG-93090 RC-4).
		if (ReadRawSection(body, "SCHEMA_VIEW_MODEL_CONFIG") != null ||
			ReadRawSection(body, "SCHEMA_MODEL_CONFIG") != null) {
			message = role == PageBodyRole.Current
				? WebCurrentFullConfigNotSupportedMessage
				: WebIncomingFullConfigNotSupportedMessage;
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
				message = role == PageBodyRole.Current
					? MobileCurrentFullConfigNotSupportedMessage
					: MobileIncomingFullConfigNotSupportedMessage;
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
		if (UsesUnsupportedFullConfigForm(incomingBody, PageBodyRole.Incoming, out string incomingFullConfigMessage)) {
			throw new FullConfigAppendNotSupportedException(incomingFullConfigMessage);
		}
		if (UsesUnsupportedFullConfigForm(currentBody, PageBodyRole.Current, out string currentFullConfigMessage)) {
			throw new FullConfigAppendNotSupportedException(currentFullConfigMessage);
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
		string mergedConverters = MergeKeyedObjectRaw(
			ReadRawSection(currentBody, "SCHEMA_CONVERTERS") ?? "{}",
			ReadRawSection(incomingBody, "SCHEMA_CONVERTERS") ?? "{}");
		string mergedValidators = MergeKeyedObjectRaw(
			ReadRawSection(currentBody, "SCHEMA_VALIDATORS") ?? "{}",
			ReadRawSection(incomingBody, "SCHEMA_VALIDATORS") ?? "{}");

		string result = currentBody;
		result = ReplaceSection(result, "SCHEMA_VIEW_CONFIG_DIFF", mergedViewConfigDiff.ToString(Newtonsoft.Json.Formatting.Indented));
		result = ReplaceSection(result, "SCHEMA_VIEW_MODEL_CONFIG_DIFF", mergedViewModelConfigDiff.ToString(Newtonsoft.Json.Formatting.Indented));
		result = ReplaceSection(result, "SCHEMA_MODEL_CONFIG_DIFF", mergedModelConfigDiff.ToString(Newtonsoft.Json.Formatting.Indented));
		result = ReplaceSection(result, "SCHEMA_HANDLERS", mergedHandlers);
		result = ReplaceSection(result, "SCHEMA_CONVERTERS", mergedConverters);
		result = ReplaceSection(result, "SCHEMA_VALIDATORS", mergedValidators);
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
	/// Merges two JavaScript object strings by key — incoming keys win over current keys
	/// with the same name. Preserves non-JSON function bodies by using raw text extraction rather
	/// than JSON parsing.
	/// </summary>
	private static string MergeKeyedObjectRaw(string current, string incoming) {
		string currentTrim = current.Trim();
		string incomingTrim = incoming.Trim();
		if (currentTrim == "{}" || string.IsNullOrEmpty(currentTrim)) {
			return incomingTrim;
		}
		if (incomingTrim == "{}" || string.IsNullOrEmpty(incomingTrim)) {
			return currentTrim;
		}
		List<(string Key, string Entry)> currentEntries = ParseObjectEntries(currentTrim);
		List<(string Key, string Entry)> incomingEntries = ParseObjectEntries(incomingTrim);
		var incomingKeys = new HashSet<string>(
			incomingEntries.Where(e => e.Key != null).Select(e => e.Key),
			StringComparer.Ordinal);
		var kept = currentEntries
			.Where(e => e.Key == null || !incomingKeys.Contains(e.Key))
			.Select(e => e.Entry)
			.Concat(incomingEntries.Select(e => e.Entry))
			.ToList();
		return kept.Count == 0 ? "{}" : "{" + string.Join(",", kept) + "}";
	}

	/// <summary>
	/// Parses a JavaScript object into top-level key-entry pairs using Acornima source ranges.
	/// Each entry preserves its raw source, including functions, comments, regular expressions,
	/// and template literals.
	/// </summary>
	private static List<(string Key, string Entry)> ParseObjectEntries(string objectSource) {
		var entries = new List<(string Key, string Entry)>();
		string wrappedSource = $"({objectSource})";
		Script script = new Parser().ParseScript(wrappedSource);
		if (script.ChildNodes.FirstOrDefault() is not ExpressionStatement {
			Expression: ObjectExpression objectExpression
		}) {
			throw new InvalidOperationException("Expected a JavaScript object expression.");
		}
		foreach (Node element in objectExpression.Properties) {
			string key = element is Property { Computed: false } property
				? property.Key switch {
					Identifier identifier => identifier.Name,
					Literal { Value: string literalKey } => literalKey,
					_ => null
				}
				: null;
			string entry = wrappedSource.Substring(element.Start, element.End - element.Start).Trim();
			if (!string.IsNullOrWhiteSpace(entry)) {
				entries.Add((key, entry));
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
