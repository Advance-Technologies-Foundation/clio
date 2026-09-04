using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Clio.Common;

namespace Clio.Command.McpServer;

/// <summary>
/// Scrubs sensitive tokens out of an exception-derived message before it is surfaced to a caller -
/// the MCP client, and since issue #1333 the CLI output and the log as well (it is called from
/// <c>Clio.Common.ClassifyingDataProvider</c>, <c>SysSettingsManager</c> and
/// <c>ExceptionReadableMessageExtension</c>). The type still lives under <c>Command/McpServer</c>; moving
/// it to <c>Clio.Common</c> is a 90-file mechanical change deliberately left out of this pull request. The MCP tool result is copied verbatim into the model/host transcript and is frequently
/// logged or forwarded to a third-party LLM, so inner-most messages from the data/HTTP/DB layers —
/// which routinely carry absolute file paths, full request URIs (including the target host for
/// <c>*-by-credentials</c> flows), connection-string hosts, and credential values — must not leak.
/// <para>
/// Redaction is deliberately surgical, not wholesale: the human-readable reason an agent needs to
/// self-correct ("Environment 'Foo' not found", "package 'Bar' is missing") survives, while only
/// the dangerous tokens are replaced with stable placeholders. Patterns are conservative to avoid
/// mangling benign text — over-redacting a host header value is acceptable; leaking a path is not.
/// </para>
/// </summary>
internal static partial class SensitiveErrorTextRedactor {
	private const int RegexTimeoutMilliseconds = 1_000;

	private const string RedactedUri = "[redacted-uri]";
	private const string RedactedPath = "[redacted-path]";
	private const string RedactedValue = "[redacted]";

	// scheme://[user[:pass]@]host[:port][/path…] — also catches credentials embedded in the authority.
	[GeneratedRegex(@"\b[a-zA-Z][a-zA-Z0-9+.\-]*://[^\s""'<>]+", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex UriRegex();

	// Windows drive-rooted (C:\…) and UNC (\\host\share\…) absolute paths.
	[GeneratedRegex(@"(?:[A-Za-z]:\\|\\\\)[^\s""'<>|]*", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex WindowsPathRegex();

	// POSIX absolute paths under well-known home/system roots and common container/app roots, so
	// generic URL fragments (e.g. "/rest/CreatioApiGateway/…", "/DataService/…") and prose are left
	// intact. The root token must be followed by a path separator + at least one segment so a bare
	// "/app" word boundary in prose is not mistaken for a path.
	[GeneratedRegex(@"/(?:Users|home|root|var|etc|opt|usr|tmp|private|mnt|srv|Library|Applications|System|app|data|config)(?:/[^\s""'<>:]*)+", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex PosixPathRegex();

	// key=value / key: value pairs whose key denotes a secret or a connection-string host/db; the
	// key is kept (so the message still reads sensibly) and only the value is redacted. Includes
	// HTTP auth headers/cookies so a bearer token or session cookie surfaced under its header name
	// is scrubbed.
	//
	// The value alternation takes the QUOTED forms first: the bare class excludes a quote character, so
	// without them a quoted secret (password="s3cr3t") matches nothing at all and reaches the reader
	// verbatim — the pattern has to fail closed on the whole pair, not on the quote.
	[GeneratedRegex(
		@"\b(password|pwd|pass|secret|token|api[_-]?key|client[_-]?secret|access[_-]?key|connection ?string|data ?source|server|host|hostname|initial ?catalog|database|uid|user ?id|authorization|auth|bearer|cookie)\b\s*[=:]\s*(?:""[^""]*""|'[^']*'|[^\s,;""']+)",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex CredentialPairRegex();

	// "Bearer <token>" as it appears in an Authorization header value (not necessarily behind a
	// key=value pair). The token segment is replaced wholesale.
	[GeneratedRegex(@"\bBearer\s+[^\s,;""']+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex BearerTokenRegex();

	// JWT-shaped value: three base64url segments joined by dots, starting with the conventional
	// "eyJ" header prefix. Catches a raw token even when it is not preceded by a key or "Bearer ".
	[GeneratedRegex(@"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex JwtRegex();

	// scheme-less host:port — a DNS host or bracketed/raw IP literal followed by a numeric port, e.g.
	// "prod-db.internal:1433", "10.0.0.5:1433", "[fe80::1]:5432". Conservative: a port number is
	// required so plain "host" words and "key:value" prose are not touched. Bracketed IPv6 is matched
	// first so its inner colons are not split. The data/connection layer leaks endpoints in this
	// scheme-less shape that UriRegex (which requires "scheme://") never catches.
	[GeneratedRegex(
		@"(?<![\w:./@-])(?:\[[0-9A-Fa-f:]+\]|(?:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?\.)+[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?|\d{1,3}(?:\.\d{1,3}){3}):\d{1,5}\b",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex HostPortRegex();

	// A bare e-mail address. Not covered by CredentialPairRegex (no key= prefix) nor by UriRegex (no
	// scheme), so platform prose like "Validation failed for user john.doe@acme.com" carried a real
	// person's address into an MCP envelope and into any log the operator pastes into a ticket. The local
	// part deliberately excludes a leading dot so a sentence-ending "word.name@host" still matches whole.
	// The FINAL label must be alphabetic and at least two characters (PR #1374 review): with a purely
	// alphanumeric last label, "clio@8.0.1" and the "kit@1.2.3" tail of "@creatio/ui-kit@1.2.3" matched,
	// so package-and-version - load-bearing diagnostic content in this product - was silently replaced by
	// a placeholder indistinguishable from a real credential redaction.
	// The LEADING lookbehind exists because Redact also runs over text that is ALREADY serialized JSON
	// (ClioRunTool.RedactFailureContent scrubs a TextContentBlock whose whole body is the tool's JSON
	// envelope). System.Text.Json writes a quote as the escape \u0022, and "u0022" is a legal e-mail local
	// part, so without the guard the address in
	//   ... the inline literal \u0022name@firm.com\u0022 instead of ...
	// matched as "u0022name@firm.com" and left a dangling backslash - "\[redacted]\u0022" - which is not a
	// valid JSON escape, so the entire tool response stopped parsing for the caller (the sync-pages /
	// update-page inline-placeholder e2e tests). The lookbehind refuses a match that begins anywhere inside
	// a "\uXXXX" escape - including on the "u" itself, which is where the corrupting match actually started
	// - while a match that begins right AFTER the complete escape (the address) is still redacted.
	[GeneratedRegex(@"(?<!\\u?[0-9A-Fa-f]{0,3})[A-Za-z0-9._%+\-]+@[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?)*\.[A-Za-z]{2,}",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex EmailRegex();

	/// <summary>Redacts every entry of <paramref name="texts"/> under the same rules as <see cref="Redact"/>.</summary>
	/// <param name="texts">The raw, possibly-sensitive lines.</param>
	/// <returns>The redacted lines in input order, safe to surface to the MCP client.</returns>
	public static List<string> RedactAll(IEnumerable<string> texts) {
		return texts.Select(Redact).ToList();
	}

	// Any token that carries the fence name, bracketed or bare. A payload writing the bare token
	// (untrusted-source-text end) with no bracket left the delimiter word intact, and a reader - human or
	// model - that treats the words as the delimiter is exactly who the fence is for.
	// ORDER MATTERS (PR #1374 review): the BRACKETED alternative requires its opening "[", and the bare
	// alternative comes second. With an optional "[" in front of the greedy [^\]]* branch, a bare
	// "untrusted-source-text end" followed anywhere later by a "]" - routine in JSON fragments, array
	// indexes and SQL prose - matched everything in between and deleted diagnostic content the operator
	// needs. Requiring the bracket keeps that branch's blast radius inside a real bracketed token.
	[GeneratedRegex(@"\[\s*untrusted-source-text[^\]]*\]|untrusted-source-text\s*(?:begin|end)",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex FenceTokenRegex();

	/// <summary>Maximum length of a diagnostic composed from repository-controlled text.</summary>
	private const int UntrustedDiagnosticLimit = 300;

	/// <summary>
	/// Maximum input length the redaction rule chain is allowed to scan. Bounds the regex work on
	/// server-authored text before it reaches <see cref="Redact"/>; the OUTPUT is clamped to
	/// <see cref="UntrustedDiagnosticLimit"/> separately.
	/// </summary>
	private const int UntrustedInputLimit = UntrustedDiagnosticLimit * 8;

	/// <summary>Opens the fenced region that marks the diagnostic as observed data, not an instruction.</summary>
	private const string UntrustedDiagnosticPrefix = "[untrusted-source-text begin] ";

	/// <summary>Closes the fenced region so the payload cannot pass its own text off as the framing.</summary>
	private const string UntrustedDiagnosticSuffix = " [untrusted-source-text end]";

	/// <summary>
	/// Redacts <paramref name="text"/> and additionally neutralizes it as a carrier of instructions, returning
	/// <see langword="null"/> — never an empty string — when there is nothing to report.
	/// </summary>
	/// <remarks>
	/// Use this, not <see cref="Redact"/>, for any text that a third party can influence the CONTENT of rather
	/// than merely the values inside. A knowledge-source diagnostic is composed from exception messages that
	/// interpolate strings taken straight out of a remote repository — a duplicate JSON property name in
	/// <c>bundle-source.json</c>, an invalid resource item id — so a repository the operator does not control
	/// can choose the prose. That text then lands on <c>get-guidance</c>, which the server instructions make
	/// mandatory on every operation, in a server whose tool surface includes destructive tools: an injection
	/// channel into the first thing an agent reads. <see cref="Redact"/> alone does not close it, because it
	/// scrubs paths, URIs and credentials and has no opinion about prose, line breaks or length.
	/// <para>So: line breaks and control characters collapse to spaces (a multi-line instruction block cannot
	/// be forged), the result is clamped, and it is prefixed with a marker naming it as data. Returning
	/// <see langword="null"/> rather than <see cref="string.Empty"/> keeps a
	/// <c>JsonIgnoreCondition.WhenWritingNull</c> field omitted instead of emitting a diagnostic nobody wrote.
	/// </para>
	/// </remarks>
	/// <param name="text">The raw, possibly attacker-authored diagnostic.</param>
	/// <returns>The neutralized text, or <see langword="null"/> when there is nothing to report.</returns>
	public static string? RedactUntrustedOrNull(string? text) => NeutralizeOrNull(text, fenced: true);

	/// <summary>
	/// The CONSOLE rendering of the same untrusted text: scrubbed, flattened and length-capped, but
	/// <b>not</b> fenced. Returns <see langword="null"/> when there is nothing to report.
	/// </summary>
	/// <remarks>
	/// PR #1374 review. <see cref="RedactUntrustedOrNull"/> exists for a field a model reads, so it wraps
	/// its result in <c>[untrusted-source-text begin] … [untrusted-source-text end]</c>. A terminal is not
	/// a model's context window: on the console that fence has no audience and reads as clio
	/// malfunctioning - <c>clio set-syssetting</c> printed
	/// <c>SysSettings with code: UsrX is not updated. [untrusted-source-text begin] Column 'Name' is
	/// required. [untrusted-source-text end]</c> for an ordinary platform validation failure.
	/// <para>Everything else the neutralization does is still needed here: the text is server-authored, so
	/// secrets are still scrubbed, forged line breaks and bidi controls still collapse, a forged fence is
	/// still neutralized, and the length is still capped - a console line must not become a page.</para>
	/// </remarks>
	/// <param name="text">The raw, possibly attacker-authored diagnostic.</param>
	public static string? RedactForConsoleOrNull(string? text) => NeutralizeOrNull(text, fenced: false);

	private static string? NeutralizeOrNull(string? text, bool fenced) {
		if (string.IsNullOrWhiteSpace(text)) {
			return null;
		}
		text = UnwrapOuterFence(text);
		// CLAMPED BEFORE the rule chain, not only after (PR #1374 review). The input here is
		// server-authored and arbitrarily large - a ResponseStatus.Message can be a whole page - and Redact
		// runs eight or more backtracking scans over it, each with its own 1 s timeout. A
		// RegexMatchTimeoutException raised on a failure-REPORTING path has no handler above it, so an
		// oversized body turned a reportable provider failure into an unrelated crash. The bound is
		// generous rather than tight because redaction can lengthen text (a matched token becomes
		// "[redacted]"), and the result is clamped to UntrustedDiagnosticLimit at the end anyway -
		// so nothing that could have survived into those 300 characters is lost here.
		if (text.Length > UntrustedInputLimit) {
			text = text[..UntrustedInputLimit];
		}
		string flattened = FlattenDisplayHostileRuns(Redact(text));
		if (flattened.Length == 0) {
			return null;
		}
		// The payload cannot be allowed to close the fence and open a section of its own. Only the fence
		// tokens themselves are neutralized - stripping every bracket would also mangle this class's own
		// [redacted-path] / [redacted-uri] placeholders, which callers and tests read.
		// Case-INSENSITIVE and shape-based: an ordinal match on the exact lowercase token lets
		// "[UNTRUSTED-SOURCE-TEXT END]" through verbatim, and a reader that treats the delimiter
		// case-insensitively would then read everything after it as server-authored.
		flattened = ExecuteRegex(
			() => FenceTokenRegex().Replace(flattened, "(fence removed)"));
		if (flattened.Length > UntrustedDiagnosticLimit) {
			flattened = string.Concat(flattened.AsSpan(0, UntrustedDiagnosticLimit), "…");
		}
		return fenced
			? UntrustedDiagnosticPrefix + flattened + UntrustedDiagnosticSuffix
			: flattened;
	}

	/// <summary>
	/// Strips one already-present outer fence so <see cref="NeutralizeOrNull"/> stays idempotent.
	/// </summary>
	/// <remarks>
	/// Fencing is a representation, not proof of provenance: an untrusted source can forge both markers.
	/// Unwrapping and sanitizing the payload again is what stops a forged wrapper from bypassing redaction,
	/// flattening, token neutralization, or the length limit.
	/// </remarks>
	private static string UnwrapOuterFence(string text) {
		bool isFenced = text.StartsWith(UntrustedDiagnosticPrefix, StringComparison.Ordinal)
			&& text.EndsWith(UntrustedDiagnosticSuffix, StringComparison.Ordinal)
			&& text.Length >= UntrustedDiagnosticPrefix.Length + UntrustedDiagnosticSuffix.Length;
		return isFenced
			? text[UntrustedDiagnosticPrefix.Length..^UntrustedDiagnosticSuffix.Length]
			: text;
	}

	/// <summary>
	/// Replaces every display-hostile character with a space and collapses the resulting runs to one space.
	/// </summary>
	/// <remarks>
	/// Which characters are hostile, and why each category is included, is stated once in
	/// <see cref="TextUtilities.IsDisplayHostile"/> - <see cref="char.IsControl(char)"/> alone is not enough on
	/// three separate counts (forged line breaks via U+2028/U+2029, a lone surrogate that makes
	/// <c>System.Text.Json</c> throw, and bidi format overrides). Collapsing the runs is this class's own step.
	/// </remarks>
	private static string FlattenDisplayHostileRuns(string redacted) {
		StringBuilder collapsed = new(redacted.Length);
		bool lastWasSpace = false;
		foreach (char character in redacted) {
			char normalized = TextUtilities.IsDisplayHostile(character) ? ' ' : character;
			bool isSpace = normalized == ' ';
			if (isSpace && lastWasSpace) {
				continue;
			}
			lastWasSpace = isSpace;
			collapsed.Append(normalized);
		}
		return collapsed.ToString().Trim();
	}

	/// <summary>
	/// Returns <paramref name="text"/> with absolute file paths, URIs, and credential/connection-string
	/// values replaced by stable placeholders. Safe to call on already-clean messages (no match → returned
	/// unchanged) and on <see langword="null"/>/empty input (returns <see cref="string.Empty"/>).
	/// </summary>
	/// <param name="text">The raw, possibly-sensitive error text.</param>
	/// <returns>The redacted text, safe to surface to the MCP client.</returns>
	public static string Redact(string? text) {
		if (string.IsNullOrEmpty(text)) {
			return string.Empty;
		}
		return ExecuteRegex(() => {
			// URIs first: a scheme://user:pass@host authority must be removed whole before the narrower
			// path/credential passes run, so its embedded host/credentials never survive.
			string result = UriRegex().Replace(text, RedactedUri);
			// Tokens next, before host:port — a JWT/Bearer value can contain dots/segments that would
			// otherwise be partially nibbled by later passes; scrub them whole first.
			result = JwtRegex().Replace(result, RedactedValue);
			result = BearerTokenRegex().Replace(result, RedactedValue);
			// Scheme-less endpoints (host:port / ip:port) before the path pass so the host authority is
			// gone before any trailing path on the same token is considered.
			// Before host:port, whose domain pattern would otherwise nibble the address's own domain.
			result = EmailRegex().Replace(result, RedactedValue);
			result = HostPortRegex().Replace(result, RedactedUri);
			result = WindowsPathRegex().Replace(result, RedactedPath);
			result = PosixPathRegex().Replace(result, RedactedPath);
			result = CredentialPairRegex().Replace(result, match => $"{match.Groups[1].Value}={RedactedValue}");
			return result;
		});
	}

	internal static string ExecuteRegex(Func<string> operation) {
		try {
			return operation();
		}
		catch (RegexMatchTimeoutException) {
			return RedactedValue;
		}
	}
}
