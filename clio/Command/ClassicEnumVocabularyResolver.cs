namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Net.Http;
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

	// NAME: 123 (or NAME: -1) followed by a comma or the closing brace. Comments are stripped before this runs, so a
	// JSDoc line mentioning a colon and a digit cannot be mistaken for a member.
	private static readonly Regex MemberRegex = new(
		@"(?<![\w$])([A-Za-z_$][\w$]*)\s*:\s*(-?\d+)\s*(?=,|\}|$)",
		RegexOptions.Compiled, TimeSpan.FromSeconds(2));

	private static readonly Regex BlockCommentRegex = new(
		@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline, TimeSpan.FromSeconds(2));

	private static readonly Regex LineCommentRegex = new(
		@"//[^\r\n]*", RegexOptions.Compiled, TimeSpan.FromSeconds(2));

	/// <inheritdoc />
	public ClassicEnumVocabularyParseResult Parse(string sysEnumsJsContent) {
		var enums = new Dictionary<string, IReadOnlyDictionary<string, long>>(StringComparer.Ordinal);
		var warnings = new List<string>();
		if (string.IsNullOrWhiteSpace(sysEnumsJsContent)) {
			warnings.Add("sysenums.js content is empty; enumVocabulary omitted.");
			return new ClassicEnumVocabularyParseResult(enums, warnings);
		}
		foreach (string enumName in EnumNames) {
			string block = ExtractObjectLiteral(sysEnumsJsContent, enumName);
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
	// with a matching closing brace exists (missing member, or the file is truncated mid-block).
	private static string ExtractObjectLiteral(string content, string enumName) {
		string marker = "Terrasoft." + enumName;
		int searchFrom = 0;
		while (true) {
			int markerIndex = content.IndexOf(marker, searchFrom, StringComparison.Ordinal);
			if (markerIndex < 0) {
				return null;
			}
			int afterMarker = markerIndex + marker.Length;
			if (afterMarker < content.Length && IsIdentifierChar(content[afterMarker])) {
				// A longer identifier that merely starts with this name (e.g. a hypothetical ViewItemTypeExtra) —
				// keep scanning past it rather than mis-anchoring on a prefix match.
				searchFrom = afterMarker;
				continue;
			}
			int cursor = SkipWhitespace(content, afterMarker);
			if (cursor >= content.Length || content[cursor] != '=') {
				searchFrom = afterMarker;
				continue;
			}
			cursor = SkipWhitespace(content, cursor + 1);
			if (cursor >= content.Length || content[cursor] != '{') {
				// Not an object literal at this occurrence (e.g. the `Terrasoft.core.enums.X = Terrasoft.X` alias
				// line) — keep scanning for the real assignment.
				searchFrom = afterMarker;
				continue;
			}
			int closingBraceIndex = FindMatchingBrace(content, cursor);
			return closingBraceIndex < 0 ? null : content.Substring(cursor, closingBraceIndex - cursor + 1);
		}
	}

	private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

	private static int SkipWhitespace(string content, int index) {
		while (index < content.Length && char.IsWhiteSpace(content[index])) {
			index++;
		}
		return index;
	}

	// Depth-counts braces from the opening `{`, skipping over block/line comments and string literals so a stray
	// brace inside a JSDoc comment (e.g. `{@link ...}`) or a quoted value cannot desynchronize the count. Returns -1
	// (truncated block) when the content ends before depth returns to zero.
	private static int FindMatchingBrace(string content, int openBraceIndex) {
		int depth = 0;
		int i = openBraceIndex;
		while (i < content.Length) {
			char c = content[i];
			if (c == '/' && i + 1 < content.Length && content[i + 1] == '*') {
				int end = content.IndexOf("*/", i + 2, StringComparison.Ordinal);
				if (end < 0) {
					return -1;
				}
				i = end + 2;
				continue;
			}
			if (c == '/' && i + 1 < content.Length && content[i + 1] == '/') {
				int end = content.IndexOf('\n', i + 2);
				i = end < 0 ? content.Length : end + 1;
				continue;
			}
			if (c is '\'' or '"') {
				i = SkipStringLiteral(content, i);
				continue;
			}
			if (c == '{') {
				depth++;
			}
			else if (c == '}') {
				depth--;
				if (depth == 0) {
					return i;
				}
			}
			i++;
		}
		return -1;
	}

	private static int SkipStringLiteral(string content, int quoteIndex) {
		char quote = content[quoteIndex];
		int i = quoteIndex + 1;
		while (i < content.Length && content[i] != quote) {
			i += content[i] == '\\' && i + 1 < content.Length ? 2 : 1;
		}
		return i + 1; // past the closing quote (or past the end, on an unterminated literal)
	}

	// Members only — comments stripped first so a JSDoc line cannot be mistaken for `NAME: number`. Non-numeric
	// values (there are none in the real source, but nothing guarantees that forever) are simply not matched by
	// MemberRegex and are silently ignored, per the engine contract. A duplicate name keeps its LAST occurrence,
	// mirroring plain JS object-literal semantics.
	private static IReadOnlyDictionary<string, long> ParseMembers(string objectLiteralText) {
		string withoutComments = LineCommentRegex.Replace(BlockCommentRegex.Replace(objectLiteralText, string.Empty), string.Empty);
		var members = new Dictionary<string, long>(StringComparer.Ordinal);
		foreach (Match match in MemberRegex.Matches(withoutComments)) {
			string name = match.Groups[1].Value;
			if (BlockedMemberNames.Contains(name)) {
				continue;
			}
			if (long.TryParse(match.Groups[2].Value, out long value)) {
				members[name] = value;
			}
		}
		return members;
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

	// A cold stand's first hit measured 29-92s (ENG-95412 field notes); a probe-sized timeout would misreport a
	// merely-slow stand as unreachable.
	private const int RequestTimeoutMs = 120_000;

	private static readonly Regex ContentHashPathRegex = new(
		"/core/([0-9a-f]{32})/", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

	/// <inheritdoc />
	public ClassicEnumVocabularyParseResult Resolve() {
		var warnings = new List<string>();
		var empty = new Dictionary<string, IReadOnlyDictionary<string, long>>(StringComparer.Ordinal);
		string baseUri = environmentSettings?.Uri?.TrimEnd('/');
		if (string.IsNullOrWhiteSpace(baseUri)) {
			warnings.Add("The environment URI is not configured; enumVocabulary omitted.");
			return new ClassicEnumVocabularyParseResult(empty, warnings);
		}
		string loginPage = TryGetString($"{baseUri}/Login/NuiLogin.aspx", "the login page", warnings);
		if (loginPage == null) {
			return new ClassicEnumVocabularyParseResult(empty, warnings);
		}
		Match hashMatch = ContentHashPathRegex.Match(loginPage);
		if (!hashMatch.Success) {
			warnings.Add(
				"Could not find the '/core/<hash>/' content-hash marker on the login page; enumVocabulary omitted.");
			return new ClassicEnumVocabularyParseResult(empty, warnings);
		}
		string sysEnumsUrl = $"{baseUri}/core/{hashMatch.Groups[1].Value}/Terrasoft/core/enums/sysenums.js";
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
	private string TryGetString(string url, string what, List<string> warnings) {
		try {
			using HttpClient client = httpClientFactory.CreateClient(nameof(ClassicEnumVocabularyResolver));
			client.Timeout = TimeSpan.FromMilliseconds(RequestTimeoutMs);
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
