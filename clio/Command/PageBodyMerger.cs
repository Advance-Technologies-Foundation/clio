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
	/// The two operation verbs whose APPLY BEHAVIOUR depends on whether the entry carries a
	/// <c>properties</c> array, so for them that array is part of the merge identity.
	/// </summary>
	/// <remarks>
	/// <c>remove</c> splits at grouping time: <see cref="JsonDiffApplier"/> routes a property removal and
	/// an element removal into different groups, applied in different passes.
	/// <para>
	/// <c>set</c> splits INSIDE its own pass, which is easy to miss and was missed once: <c>Set</c> calls
	/// <c>Remove</c>, and <c>Remove</c> either strips the named keys in place (properties form) or detaches
	/// the element and returns its position so the following <c>Insert</c> can restore it (element form).
	/// The two do materially different things, so two <c>set</c> entries for one name that differ only by
	/// <c>properties</c> must not collide — conflating them destroys one, which is the defect class #1132
	/// exists to eliminate.
	/// </para>
	/// Both compared Ordinal, matching the differ's own exact-case switch.
	/// </remarks>
	private const string RemoveOperationName = "remove";

	/// <inheritdoc cref="RemoveOperationName"/>
	private const string SetOperationName = "set";

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
	/// <remarks>
	/// Discards the merge's warnings. A production caller that SAVES the result should use
	/// <see cref="Merge(string, string, out IReadOnlyList{string})"/> instead and surface them — silently
	/// dropping an operation the caller did not send is the defect #1132 filed. This overload exists for
	/// call sites that only need the merged text (tests, previews).
	/// </remarks>
	public static string Merge(string currentBody, string incomingBody) =>
		Merge(currentBody, incomingBody, out _);

	/// <summary>
	/// Reporting overload of <see cref="Merge(string, string)"/>. Identical merge; additionally reports the
	/// one loss the merge cannot avoid, so the caller can surface it instead of the page silently changing.
	/// </summary>
	/// <param name="currentBody">The schema's existing body on the server.</param>
	/// <param name="incomingBody">The fragment the caller wants to add.</param>
	/// <param name="supersededDropWarnings">
	/// One actionable warning per IDENTITY whose further current entries were dropped because the incoming
	/// fragment superseded it. Empty — never <see langword="null"/> — in the normal case.
	/// <para>
	/// Deliberately covers the CURRENT body only. If the incoming fragment itself repeats one identity, last
	/// spelling wins and the earlier one is discarded without a warning: that body is the caller's own, they
	/// can read it, and warning about their own input would be noise. The current body is different — the
	/// caller cannot see what the server held.
	/// </para>
	/// </param>
	/// <remarks>
	/// #1132 asks that where the merge cannot preserve an operation it "fail before saving with an actionable
	/// conflict or warning". This is that channel. It is a WARNING and not a rejection because the merge
	/// result is still strictly better than the alternatives — refusing the write would strand the caller with
	/// no way to append at all, and dropping silently is the defect being fixed.
	/// </remarks>
	public static string Merge(string currentBody, string incomingBody, out IReadOnlyList<string> supersededDropWarnings) {
		var drops = new List<string>();
		supersededDropWarnings = drops;
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
			? MergeMobile(currentBody, incomingBody, drops)
			: MergeWeb(currentBody, incomingBody, drops);
	}

	/// <summary>
	/// Merges two web (AMD) page bodies using marker-based section replacement.
	/// </summary>
	private static string MergeWeb(string currentBody, string incomingBody, List<string> drops) {
		// Precondition: Merge() has already rejected a full-config current or incoming body via the shared
		// UsesUnsupportedFullConfigForm predicate, so this method only ever sees diff-form bodies.
		JArray mergedViewConfigDiff = MergeViewConfigDiffOperations(
			ReadJsonArray(currentBody, "SCHEMA_VIEW_CONFIG_DIFF"),
			ReadJsonArray(incomingBody, "SCHEMA_VIEW_CONFIG_DIFF"),
			drops);
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
	private static string MergeMobile(string currentBody, string incomingBody, List<string> drops) {
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
		JArray mergedViewConfigDiff = MergeViewConfigDiffOperations(
			current["viewConfigDiff"] as JArray ?? new JArray(),
			incoming["viewConfigDiff"] as JArray ?? new JArray(),
			drops);
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

	/// <summary>
	/// Merges two <c>viewConfigDiff</c> operation arrays. What collides is an
	/// <see cref="OperationIdentity"/>, not a <c>name</c> alone: every current entry survives at its
	/// original position unless the incoming fragment carries the same identity, which replaces the first
	/// occurrence in place and supersedes any later one. Unmatched incoming entries are appended in the
	/// caller's own order.
	/// </summary>
	/// <remarks>
	/// GitHub #1132: the previous version keyed <c>current.Concat(incoming)</c> by <c>name</c> alone, so a
	/// page carrying both a <c>move</c> and a <c>merge</c> for one component silently lost the <c>move</c>
	/// on any append. Why several operations per name are legitimate, why the identity is shaped the way it
	/// is, and why a transform kept beside an <c>insert</c> is inert (GH-1240), are recorded in
	/// <c>docs/knowledge/Command/viewconfigdiff-carries-multiple-operations-per-component-name.md</c>.
	/// </remarks>
	private static JArray MergeViewConfigDiffOperations(JArray current, JArray incoming, List<string> drops) {
		Dictionary<OperationIdentity, JToken> incomingByIdentity = IndexIncomingByIdentity(incoming);
		var replaced = new HashSet<OperationIdentity>();
		var merged = new JArray();
		AppendCurrentEntries(current, incomingByIdentity, replaced, merged, drops);
		AppendUnmatchedIncomingEntries(incoming, incomingByIdentity, replaced, merged);
		return merged;
	}

	/// <summary>
	/// Indexes the incoming fragment by operation identity. Last spelling wins within one fragment; the
	/// emit pass still places it at the first occurrence's position.
	/// </summary>
	private static Dictionary<OperationIdentity, JToken> IndexIncomingByIdentity(JArray incoming) {
		var incomingByIdentity = new Dictionary<OperationIdentity, JToken>();
		foreach (JToken item in incoming) {
			if (TryGetOperationIdentity(item, out OperationIdentity identity)) {
				incomingByIdentity[identity] = item;
			}
		}
		return incomingByIdentity;
	}

	/// <summary>
	/// Emits the current body in place, substituting the incoming entry wherever an identity collides,
	/// and reporting the one entry the merge cannot preserve.
	/// </summary>
	private static void AppendCurrentEntries(JArray current,
		Dictionary<OperationIdentity, JToken> incomingByIdentity, HashSet<OperationIdentity> replaced,
		JArray merged, List<string> drops) {
		var warned = new HashSet<OperationIdentity>();
		foreach (JToken item in current) {
			if (!TryGetOperationIdentity(item, out OperationIdentity identity) ||
				!incomingByIdentity.TryGetValue(identity, out JToken replacement)) {
				// The #1132 fix: with no incoming counterpart, a current entry is kept even when an
				// earlier one shares its identity.
				merged.Add(item);
				continue;
			}
			// Only the first occurrence is replaced. A later current entry of the same identity is
			// dropped, because keeping it would re-apply stale values after the replacement — at the cost
			// of losing its keys when the two set disjoint ones.
			if (replaced.Add(identity)) {
				merged.Add(replacement);
				continue;
			}
			// The one loss the merge cannot avoid, so it is REPORTED rather than silent (#1132 AC4).
			// One warning per IDENTITY, not per dropped entry: three carried occurrences would otherwise
			// emit two byte-identical sentences, and CombineWarnings does not dedupe.
			if (warned.Add(identity)) {
				drops.Add(BuildSupersededDropMessage(identity));
			}
		}
	}

	/// <summary>
	/// Appends the incoming entries that did not replace anything, in the order the caller gave them, so
	/// an unidentified entry keeps its position.
	/// </summary>
	private static void AppendUnmatchedIncomingEntries(JArray incoming,
		Dictionary<OperationIdentity, JToken> incomingByIdentity, HashSet<OperationIdentity> replaced,
		JArray merged) {
		var emitted = new HashSet<OperationIdentity>();
		foreach (JToken item in incoming) {
			if (!TryGetOperationIdentity(item, out OperationIdentity identity)) {
				merged.Add(item);
				continue;
			}
			if (!replaced.Contains(identity) && emitted.Add(identity)) {
				merged.Add(incomingByIdentity[identity]);
			}
		}
	}

	/// <summary>
	/// Identity of one <c>viewConfigDiff</c> operation, mirroring the distinctions the differ itself
	/// makes. A record struct rather than a composed string key: any separator can be smuggled inside a
	/// value — <c>U+0000</c> included, it is a legal JSON escape — which would forge a collision.
	/// </summary>
	/// <param name="Operation">The operation verb, or the empty string when the entry carries none.</param>
	/// <param name="Name">The target component name.</param>
	/// <param name="TargetsProperties">
	/// A <c>remove</c> or <c>set</c> carrying a <c>properties</c> array — the two verbs whose apply
	/// behaviour that array changes (see <see cref="RemoveOperationName"/>). Without this component the
	/// property form and the element form of one verb would share an identity and one would be destroyed.
	/// </param>
	private readonly record struct OperationIdentity(string Operation, string Name, bool TargetsProperties);

	/// <summary>
	/// Actionable warning for a current entry dropped because the incoming fragment superseded an identity
	/// the current body carried more than once.
	/// </summary>
	/// <remarks>
	/// Only the FIRST occurrence is replaced; a later one cannot also be kept, because the differ applies a
	/// group in array order and the stale values would then re-apply after the caller's replacement. The
	/// message names what to do about it, since the caller cannot see the server body they just overwrote.
	/// </remarks>
	private static string BuildSupersededDropMessage(OperationIdentity identity) {
		string verb = string.IsNullOrEmpty(identity.Operation) ? "(no operation)" : identity.Operation;
		return $"Component '{identity.Name}' carried more than one '{verb}' operation in the page's own body, " +
			"and the appended fragment supersedes that operation. Only the first occurrence was replaced; every " +
			"later one was dropped, because keeping it would re-apply its values AFTER your replacement. If they " +
			"set different keys, the dropped entries' keys are gone from the saved page. Re-read the page with " +
			"get-page and re-apply anything missing. See docs://mcp/guides/page-modification.";
	}

	/// <summary>
	/// Whether a <c>properties</c> array changes how the differ applies this verb — true for
	/// <c>remove</c> and <c>set</c>, false for every other verb, where <c>properties</c> is inert and must
	/// therefore not split the identity.
	/// </summary>
	private static bool SplitsOnProperties(string operation) =>
		string.Equals(operation, RemoveOperationName, StringComparison.Ordinal)
		|| string.Equals(operation, SetOperationName, StringComparison.Ordinal);

	/// <summary>
	/// Computes an entry's <see cref="OperationIdentity"/>. Returns <see langword="false"/> for a
	/// non-object element, or one whose <c>name</c> is not a non-empty JSON string — such an entry is
	/// never merged and never reordered, which is the safe direction.
	/// </summary>
	/// <remarks>
	/// <c>operation</c> is ordinal and never case-folded: <see cref="JsonDiffApplier"/> switches on the raw
	/// string with no default case, so a mis-cased <c>"Merge"</c> is discarded at apply time and must not
	/// be allowed to replace a working <c>"merge"</c>. A missing <c>operation</c> is not defaulted either —
	/// guessing one would replace an operation the caller never named.
	/// </remarks>
	private static bool TryGetOperationIdentity(JToken item, out OperationIdentity identity) {
		identity = default;
		if (item is not JObject operationItem) {
			return false;
		}
		if (operationItem["name"] is not JValue { Type: JTokenType.String } nameValue) {
			return false;
		}
		string name = nameValue.Value<string>();
		if (string.IsNullOrEmpty(name)) {
			return false;
		}
		string operation = operationItem["operation"] is JValue { Type: JTokenType.String } operationValue
			? operationValue.Value<string>()
			: string.Empty;
		bool targetsProperties = SplitsOnProperties(operation) && operationItem["properties"] is JArray;
		identity = new OperationIdentity(operation, name, targetsProperties);
		return true;
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
