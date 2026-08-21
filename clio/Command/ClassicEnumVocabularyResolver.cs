namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Clio.Common;

/// <summary>
/// One enum table parsed from the target stand's own <c>sysenums.js</c>: member name to numeric value, verbatim as
/// core spells it. Never a copy of the migration engine's pinned tables — the whole point of ENG-95412 is measuring
/// the STAND's own values so its enum-drift guard has a real, independent input to compare against.
/// </summary>
public readonly record struct ClassicEnumVocabularyParseResult(
	IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>> Enums,
	IReadOnlyList<string> Warnings);

/// <summary>Parses core's <c>sysenums.js</c> source text into the enum-drift guard's <c>enumVocabulary</c> tables.</summary>
public interface IClassicEnumVocabularySourceParser {

	/// <summary>
	/// Extracts <c>Terrasoft.ViewItemType</c>/<c>ContentType</c>/<c>DataValueType</c> from raw <c>sysenums.js</c> text.
	/// A block that is absent, truncated, or carries no numeric member is omitted from <see cref="ClassicEnumVocabularyParseResult.Enums"/>
	/// (never emitted empty) and explained in <see cref="ClassicEnumVocabularyParseResult.Warnings"/> — a partial echo is the
	/// expected degraded outcome, never a hard failure.
	/// </summary>
	ClassicEnumVocabularyParseResult Parse(string sysEnumsJsContent);
}

/// <summary>
/// Brace-matched, never-executed extraction of the three enum tables the classic-to-freedom-migration engine's
/// <c>enumDriftIssues</c> guard reads (<c>DRIFT_TABLES</c> in <c>engine/engine.mjs</c>). The source declares each as a
/// plain object literal (<c>Terrasoft.ViewItemType = { NAME: 0, ... };</c>), so reading it is a text extraction, not a
/// script evaluation.
/// </summary>
internal sealed class ClassicEnumVocabularySourceParser : IClassicEnumVocabularySourceParser {

	// Manifest key == the global name core assigns it (Terrasoft.ViewItemType / ContentType / DataValueType), so no
	// separate mapping table is needed beyond this list.
	private static readonly string[] EnumNames = ["ViewItemType", "ContentType", "DataValueType"];

	// The consumer (engine.mjs) reads the echoed vocabulary with Object.hasOwn, never `in`, specifically so a member
	// named like a prototype method cannot be mistaken for a pinned enum member. Blocked rather than allow-listed so a
	// future core member in a different case convention is not silently dropped by an ALL-CAPS-only filter.
	private static readonly HashSet<string> BlockedMemberNames = new(StringComparer.Ordinal) {
		"__proto__", "constructor", "prototype", "toString", "toLocaleString",
		"valueOf", "hasOwnProperty", "isPrototypeOf", "propertyIsEnumerable"
	};

	// NAME: 123 (or NAME: -1) followed by a comma or the closing brace. Comments AND string-literal contents are
	// blanked out before this runs (BlankCommentsAndStringLiterals), so neither a JSDoc line nor a quoted description
	// mentioning a colon and a digit can be mistaken for a member.
	private static readonly Regex MemberRegex = new(
		@"(?<![\w$])([A-Za-z_$][\w$]*)\s*:\s*(-?\d+)\s*(?=,|\}|$)",
		RegexOptions.Compiled, TimeSpan.FromSeconds(2));

	/// <inheritdoc />
	public ClassicEnumVocabularyParseResult Parse(string sysEnumsJsContent) {
		var enums = new Dictionary<string, IReadOnlyDictionary<string, long>>(StringComparer.Ordinal);
		var warnings = new List<string>();
		if (string.IsNullOrWhiteSpace(sysEnumsJsContent)) {
			warnings.Add("sysenums.js content is empty; enumVocabulary omitted.");
			return new ClassicEnumVocabularyParseResult(enums, warnings);
		}
		// Comments and string-literal contents are blanked out ONCE, up front, and every later step (marker search,
		// brace matching, member scan) reads that same blanked text. Doing it once is not just cheaper than three
		// passes: it makes it impossible for the block finder and the member scanner to disagree about where a
		// string or comment ends, because they are literally looking at the same characters. Blanking preserves
		// length and therefore every offset, so positions in the blanked text still address the original source.
		string scannable = BlankCommentsAndStringLiterals(sysEnumsJsContent);
		foreach (string enumName in EnumNames) {
			string block = ExtractObjectLiteral(scannable, enumName);
			if (block == null) {
				warnings.Add(
					$"Could not find a complete 'Terrasoft.{enumName} = {{ ... }}' block in sysenums.js; " +
					$"'{enumName}' omitted from enumVocabulary.");
				continue;
			}
			IReadOnlyDictionary<string, long> members = ParseMembers(block);
			if (members.Count == 0) {
				warnings.Add(
					$"'Terrasoft.{enumName}' block carried no numeric members; '{enumName}' omitted from enumVocabulary.");
				continue;
			}
			enums[enumName] = members;
		}
		return new ClassicEnumVocabularyParseResult(enums, warnings);
	}

	// Finds `Terrasoft.<enumName> = { ... }` (the exact assignment, not an alias line such as
	// `Terrasoft.core.enums.ViewItemType = Terrasoft.ViewItemType`, which resolves to an identifier rather than a
	// brace) and returns the object-literal text INCLUDING the surrounding braces, or null when no such assignment
	// with a matching closing brace exists (missing member, or the file is truncated mid-block). Operates on the
	// comment/string-blanked text, so a marker mentioned inside a quoted string cannot anchor the search.
	private static string ExtractObjectLiteral(string content, string enumName) {
		string marker = "Terrasoft." + enumName;
		int searchFrom = 0;
		while (true) {
			int markerIndex = content.IndexOf(marker, searchFrom, StringComparison.Ordinal);
			if (markerIndex < 0) {
				return null;
			}
			int afterMarker = markerIndex + marker.Length;
			int skipTo = NextOffsetWhenMarkerIsPartOfLongerIdentifier(content, markerIndex, afterMarker);
			if (skipTo >= 0) {
				searchFrom = skipTo;
				continue;
			}
			int openBraceIndex = FindAssignedObjectLiteralStart(content, afterMarker);
			if (openBraceIndex < 0) {
				searchFrom = afterMarker;
				continue;
			}
			int closingBraceIndex = FindMatchingBrace(content, openBraceIndex);
			return closingBraceIndex < 0
				? null
				: content.Substring(openBraceIndex, closingBraceIndex - openBraceIndex + 1);
		}
	}

	// Guards the marker match on BOTH sides: a leading identifier char means the match is the tail of a longer name
	// (a hypothetical XTerrasoft.ViewItemType) — advance by one so a real, later occurrence is still found; a
	// trailing one means a longer name merely starts with it (ViewItemTypeExtra) — skip past it rather than
	// mis-anchoring on a prefix. Returns the next search offset, or -1 when the marker stands on its own.
	private static int NextOffsetWhenMarkerIsPartOfLongerIdentifier(string content, int markerIndex, int afterMarker) {
		if (markerIndex > 0 && IsIdentifierChar(content[markerIndex - 1])) {
			return markerIndex + 1;
		}
		return afterMarker < content.Length && IsIdentifierChar(content[afterMarker]) ? afterMarker : -1;
	}

	// Index of the `{` in `<marker> = {`, or -1 when this occurrence is not an object-literal assignment (e.g. the
	// `Terrasoft.core.enums.X = Terrasoft.X` alias line, whose right-hand side is an identifier).
	private static int FindAssignedObjectLiteralStart(string content, int afterMarker) {
		int cursor = SkipWhitespace(content, afterMarker);
		if (cursor >= content.Length || content[cursor] != '=') {
			return -1;
		}
		cursor = SkipWhitespace(content, cursor + 1);
		return cursor < content.Length && content[cursor] == '{' ? cursor : -1;
	}

	private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

	private static int SkipWhitespace(string content, int index) {
		while (index < content.Length && char.IsWhiteSpace(content[index])) {
			index++;
		}
		return index;
	}

	// Plain brace depth count from the opening `{`. No comment/string handling is needed here because the caller
	// hands in the already-blanked text, so a stray brace inside a JSDoc comment (`{@link ...}`) or a quoted value
	// has already been replaced by a space. Returns -1 (truncated block) when the content ends before depth returns
	// to zero — which is also what an unterminated comment or string produces, since blanking swallows the rest.
	private static int FindMatchingBrace(string blankedContent, int openBraceIndex) {
		int depth = 0;
		for (int i = openBraceIndex; i < blankedContent.Length; i++) {
			char c = blankedContent[i];
			if (c == '{') {
				depth++;
			}
			else if (c == '}' && --depth == 0) {
				return i;
			}
		}
		return -1;
	}

	private static int SkipStringLiteral(string content, int quoteIndex) {
		char quote = content[quoteIndex];
		int i = quoteIndex + 1;
		while (i < content.Length && content[i] != quote) {
			i += content[i] == '\\' && i + 1 < content.Length ? 2 : 1;
		}
		return Math.Min(i + 1, content.Length); // past the closing quote, clamped on an unterminated literal
	}

	// Members only. The block handed in is already comment/string-blanked, so neither a JSDoc line NOR a quoted
	// description value can be mistaken for `NAME: number`. A regex-only comment strip over raw text would get this
	// wrong both ways: a colon+digit inside a quoted string (`DESC: "see LEGACY: 2 instead"`) would fabricate a
	// phantom member, and a `//` inside a URL-shaped string value (`A: "http://x", B: 2`) would delete a real one.
	// Non-numeric values are simply not matched by MemberRegex and are silently ignored, per the engine contract. A
	// duplicate name keeps its LAST occurrence, mirroring plain JS object-literal semantics.
	private static IReadOnlyDictionary<string, long> ParseMembers(string blankedObjectLiteralText) {
		var members = new Dictionary<string, long>(StringComparer.Ordinal);
		foreach (GroupCollection groups in MemberRegex.Matches(blankedObjectLiteralText).Select(match => match.Groups)) {
			string name = groups[1].Value;
			if (BlockedMemberNames.Contains(name)) {
				continue;
			}
			if (long.TryParse(groups[2].Value, out long value)) {
				members[name] = value;
			}
		}
		return members;
	}

	// Replaces every block comment, line comment, and string literal (quotes included) with spaces of the SAME
	// length, so byte offsets stay meaningful for diagnostics and MemberRegex's `\s*` around the value still matches.
	private static string BlankCommentsAndStringLiterals(string text) {
		var blanked = new StringBuilder(text.Length);
		int i = 0;
		while (i < text.Length) {
			int regionEnd = FindSkippableRegionEnd(text, i);
			if (regionEnd < 0) {
				blanked.Append(text[i]);
				i++;
				continue;
			}
			AppendBlanks(blanked, regionEnd - i);
			i = regionEnd;
		}
		return blanked.ToString();
	}

	// Index just past the comment or string literal that starts at <paramref name="i"/>, or -1 when nothing
	// skippable starts there. An unterminated comment/literal reports the end of the text, so the rest is blanked
	// (and any block still open is therefore reported as truncated by FindMatchingBrace).
	private static int FindSkippableRegionEnd(string text, int i) {
		char c = text[i];
		bool hasNext = i + 1 < text.Length;
		if (c == '/' && hasNext && text[i + 1] == '*') {
			int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
			return end < 0 ? text.Length : end + 2;
		}
		if (c == '/' && hasNext && text[i + 1] == '/') {
			int end = text.IndexOf('\n', i + 2);
			return end < 0 ? text.Length : end;
		}
		return c is '\'' or '"' ? SkipStringLiteral(text, i) : -1;
	}

	private static void AppendBlanks(StringBuilder sb, int count) {
		for (int i = 0; i < count; i++) {
			sb.Append(' ');
		}
	}
}

/// <summary>Resolves the <c>enumVocabulary</c> manifest block by reading the TARGET stand's own <c>sysenums.js</c>.</summary>
public interface IClassicEnumVocabularyResolver {

	/// <summary>
	/// Fetches and parses the target environment's own core enum declarations. Every failure mode (unreachable host,
	/// missing content-hash marker, missing/truncated <c>sysenums.js</c>, unparseable block) degrades to an empty or
	/// partial result plus an explanatory warning — this never throws for a reachability or parsing gap.
	/// </summary>
	ClassicEnumVocabularyParseResult Resolve();
}

/// <summary>
/// Reads the two unauthenticated, cacheable resources core itself serves — the login page (which names the current
/// content-hash) and <c>/core/&lt;hash&gt;/Terrasoft/core/enums/sysenums.js</c> — so <c>enumVocabulary</c> reflects
/// the STAND's actual platform build rather than a copy of the migration engine's pinned tables (which would make its
/// drift guard tautological). Deliberately bypasses <see cref="IApplicationClient"/>: that client's lazy-login path
/// has no timeout and does not apply to a bearer/OAuth session, while this fetch is unauthenticated by design and
/// must survive a cold stand's slow first hit without blocking the rest of the command on a login round-trip.
/// </summary>
internal sealed class ClassicEnumVocabularyResolver(
	EnvironmentSettings environmentSettings,
	IHttpClientFactory httpClientFactory,
	IClassicEnumVocabularySourceParser parser) : IClassicEnumVocabularyResolver {

	// Named HttpClient registered in BindingsModule.cs with its timeout, response-size cap, and redirect policy —
	// never mutated per-call (avoids InvalidOperationException / races on a shared HttpClient property, same
	// reasoning as the component-registry client).
	public const string HttpClientName = nameof(ClassicEnumVocabularyResolver);

	// Case-insensitive: nothing about the hash's own casing is a documented platform contract, only that it is
	// 32 hex characters, so matching only lowercase would silently omit enumVocabulary on a stand that happens to
	// serve an uppercase-hex marker. The optional leading '/0' is CAPTURED rather than merely tolerated, so a .NET
	// Framework login page that already spells its static root is echoed back verbatim instead of re-derived; the
	// IsNetCore split below is the fallback for a page that names '/core/...' with no root prefix.
	private static readonly Regex ContentHashPathRegex = new(
		"((?:/0)?)/core/([0-9a-fA-F]{32})/", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

	// Same runtime split the rest of the repository applies to unauthenticated UI paths
	// (EnvironmentRuntimeDetectionService.BuildUiMarkerUrl): .NET Core serves the login page at /Login/Login.html off
	// the site root, while .NET Framework serves it at /0/Login/NuiLogin.aspx behind the /0 application root. Getting
	// this wrong never fails loudly - it 404s and silently omits enumVocabulary on every stand of one runtime.
	private static string BuildLoginPageUrl(string baseUri, bool isNetCore) =>
		$"{baseUri}{(isNetCore ? "/Login/Login.html" : "/0/Login/NuiLogin.aspx")}";

	// Static-content root for the hashed /core/... tree: the bare site root on .NET Core, /0 on .NET Framework - the
	// same split SysImageUploader applies to static workspace roots.
	private static string BuildStaticRoot(string baseUri, bool isNetCore) => isNetCore ? baseUri : baseUri + "/0";

	/// <inheritdoc />
	public ClassicEnumVocabularyParseResult Resolve() {
		var warnings = new List<string>();
		var empty = new Dictionary<string, IReadOnlyDictionary<string, long>>(StringComparer.Ordinal);
		string baseUri = environmentSettings?.Uri?.TrimEnd('/');
		if (string.IsNullOrWhiteSpace(baseUri)) {
			warnings.Add("The environment URI is not configured; enumVocabulary omitted.");
			return new ClassicEnumVocabularyParseResult(empty, warnings);
		}
		bool isNetCore = environmentSettings.IsNetCore;
		string loginPage = TryGetString(BuildLoginPageUrl(baseUri, isNetCore), "the login page", warnings);
		if (loginPage == null) {
			return new ClassicEnumVocabularyParseResult(empty, warnings);
		}
		Match hashMatch = ContentHashPathRegex.Match(loginPage);
		if (!hashMatch.Success) {
			warnings.Add(
				"Could not find the '/core/<hash>/' content-hash marker on the login page; enumVocabulary omitted.");
			return new ClassicEnumVocabularyParseResult(empty, warnings);
		}
		string markedRoot = hashMatch.Groups[1].Value;
		string staticRoot = markedRoot.Length > 0 ? baseUri + markedRoot : BuildStaticRoot(baseUri, isNetCore);
		string sysEnumsUrl = $"{staticRoot}/core/{hashMatch.Groups[2].Value}/Terrasoft/core/enums/sysenums.js";
		string sysEnumsJs = TryGetString(sysEnumsUrl, "sysenums.js", warnings);
		if (sysEnumsJs == null) {
			return new ClassicEnumVocabularyParseResult(empty, warnings);
		}
		ClassicEnumVocabularyParseResult parsed = parser.Parse(sysEnumsJs);
		warnings.AddRange(parsed.Warnings);
		return new ClassicEnumVocabularyParseResult(parsed.Enums, warnings);
	}

	// Plain unauthenticated GET; a non-success status or transport failure degrades to null + a warning rather than
	// throwing, so a stand that cannot serve this best-effort input never fails the whole page-sources collection.
	// Timeout/redirect policy/response-size cap live on the named client's registration (BindingsModule.cs), not here.
	private string TryGetString(string url, string what, List<string> warnings) {
		try {
			using HttpClient client = httpClientFactory.CreateClient(HttpClientName);
			using HttpResponseMessage response = client.GetAsync(url).GetAwaiter().GetResult();
			if (!response.IsSuccessStatusCode) {
				warnings.Add(
					$"Could not fetch {what} ({(int)response.StatusCode} {response.ReasonPhrase}) from '{url}'; " +
					"enumVocabulary omitted.");
				return null;
			}
			return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
		}
		catch (Exception ex) {
			warnings.Add($"Could not fetch {what} from '{url}': {ex.Message}; enumVocabulary omitted.");
			return null;
		}
	}
}
