using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Clio.Common;

/// <summary>
/// Scrubs sensitive tokens out of an exception-derived message before it is surfaced to a caller -
/// the MCP client, and since issue #1333 the CLI output and the log as well (it is called from
/// <c>Clio.Common.ClassifyingDataProvider</c>, <c>SysSettingsManager</c> and
/// <c>ExceptionReadableMessageExtension</c>). The MCP tool result is copied verbatim into the model/host
/// transcript and is frequently logged or forwarded to a third-party LLM, so inner-most messages from the
/// data/HTTP/DB layers —
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
	// Carries the SAME \uXXXX guard as EmailRegex below, for the same reason and against the same input:
	// Redact also runs over already-serialized JSON (ClioRunTool.RedactFailureContent), where a quote is
	// written as " and "u0022" is a legal scheme prefix, so
	//   Request to "https://host/x" failed
	// matched from the "u" of the escape and left "\[redacted-uri]" - not a valid JSON escape, so the whole
	// tool response stopped parsing for the caller. The backslash is also excluded from the tail class, so a
	// match can no longer swallow the CLOSING escape either; both escapes survive and the value between them
	// is what gets replaced.
	// The guard is paired with a positive lookbehind for a COMPLETE escape, alternated with the ordinary ,
	// because the two must not be traded against each other: rejecting the start inside the escape is what
	// keeps the response parseable, and permitting a start right after the escape is what keeps the URI
	// redacted. Without the second half,  alone refuses the position (a hex digit and "h" are both word
	// characters, so there is no boundary between them) and the host ships in the clear - trading a
	// corrupted response for a leaked one, which this class's policy above rejects outright.
	// ')' is excluded from the tail: clio's own messages render endpoints as "(URL: <uri>)", and a class
	// that accepts ')' consumed the closing bracket too, leaving the reader an unbalanced
	// "(URL: [redacted-uri]". A URI whose path genuinely ends in ')' loses only that character, and it is
	// redacted either way.
	[GeneratedRegex(@"(?<!\\u?[0-9A-Fa-f]{0,3})(?:(?<=\\u[0-9A-Fa-f]{4})|\b)[a-zA-Z][a-zA-Z0-9+.\-]*://[^\s""'<>)\\]+", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
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
	// The session-cookie NAMES are listed individually because none of them contains any of the generic
	// key words above: a body reading ".ASPXAUTH=<value>; BPMCSRF=<value>; sessionid=<value>" matched
	// nothing at all and reached an agent transcript verbatim through the unparseable-body preview,
	// measured on a stand. A Creatio Forms-auth cookie IS the session - possessing it is possessing the
	// session - so it belongs in the same class as a password.
	//
	// The value alternation takes the QUOTED forms first: the bare class excludes a quote character, so
	// without them a quoted secret (password="s3cr3t") matches nothing at all and reaches the reader
	// verbatim — the pattern has to fail closed on the whole pair, not on the quote.
	//
	// THE KEY WORDS are declared once and interpolated into this rule and into the JSON property rule
	// below, so a key added here is covered in both spellings at the same time. Drift between the two
	// would be silent: the pair form would be scrubbed and the JSON form would ship in the clear, which
	// is the exact defect issue #1497 reports.
	//
	// They are split into TWO sets, because the JSON rule must not take all of them: a secret is a
	// secret in any spelling, while a connection-string PART is only a secret when it is written as part
	// of a connection string.
	private const string CredentialSecretKeys =
		@"password|pwd|pass|secret|token|api[_-]?key|client[_-]?secret|access[_-]?key|connection ?string|"
		+ @"authorization|auth|bearer|set-cookie|cookie|asp\.net_sessionid|aspxauth|bpmcsrf|jsessionid|"
		+ @"phpsessid|session[_-]?id|[xc]srf[_-]?token";

	// The connection-string parts, as this rule sees them in
	// "Data Source=db.internal;Initial Catalog=x;UID=sa". Kept OUT of the JSON key set except for the
	// four a JSON body genuinely carries (server / host / hostname / database): "uid" is also how Creatio
	// spells the identifier property "UId" on every schema, package and descriptor payload, so a JSON
	// rule carrying it would replace an ordinary identifier with a credential placeholder on practically
	// every diagnostic body. "user id", "data source" and "initial catalog" are excluded with it - they
	// do not occur as JSON property names in this product, so including them would buy nothing and risk
	// the same collision.
	private const string ConnectionStringPartKeys =
		@"data ?source|server|hostname|host|initial ?catalog|database|uid|user ?id";

	[GeneratedRegex(
		$@"\b({CredentialSecretKeys}|{ConnectionStringPartKeys})\b\s*[=:]\s*(?:""[^""]*""|'[^']*'|[^\s,;""']+)",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex CredentialPairRegex();

	// The SAME key words as they are spelled in a JSON property name - the SECRET set, plus the four
	// connection-string parts a JSON body genuinely carries. "uid", "user id", "data source" and
	// "initial catalog" are deliberately NOT here; the reason is on ConnectionStringPartKeys and it is a
	// real collision, not a theoretical one, so do not "fix" the drift by pasting the two sets together.
	//
	// The compound wrapper accepts separator-joined forms - "access_token", "db.password",
	// "x-csrf-token", "token_type" - and nothing else. That is the word-boundary rule
	// CredentialPairRegex already applies ('.' and '-' are word boundaries), widened by '_', which is a
	// WORD character and therefore NOT matched by the pair rule - an asymmetry accepted here because an
	// OAuth envelope is JSON and spells its keys that way. A trailing segment is accepted as well, so
	// "token_type" and "server-version" are redacted where the pair form leaves them alone; that
	// over-redaction is accepted, because this class prefers over-redacting to leaking. camelCase and
	// glued forms stay OUT, which is what keeps "passwordHint" and "tokenCount" untouched in JSON
	// exactly as "passwordHint=x" is untouched in key=value form.
	private const string JsonCredentialKeyPattern =
		$@"(?:[A-Za-z0-9]+[_.\-])*(?:{CredentialSecretKeys}|server|hostname|host|database)(?:[_.\-][A-Za-z0-9]+)*";

	// The three spellings one JSON quote can take in text this rule runs over: a literal quote, the
	// hand-written \" escape, and the \u0022 escape that System.Text.Json's DEFAULT encoder emits for a
	// nested serialized body. Measured, not assumed:
	// JsonSerializer.Serialize(new { body = "{\"password\":\"s3cr3t\"}" }) produces the \u0022 form, so a
	// rule that knew only the backslash-quote spelling would redact nothing at all on a real envelope.
	// The spelling is captured as <q> and the replacement writes the SAME spelling back, so one document
	// never ends up with two spellings mixed into it.
	private const string JsonQuoteSpellings = @"\\u0022|\\""|""";

	// The value's content class. A plain quote and a backslash are excluded, and a backslash escape is
	// consumed as one unit, so an escaped quote inside a PLAIN value ("a\"b") is taken with the value
	// instead of ending it. The leading lookahead is what stops the class at the closing spelling.
	private const string JsonStringContent = @"(?:(?!\k<q>)(?:[^""\\]|\\.))*";

	// The non-string JSON values a credential can be written as. Without them a secret written unquoted
	// (or a null/boolean flag under a secret key) would slip past the string alternative untouched.
	private const string JsonLiteralValue = @"null|true|false|-?\d+(?:\.\d+)?(?:[eE][+\-]?\d+)?";

	// A credential written as a JSON PROPERTY - "password":"s3cr3t" - which CredentialPairRegex cannot
	// reach at all: it requires \b(key)\b\s*[=:], and the key's own CLOSING quote sits between the key
	// and the colon, so the pair never matches. Measured on the shipped rules before this change:
	// Redact("{\"password\":\"s3cr3t\"}") returned that input verbatim (issue #1497, found while
	// finishing #1384). A JSON body is how a Creatio service answer, an environment-registration echo or
	// a serialized DTO carries a credential, and Redact also runs over already-serialized tool envelopes
	// (ClioRunTool.RedactFailureContent).
	//
	// The pair is rewritten in its JSON shape - "key":"[redacted]" - not in the key=value shape the pair
	// rule uses, so the surrounding payload stays parseable JSON instead of losing a quote.
	//
	// ACCEPTED LIMITS, all deliberate and none of them demonstrated on a production path (see the review
	// on PR #1504): an object or array value is NOT matched, because a regex cannot balance brackets; a
	// value sliced by an input cap before its closing quote is NOT matched; MIXED spellings inside one
	// document are NOT matched, because the closing quote is a backreference to the opening one; and at
	// the two ESCAPED spellings an inner escaped quote ends the value early, so its tail is not redacted
	// (System.Text.Json writes that inner quote as \\ plus the spelling, and the escape unit above
	// consumes only the first two backslashes). In every one of these cases the property is left exactly
	// as it was found, so the document still parses. This redactor is a last line of defence, not the
	// only one.
	//
	// NO \uXXXX guard here, unlike UriRegex/HostPortRegex/EmailRegex. Those carry one because they can
	// begin a match on the "u" INSIDE a \u0022 escape and swallow the backslash. This rule begins on a
	// complete quote spelling instead, so it cannot start mid-escape.
	[GeneratedRegex(
		$@"(?<q>{JsonQuoteSpellings})(?<key>{JsonCredentialKeyPattern})\k<q>\s*:\s*(?:\k<q>{JsonStringContent}\k<q>|{JsonLiteralValue})",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex JsonCredentialPropertyRegex();

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
	// Carries the SAME \uXXXX guard as UriRegex and EmailRegex: on already-serialized JSON,
	//   Could not connect to "db.internal:1433" - timeout
	// matched from the "u" of the opening escape ("u0022db" is a legal DNS label) and left
	// "\[redacted-uri]", which is not a valid JSON escape - the whole tool response then failed to parse.
	// Same pairing as UriRegex, and load-bearing for the same reason: the pre-existing (?<![\w:./@-]) guard
	// rejects a start preceded by the escape's last hex digit, so the guard ALONE would stop matching the
	// host altogether and leak it in the clear. Alternating it with a positive lookbehind for a complete
	// escape keeps both properties - the response parses AND the endpoint is replaced. Mid-token rejection
	// is unaffected: "xfoo.bar:80" has no complete escape before it, so it still takes the negative arm.
	[GeneratedRegex(
		@"(?<!\\u?[0-9A-Fa-f]{0,3})(?:(?<=\\u[0-9A-Fa-f]{4})|(?<![\w:./@-]))(?:\[[0-9A-Fa-f:]+\]|(?:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?\.)+[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?|\d{1,3}(?:\.\d{1,3}){3}):\d{1,5}\b",
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
	//
	// THE HOST IS AN ORDERED ALTERNATION of three shapes (issue #1380). The dotted shape alone required at
	// least one dot, so an address on a SINGLE-label host was not matched at all - "admin@localhost",
	// "svc@creatio-app", "user@INTRANET", "user@[10.0.0.5]". On-prem Creatio deployments are the population
	// whose authentication failures most often name such an address, so the rule missed exactly where it is
	// most needed. Order is load-bearing: the dotted branch is tried BEFORE the single-label one, so
	// "user@host.example.com" is consumed whole instead of stopping after "user@host" and leaving a
	// half-redacted ".example.com" tail in the clear.
	//   1. \[[^\]\s]{1,45}\] - anything bracketed, matched together with its brackets so no stray "]" is
	//      left behind. Brackets immediately after "@" are the authority-literal syntax and nothing else, so
	//      the class is deliberately loose rather than IP-shaped: it also covers the zone index .NET writes
	//      into socket errors ("user@[fe80::1%eth0]") and the RFC 5321 tagged form ("user@[IPv6:fe80::1]"),
	//      both of which an IP-only class left in the clear. The length bound keeps the branch from
	//      scanning a whole line after an unmatched "[".
	//   2. the pre-existing dotted host, whose FINAL label must be alphabetic and at least two characters
	//      (PR #1374 review): with a purely alphanumeric last label, "clio@8.0.1" and the "kit@1.2.3" tail
	//      of "@creatio/ui-kit@1.2.3" matched, so package-and-version - load-bearing diagnostic content in
	//      this product - was silently replaced by a placeholder indistinguishable from a real credential
	//      redaction. Unchanged here; widening must not reopen it.
	//   3. a single label: starts with a LETTER, is at least two characters long, and may carry digits or
	//      hyphens after the first character. The letter start is what keeps rule 2's narrowing intact -
	//      "@8.0.1", "@20" and "@1.2.3" cannot enter this branch at all. A trailing DIGIT is allowed,
	//      because on-prem host names routinely end in one ("user@WEB01", "svc@dev04").
	// Branch 3's trailing (?![A-Za-z0-9\-]) only forbids stopping part-way through a label. It deliberately
	// does NOT also forbid a following ".<label>". An earlier revision did, on the theory that it prevented
	// a partial match; it does not - for "user@host.example.c" branch 2 backtracks to "user@host.example"
	// and the ".c" is left over either way. What the extra arm actually did was turn a single-label host
	// followed by a short or numeric label into a TOTAL leak: "user@localhost.c", "user@node1.k8s" and
	// "admin@host.i18n" fail branch 2 (last label too short / not alphabetic) and were then refused by
	// branch 3 as well, so they shipped in the clear. Without it they become "[redacted].c" / "[redacted]
	// .k8s". The price is that a four-part version behind a letter-led head ("x@v4.1.1") is cut to
	// "[redacted].1.1", which this class's policy accepts: over-redacting is acceptable, leaking is not.
	// SHAPES THIS BRANCH NOW EATS, all accepted for the same reason - none of them can be told apart from a
	// UPN on a single-label host, which is exactly what must be redacted:
	//   "Contact@Account"-style identifiers, GitHub Actions refs ("checkout@v5", "setup-dotnet@v4", which
	//   live in .github/workflows and never reach this redactor), branch references
	//   ("ProcessBuilder@feature"), and npm dist-tags ("uuid@latest" - the npm prose around it still tells
	//   the operator what happened when it reads "uuid@[redacted]"). A case-sensitive carve-out for
	//   "latest" was tried and removed: it let "svc@latest" leak while redacting "svc@LATEST", and an
	//   on-prem host may well be named either.
	// "git@github.com:org/repo.git" was already matched by branch 2 and still becomes
	// "[redacted]:org/repo.git"; a digest specifier ("image@sha256:...") stops at the ":" the same way.
	// Also unchanged by this widening: "Prop@odata.mediaReadLink"-style OData annotations were already
	// eaten by the dotted branch before issue #1380 and still are.
	[GeneratedRegex(@"(?<!\\u?[0-9A-Fa-f]{0,3})[A-Za-z0-9._%+\-]+@(?:\[[^\]\s]{1,45}\]|[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?)*\.[A-Za-z]{2,}|[A-Za-z][A-Za-z0-9\-]*[A-Za-z0-9](?![A-Za-z0-9\-]))",
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
	/// Clamps <paramref name="text"/> to <paramref name="maxLength"/> WITHOUT stripping the closing
	/// untrusted-source-text marker off an already-fenced diagnostic.
	/// </summary>
	/// <remarks>
	/// An outer cap applied to a composed diagnostic is not the same problem as the cap inside
	/// <see cref="NeutralizeOrNull"/>: that one clamps the payload BEFORE appending the suffix, so the
	/// closer always survives. A caller that re-caps the finished message (<c>SysSettingsCommand.SafeDetail</c>
	/// re-capping <c>DataProviderFailureException.Message</c>, which is
	/// <c>ServerReportedFailureText.ComposeMessage</c>'s fenced output) cuts the closer off instead,
	/// leaving an opener with no terminator. Every field emitted after such a message - <c>error-category</c>,
	/// <c>cause</c>, <c>recovery-action</c>, <c>correlation-id</c> - then falls inside the fence for any
	/// reader keying on the markers, which is exactly what issue #1333's fence exists to prevent.
	/// <para>So the payload is cut and the closer re-appended, keeping the total within budget. Unfenced
	/// text takes the plain truncation, and a budget too small to hold the fence at all degrades to plain
	/// truncation rather than emitting a fence with no content.</para>
	/// </remarks>
	/// <param name="text">The possibly-fenced message to clamp.</param>
	/// <param name="maxLength">The largest result length allowed, ellipsis and closer included.</param>
	internal static string ClampPreservingFence(string text, int maxLength) {
		const string ellipsis = "...";
		if (string.IsNullOrEmpty(text) || text.Length <= maxLength) {
			return text;
		}
		int openerAt = text.IndexOf(UntrustedDiagnosticPrefix, StringComparison.Ordinal);
		bool isFenced = openerAt >= 0 && text.EndsWith(UntrustedDiagnosticSuffix, StringComparison.Ordinal);
		if (!isFenced) {
			return TextUtilities.TruncateWithoutSplittingSurrogatePair(text, maxLength) + ellipsis;
		}
		// The payload is what gets cut; the label before the opener and the closer after it are clio's own
		// framing and are kept whole. Budget = what is left once both are reserved.
		int framingLength = openerAt + UntrustedDiagnosticPrefix.Length
			+ UntrustedDiagnosticSuffix.Length + ellipsis.Length;
		int payloadBudget = maxLength - framingLength;
		if (payloadBudget <= 0) {
			return TextUtilities.TruncateWithoutSplittingSurrogatePair(text, maxLength) + ellipsis;
		}
		string payload = text[(openerAt + UntrustedDiagnosticPrefix.Length)..^UntrustedDiagnosticSuffix.Length];
		return text[..(openerAt + UntrustedDiagnosticPrefix.Length)]
			+ TextUtilities.TruncateWithoutSplittingSurrogatePair(payload, payloadBudget)
			+ ellipsis
			+ UntrustedDiagnosticSuffix;
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
	/// <remarks>
	/// A credential is recognized in BOTH spellings: the <c>key=value</c> / <c>key: value</c> pair
	/// (<c>CredentialPairRegex</c>) and the JSON property <c>"key":"value"</c> in its plain, its
	/// backslash-escaped and its <c>\u0022</c>-escaped form (issue #1497). The JSON rule runs FIRST in the
	/// chain; the ordering is load-bearing and the reason is stated at the call site inside this method.
	/// </remarks>
	/// <param name="text">The raw, possibly-sensitive error text.</param>
	/// <returns>The redacted text, safe to surface to the MCP client.</returns>
	public static string Redact(string? text) {
		if (string.IsNullOrEmpty(text)) {
			return string.Empty;
		}
		return ExecuteRegex(() => {
			// JSON-shaped credential properties FIRST - before EVERY other rule, not merely before
			// CredentialPairRegex. BearerTokenRegex (fifth in this chain) and CredentialPairRegex (last)
			// both take a bare value class that does not exclude a backslash, so on an escaped spelling
			// they eat the value's CLOSING escape and leave a lone backslash behind, which is not a valid
			// JSON escape - the caller then loses the whole tool response rather than one field. Measured,
			// not reasoned - moving this rule after them turns both cases of
			// Redact_ShouldKeepEscapedJsonParseable_WhenTheValueAlsoMatchesALaterRule red:
			//   \"authorization\":\"Bearer abc\"  -> BearerTokenRegex consumes "abc\" (its class stops
			//                                         at the quote but takes the backslash with it)
			//   \"cookie\":\"BPMCSRF=abc\"        -> CredentialPairRegex does the same to the pair
			// and the lone backslash left behind is not a valid JSON escape. Nothing is lost by going
			// first: a secret-keyed value is replaced wholesale either way, so the URI/host/path rules
			// have nothing left to find inside it.
			string result = JsonCredentialPropertyRegex().Replace(text, RedactJsonCredentialProperty);
			// URIs next: a scheme://user:pass@host authority must be removed whole before the narrower
			// path/credential passes run, so its embedded host/credentials never survive.
			result = UriRegex().Replace(result, RedactedUri);
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

	/// <summary>
	/// Rewrites a credential-keyed JSON property as <c>"key":"[redacted]"</c> in the SAME quote spelling
	/// the match was written in, so a document written with one spelling never gains a second one.
	/// </summary>
	private static string RedactJsonCredentialProperty(Match match) {
		string quote = match.Groups["q"].Value;
		return $"{quote}{match.Groups["key"].Value}{quote}:{quote}{RedactedValue}{quote}";
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
