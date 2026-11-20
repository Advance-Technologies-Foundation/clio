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

	// ONE placeholder for every endpoint shape - a URI, a dotted or single-label host:port, an IPv6 address and a
	// private IPv4 address. "[redacted-uri]" is the one master already wrote for host:port (HostPortRegex), and
	// callers and tests read it; a second placeholder for the same kind of value ("[redacted-host]") would make
	// every reader handle two spellings of one thing.
	private const string RedactedUri = "[redacted-uri]";
	private const string RedactedPath = "[redacted-path]";
	private const string RedactedValue = "[redacted]";

	/// <summary>What an entry point returns when it fails closed - a regex timeout or a call past its budget - so a reader
	/// can tell "the redaction gave up" from "a value was redacted". <c>PlaceholderPieceRegex</c> reads it as a
	/// placeholder, so every later pass keeps it.</summary>
	internal const string RedactedTimeout = "[redacted-timeout]";

	// Where a match of the newer host, scheme and path rules may begin, so that none of them can start INSIDE a
	// JSON escape of an already-serialized envelope (ClioRunTool.RedactSerializedText) and leave a lone backslash
	// behind. It is the same pairing UriRegex and HostPortRegex carry, for the same reason, widened by one arm: a
	// start right after a complete \n, \r or \t escape is allowed too, because the default encoder writes a line
	// break that way and "\nredis:6379" must lose the host, not the "n". The ordinary arm refuses a start after a
	// word character or a backslash; each rule appends the extra characters its own shape must not follow.
	private const string EscapeAwareStartPrefix =
		@"(?<!\\u?[0-9A-Fa-f]{0,3})(?:(?<=\\u[0-9A-Fa-f]{4})|(?<=\\[nrt])|(?<![\w\\";

	private const string EscapeAwareStartSuffix = @"]))";

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
	// RFC 3986 userinfo has its own prefix: apostrophes and parentheses are legal BEFORE '@',
	// even though they delimit prose in the tail. Otherwise a partial password survives the match.
	// System.Text.Json escapes '&', apostrophe and '+'; accept those complete userinfo units too,
	// but never the surrounding quote escape (\u0022).
	[GeneratedRegex(@"(?<!\\u?[0-9A-Fa-f]{0,3})(?:(?<=\\u[0-9A-Fa-f]{4})|(?<=\\[nrt])|\b)[a-zA-Z][a-zA-Z0-9+.\-]*://"
		+ @"(?:(?:[A-Za-z0-9._~!$&'()*+,;=:\-]|%[0-9A-Fa-f]{2}|\\u002[67bB])*@)?[^\s""'<>)\\]+",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex UriRegex();

	// Windows drive-rooted (C:\…) and UNC (\\host\share\…) absolute paths.
	// A closing bracket (")", "}" and "]") ends the path ONLY when no further path segment follows it: a site writes
	// "see (C:\x\y.dll)" and "{/etc/x}", and a tail that took the bracket left the reader "([redacted-path]" with
	// nothing to close it - the same defect UriRegex fixed for "(URL: …)". A bracket followed by a separator and a
	// segment is part of a directory name ("C:\Users\jdoe(old)\secrets\db.config"), and the path runs on through
	// it: stopping there left "[redacted-path])\secrets\db.config" with the rest of the path in the clear. The
	// same continuation is in the drive-slash, POSIX and relative path rules.
	//
	// ESCAPE-AWARE, because Redact also runs over a serialized envelope (ClioRunTool.RedactSerializedText), where a
	// site message that quotes its own JSON carries "run \"CanManageSolution\"" as run \\\u0022CanManageSolution\\\u0022
	// (an escaped backslash, then an escaped quote). The UNC alternative used to start on that "\\" and run to the
	// next space, so the operation name - the site's whole reason - became "[redacted-path]" (found live through
	// clio-run update-page, 19 Nov). So:
	//   - a UNC start is two backslashes (four, once escaped) NOT after another backslash, followed by a host token
	//     and a further backslash ("\\fs01\share", "\\\\fs01\\share"); an escaped quote is not a host;
	//   - a drive takes "C:\" or its escaped spelling "C:\\" whole;
	//   - in the tail an escaped backslash pair is one unit, and a single backslash right before a quote ends the
	//     path - it escapes that quote - so the match can never take half of an escape and leave the JSON broken;
	//   - a closing bracket is taken only when a backslash (one, or an escaped pair) and a segment character follow,
	//     and that backslash starts no escape. After a bracket "\uXXXX" is an escaped quote (an envelope's
	//     "(C:\\x.dll)\u0022"), and "\n", "\r" or "\t" before a capital letter, another backslash, whitespace or the end
	//     is a line break or a tab ("see (C:\x\y.dll)\nNext line"); before a LOWER-CASE letter it is the next folder
	//     ("jdoe(old)\temp", "data[1]\nightly"), because when the two readings disagree the one that removes wins.
	[GeneratedRegex(
		@"(?:[A-Za-z]:(?:\\\\|\\(?!""))|(?<!\\)(?:\\\\){1,2}(?=[A-Za-z0-9_$][\w.$\-]*\\))"
		+ @"(?:\\\\|\\(?![""\\])|[^\s""'<>|)}\]\\]"
		+ @"|[)}\]]+(?=(?:\\\\|\\)(?![rnt](?![a-z])|u[0-9A-Fa-f]{4})[^\s""'<>|)}\]\\]))*",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex WindowsPathRegex();

	// A drive followed by a FORWARD slash ("C:/Secrets/db.config", "D:/data/x.mdf"), which .NET and the IIS
	// configuration both accept and which WindowsPathRegex (backslash only) never matched: the site's "D:/x/y"
	// shipped whole, and "D:/data/x.mdf" lost only its "/data/…" tail to PosixPathRegex, leaving "D:" behind.
	// Not after a word character, so "file:///C:/…" (UriRegex's) and a "key:/…" pair are not read as a drive,
	// and "C://" is left to UriRegex. The tail stops at a backslash so it cannot eat a JSON escape, and at a closing
	// bracket unless a "/" and a segment follow it (see WindowsPathRegex).
	[GeneratedRegex(EscapeAwareStartPrefix + EscapeAwareStartSuffix
		+ @"[A-Za-z]:/(?!/)(?:[^\s""'<>|)}\]\\]|[)}\]]+(?=/[^\s""'<>|)}\]\\/]))*",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex DriveSlashPathRegex();

	// POSIX absolute paths under well-known home/system roots and common container/app roots, so
	// generic URL fragments (e.g. "/rest/CreatioApiGateway/…", "/DataService/…") and prose are left
	// intact. The root token must be followed by a path separator + at least one segment so a bare
	// "/app" word boundary in prose is not mistaken for a path.
	// A closing bracket ends the path unless a "/" and a segment follow it, for the reasons given on
	// WindowsPathRegex. A backslash is taken in escaped units only: an escaped backslash pair ("\\") is one unit, and
	// a single backslash right before a quote or another backslash ends the path. Reading "\\" one backslash at a
	// time took the first half of the pair, so ".../var/www/app\\"" in an envelope became "[redacted-path]\"", an
	// escaped quote that left the JSON string unterminated.
	[GeneratedRegex(@"/(?:Users|home|root|var|etc|opt|usr|tmp|private|mnt|srv|Library|Applications|System|app|data|config)"
		+ @"(?:/(?:[^\s""'<>:)}\]\\]|\\\\|\\(?![""\\])|[)}\]]+(?=/[^\s""'<>:)}\]\\/]))*)+",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
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
	// THE TOKEN ALTERNATIVE CARRIES ITS OWN QUALIFIER LIST (PR #1493 review). A bare "token" behind \b
	// cannot match inside "accessToken": the boundary is between "s" and "T", both word characters, so
	// "accessToken=..." and "refreshToken=..." shipped in the clear while "access_token=..." was caught.
	// The qualifiers are enumerated rather than allowing any camelCase prefix, because a generic prefix
	// would pull "uid" into "SchemaUId" - the exact collision this class must not make, since UId is how
	// Creatio spells the identifier property on every schema and package payload.
	// "private_key" and "client_id" were in neither key set. A private key is the highest-value string
	// this redactor is ever handed; both forms ("private_key", "privateKey") are covered by [_-]? plus
	// IgnoreCase.
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
	// The token alternative carries its OWN qualifier list (PR #1493 review). A bare "token" behind \b
	// cannot match inside "accessToken" - the boundary falls between "s" and "T", both word characters -
	// so "accessToken=..." and "refreshToken=..." shipped in the clear while "access_token=..." was
	// caught. JsonCredentialKeyPattern has the same gap by a different route: its wrapper reaches a key
	// only through a "_", "." or "-", so the JSON spelling leaked too. Fixing it here fixes both rules,
	// since the JSON pattern is built from this constant.
	// The qualifiers are ENUMERATED rather than allowing any camelCase prefix: a generic prefix would
	// pull "uid" into "SchemaUId", and UId is how Creatio spells the identifier property on every schema
	// and package payload, so that collision would cost more diagnostic signal than the widening buys.
	// "private_key" and "client_id" were in neither key set at all. A private key is the highest-value
	// string this redactor is ever handed; [_-]? plus IgnoreCase covers both spellings of each.
	// The pass words, alone and in the compound spellings a site and a form write them in - "UserPassword",
	// "db_pass", "PassPhrase_backup", "passwordConfirmation", "PasswordNew", "my-passcode" - each of which shipped
	// in the clear, because the bare words behind \b cannot match inside a word. The prefixes and suffixes are
	// ENUMERATED, not "any word before or after": a generic suffix takes "passwordHint" (kept by
	// Redact_ShouldLeaveGluedJsonKey_Untouched) and "PasswordExpired", which are a form's own field names, not
	// secrets. "pw" is a secret in any spelling, so it joins the same set.
	private const string PassWordKeys =
		@"(?:(?:user|db|new|old|my|admin|app|smtp|proxy|current|connection)[_-]?)?"
		+ @"(?:password|passphrase|passcode|passwd|pass|pwd|pw)"
		+ @"(?:[_-]?(?:confirmation|confirm|repeat|new|old|backup|retry))?";

	// "credential(s)" is a key of its own, OUTSIDE CredentialSecretKeys, so the JSON rule can leave exactly one key
	// alone: clio's own read-only flag "constant-credentials-masked": true (WebServiceModels, describe-web-service),
	// which the compound wrapper would otherwise read as a credential and replace. Every other spelling goes in both
	// rules ("credentials", "db_credentials", "smtp.credentials", "credentials.masked", "credentials-masked-value").
	private const string CredentialWordKeys = @"credentials?";

	private const string CredentialSecretKeys =
		PassWordKeys + @"|secret|(?:access|refresh|id|auth|bearer|api|client|session|[xc]srf)?[_-]?token|"
		+ @"api[_-]?key|client[_-]?secret|client[_-]?id|private[_-]?key|access[_-]?key|connection ?string|"
		+ @"authorization|auth|bearer|set-cookie|cookie|asp\.net_sessionid|aspxauth|bpmcsrf|jsessionid|"
		+ @"phpsessid|session[_-]?id|[xc]srf[_-]?token";

	// Keys the PAIR rule takes but the JSON rule must not: a bare "key" is how Creatio names an ordinary member
	// of many payloads ({"key":"UsrStatus"}), and "API key: …"/"Secret key: …" are prose spellings no JSON
	// property has. A bare "key" or "token" written with ":" keeps an identifier or a constraint name as its
	// value (see IsIdentifierPairValue), so "Key: UsrStatus" and "Duplicate key: FK_Contact_Account" stay.
	private const string PairOnlyKeys = @"key|(?:api|secret|private|client|access)[ \t]+key";

	// The keys whose ":"-written value is kept when it is an identifier or a constraint name. clio's own prose
	// writes these words before a colon ("localization key: author", "cached token: yes", "a restart started with
	// credentials: check …"), and so does the site ("Key: UsrStatus", "token: expired"); a secret is not shaped
	// like an identifier. Written with "=", the value always goes.
	[GeneratedRegex(@"^(?:key|token|credentials?|(?:api|secret|private|client|access)[ \t]+key)$",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex IdentifierValuedKeyRegex();

	// An identifier as Creatio and clio spell one - "UsrStatus", "UsrOrder_FormPage", "expired", "Id" - matched
	// CASE-SENSITIVELY, so "x7Kq2mVb9Lp" or "abcdefghXYZ" (a run of capitals, a digit) is not one; or a
	// constraint or index name ("FK_Contact_Account"), which a duplicate-key message names as the reason.
	[GeneratedRegex(@"^(?:(?:[A-Z]?[a-z]+(?:[A-Z][a-z]+)*)(?:_[A-Z]?[a-z]+(?:[A-Z][a-z]+)*)*|(?i:PK|FK|IX|UQ|CK|DF)_\w+)$",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex IdentifierPairValueRegex();

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

	// The scheme words an Authorization value starts with. A pair whose value is one of them followed by the
	// credential's placeholder (the Bearer and scheme rules ran first) is left as it is, so the reader keeps
	// "Authorization: Bearer [redacted]"; a scheme word followed by anything else takes that token with it
	// ("password=Basic dXNl…", "password=Negotiate YII"), because the scheme alone is not the secret.
	private const string SchemeWords = "Bearer|Basic|NTLM|Negotiate|Digest";

	// The value of a credential pair, in the order the alternatives are tried:
	//   1. a scheme word and an already-redacted credential, kept whole (group "kept"), in any quote spelling
	//      the scheme rules keep around it (none, ", ', \", \u0022, \u0027, &quot;);
	//   2. a scheme word and the next token, taking a backslash only as a whole escape unit (see 6);
	//   3. a double- or single-quoted value that honours \" and \' escapes - EXCEPT where that quote is followed by
	//      a space, ",", ";", "&", a closing bracket or the end, which is how a site writes a value ending in a
	//      backslash ("Data Source=\"C:\data\"; Password=…"): there the backslash is literal and the quote closes.
	//      Without escapes "password=\"he said \\\"no\\\"\"" kept everything after the first escaped quote;
	//   4. an UNCLOSED quote, to the end of its line - never past it, and never across a JSON bracket, so on a
	//      serialized envelope (one line, structural brackets) it cannot run to the end of the document;
	//   5. a value quoted with escaped quotes (\"…\"), which is how a pair reads inside a JSON string written with
	//      the relaxed encoder; it is taken whole, quotes included, so the string stays closed;
	//   6. a bare value, which stops at "&" when a new "name=" follows ("client_secret=s3cr3t&api_key=abc" used
	//      to lose all five key names into one placeholder), "&amp;" included, and takes a backslash only in
	//      escaped units: an escaped backslash pair ("\\") is one unit, and a single backslash right before a quote
	//      or another backslash ends the value. Taking the backslash of an escaped quote left that quote unescaped
	//      and closed the JSON string early; reading "\\" one backslash at a time took the first half of the pair,
	//      so {"error":"password=abc\\"} became {"error":"password=[redacted]\"} - an unterminated string.
	private const string PairValueBackslashUnit = @"\\\\|\\(?![""\\])";

	private const string CredentialPairValue =
		$@"(?<kept>(?:{SchemeWords})\s+(?<sq>\\u0022|\\u0027|\\""|&quot;|[""']?)\[redacted\]\k<sq>)"
		+ $@"|(?:{SchemeWords})\s+(?:[^\s,;""'\\]|{PairValueBackslashUnit})+"
		+ @"|""(?:[^""\\\r\n]|\\""(?![\s,;&)\]}]|\z)|\\[^""\r\n]|\\(?=""))*"""
		+ @"|'(?:[^'\\\r\n]|\\'(?![\s,;&)\]}]|\z)|\\[^'\r\n]|\\(?='))*'"
		+ @"|""[^""\r\n{}\[\]]+(?=[\r\n]|\z)"
		+ @"|'[^'\r\n{}\[\]]+(?=[\r\n]|\z)"
		+ @"|\\""(?:[^""\\\r\n]|\\[^""\r\n])*\\"""
		+ $@"|(?:[^\s,;""'&\\]|{PairValueBackslashUnit}|&(?!(?:amp;)?[A-Za-z_][\w.\-]*=))+";

	// A key may be written in single quotes, as a Python- or JavaScript-shaped dict does ({'Password': 'x'}); the
	// pair rule could not reach it, because the key's closing quote sits where it needs its "=" or ":".
	// A key right after a "\n", "\r" or "\t" escape counts as starting a word: in a serialized envelope a line
	// break is written that way, and "\nPassword=hunter2" had no word boundary before the key, so it shipped.
	[GeneratedRegex(
		$@"(?<kq>')?(?:(?<=\\u[0-9A-Fa-f]{{4}})|(?<=\\[nrt])|\b)(?<key>{CredentialSecretKeys}|{CredentialWordKeys}|{ConnectionStringPartKeys}|{PairOnlyKeys})\b(?(kq)')"
		+ $@"(?<sep>\s*[=:]\s*)(?<value>{CredentialPairValue})",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex CredentialPairRegex();

	// A pair inside a ";"-separated list - a connection string - whose value runs to the next ";" or the end of
	// the line, spaces included: "Password=p@ss w0rd;" kept " w0rd" and "Data Source=my server;" kept " server".
	// Only a list qualifies (RedactCredentialListPair checks it): the pair follows a ";", or the text starts with
	// it and a "; name=" follows. A quoted value is left to CredentialPairRegex. On a serialized envelope the
	// value cannot end inside an escape: it ends only before a ";" or the end of a line, never before a quote.
	// The same value shape serves the site-only list rule (SiteKeyListPairRegex).
	private const string ListPairValue =
		@"(?<value>[^;\r\n""'\s](?:[^;\r\n""']*[^;\r\n""'\s])?)(?=[ \t]*(?:;|\r|\n|\z))";

	[GeneratedRegex(
		$@"(?<=(?:^|;)[ \t]*)(?<key>{CredentialSecretKeys}|{CredentialWordKeys}|{ConnectionStringPartKeys})\b"
		+ $@"(?<sep>[ \t]*=[ \t]*){ListPairValue}",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex CredentialListPairRegex();

	// What has to follow the FIRST pair of a list for it to count as one: "; name=".
	[GeneratedRegex(@"\G[ \t]*;[ \t]*[A-Za-z][A-Za-z0-9_ ]{0,40}=", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex ListContinuationRegex();

	// THE BEARER FLOOR (rule 1, Bearer). Master's rule was "\bBearer\s+[^\s,;\"']+" with IgnoreCase: it removed the
	// whole run after the word, whatever it was. That is the floor - nothing it removed may reach a caller now - and
	// SensitiveErrorTextRedactorSiteTextTests proves it as a property over the fixture, a corpus of token shapes and
	// master's own cases. This rule starts wherever master's did (a word boundary; also right after a JSON escape),
	// takes the same gap ("\s+", a no-break space included) and at least the same run, and keeps only the documented
	// trades (BearerTradeRegex): the word "token" ("The bearer token expired", "Invalid Bearer token."), a challenge
	// parameter ("WWW-Authenticate: Bearer realm=…") and clio's own placeholder. The scheme word stays.
	// The run is master's [^\s,;"']+ with three changes, none of which lets a credential character through:
	//   - a backslash continues the run, except where it starts an escaped quote (\", \u0022, \u0027), which ends
	//     it as the closing quote; an escaped backslash pair is one unit, so a run never ends inside an escape;
	//   - "&amp;" is part of the run, and "&quot;" is a quote around it, as an HTML-escaped body writes them;
	//   - the quote around the credential may be written ", ', \", \u0022, \u0027 or &quot; and stays.
	// Trailing sentence punctuation is peeled off the run by RedactBearerCredential and kept.
	private const string CredentialRunQuote = @"(?<q>\\u0022|\\u0027|\\""|&quot;|[""'])?";

	private const string CredentialRun =
		@"(?<cred>(?:[^\s,;""'\\&]|\\\\|\\(?!u00(?:22|27)|[""'])|&amp;|&(?!quot;))+)(?<close>\k<q>)?";

	[GeneratedRegex(
		@"(?:(?<=\\u[0-9A-Fa-f]{4})|(?<=\\[nrt])|\b)(?<scheme>Bearer)(?<gap>\s+)" + CredentialRunQuote + CredentialRun,
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex BearerCredentialRegex();

	// "Bearer" glued to the word before it ("NextBearer abcdefgh.ijklmnop"), where neither master's "\bBearer" nor the
	// rule above starts. Only a credential-shaped run goes: a sentence word after a name that ends in "Bearer" stays.
	[GeneratedRegex(@"(?<=[a-z])(?<scheme>Bearer)(?<gap>[ \t]+)" + CredentialRunQuote + CredentialRun,
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex GluedBearerCredentialRegex();

	// The documented trades after Bearer, read on the run without its trailing punctuation.
	[GeneratedRegex(@"^(?i:tokens?)(?:-(?:based|enabled|disabled|only|protected|secured|style|like|type|related|specific|level|scoped|bound|aware))?$",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex BearerTradeRegex();

	// Scheme credential (rule 1, Basic, NTLM and Negotiate), in any letter case ("ntlm", "NEGOTIATE"). Master had no
	// rule for these, so there is no floor to keep; the line is drawn by WHERE the word stands:
	//   - in a HEADER POSITION - right after "Authorization:", "Proxy-Authorization:", "WWW-Authenticate:",
	//     "Proxy-Authenticate:", a quoted credential key ("\"token\": ") or a bare one before "=" ("password=");
	//     see HeaderPositionRegex - only a challenge parameter ("WWW-Authenticate: Basic realm=…"), a word of the
	//     closed list HeaderProseWordRegex or a placeholder stays: "Authorization: Basic configuration" loses the word;
	//     "auth: Basic mode is off" is a sentence, not a header;
	//   - ELSEWHERE, in a sentence, a prose word stays - all lower case or one leading capital then lower case,
	//     optionally "-"-joined ("Basic configuration is missing", "NTLM handshake failed", "Basic auth-enabled") -
	//     and so do an acronym (with "/n", "/n.n", "/ACRONYM", "vn" or a dotted version of up to four short parts:
	//     "SSL/TLS", "HTTP/1.1", "TLSv1.2"), a version of up to four short parts ("8.1.0.5"), a factor word ("2FA")
	//     and a CamelCase word with ONE inner capital and no digit ("OAuth"). Anything else goes: an inner capital
	//     with a digit ("UsrPage1"), two inner capitals ("AdminApiKey"), a long dotted run, base64 ("dXNlcjpwYXNz"
	//     is all letters but has inner capitals), "+", "/" or "=".
	// The run, its quote spellings and the trailing punctuation are the Bearer rule's.
	[GeneratedRegex(
		EscapeAwareStartPrefix + @"\-" + EscapeAwareStartSuffix + @"(?<scheme>Basic|NTLM|Negotiate)(?<gap>\s+)"
		+ CredentialRunQuote + CredentialRun,
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex SchemeCredentialRegex();

	// What a header position looks like just before the scheme word, and nothing else:
	//   - a header name - Authorization, Proxy-Authorization, WWW-Authenticate or Proxy-Authenticate - and its ":" or
	//     "=", the name optionally quoted ("Authorization: Basic …", "\"Authorization\": \"Basic …");
	//   - a credential key of the pair rule's list (CredentialSecretKeys and "credential(s)": password, pwd, secret,
	//     the token family, auth, api_key, client_secret, cookie, …) written as a key: QUOTED before its ":" or "=" -
	//     a JSON or dict key ("\"token\": \"Basic …") - or bare before an "=" (a pair: "password=Basic …");
	//   - a "key" or "auth" word JOINED by "-" or "_" to the word before it, a header name or a compound key, before
	//     its ":" or "=" ("X-Auth: Basic …", "X_Key: …", "Api-Key: …", "Proxy-Auth: …").
	// A key word with a colon in a sentence is not one: "Primary key: Basic info", "auth: Basic mode is off" and
	// "The secret: Negotiate later" keep the prose word after the scheme word.
	[GeneratedRegex(
		$@"(?:(?<![\w\-])(?:""|'|\\""|\\u0022)?(?:(?:proxy-)?authori[sz]ation|(?:www|proxy)-authenticate)(?:""|'|\\""|\\u0022)?[ \t]*[:=]"
		+ $@"|(?<![\w.\-])(?:""|'|\\""|\\u0022)(?:{CredentialSecretKeys}|{CredentialWordKeys})(?:""|'|\\""|\\u0022)[ \t]*[:=]"
		+ $@"|(?<![\w.\-])(?:{CredentialSecretKeys}|{CredentialWordKeys})[ \t]*="
		+ $@"|(?<![\w\-])[A-Za-z]\w*(?:[\-_][A-Za-z0-9]+)*[\-_](?:key|auth)(?:""|'|\\""|\\u0022)?[ \t]*[:=])"
		+ @"[ \t]*(?:""|'|\\""|\\u0022)?[ \t]*$",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex HeaderPositionRegex();

	// The words that may follow a scheme word in a header position, in any letter case.
	[GeneratedRegex(@"^(?i:token|tokens|realm|challenge|negotiate|negotiation|required|missing|invalid|expired|rejected|refused|failed|none|null)$",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex HeaderProseWordRegex();

	// What stays after Basic, NTLM or Negotiate in a sentence: see SchemeCredentialRegex.
	[GeneratedRegex(
		@"^(?:[A-Za-z][a-z]*(?:-[A-Za-z][a-z]*)*"
		+ @"|[A-Z]{2,5}(?:/[A-Z]{2,5})*(?:/\d(?:\.\d)?|v?\d{1,2}(?:\.\d{1,2}){0,3})?"
		+ @"|v?\d{1,4}(?:\.\d{1,4}){1,3}|v\d{1,2}|\d[A-Z]{1,3}|[A-Z][a-z]*[A-Z][a-z]+)$",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex ProseAfterSchemeWordRegex();

	// The only pair keys whose colon may stand in a sentence before a scheme word and a prose word.
	[GeneratedRegex(@"^(?:key|auth)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex ProseKeyRegex();

	// A pair value that is a scheme word and one more word, read by RedactCredentialPair.
	[GeneratedRegex($@"^(?<scheme>{SchemeWords})\s+(?<word>\S+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
		RegexTimeoutMilliseconds)]
	private static partial Regex SchemeWordAndKeptWordRegex();

	// Sentence punctuation that may close a credential run and is not part of it ("Invalid Bearer token.",
	// "(Authorization: Bearer x7Kq)").
	[GeneratedRegex(@"[.)\]}:!?]+$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex TrailingSentencePunctuationRegex();

	// The run must be the placeholder alone: "[redacted]%2Fxyz" - a JWT cut at a "%" by JwtRegex - still goes whole.
	[GeneratedRegex(@"^\[redacted(?:-[a-z]+)?\][.)\]}:!?]*$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex PlaceholderRunRegex();

	// A challenge parameter after a scheme ("WWW-Authenticate: Bearer realm=\"api\", error=…") is not a
	// credential: it is what the site asks for.
	[GeneratedRegex(@"^(?:realm|scope|error|error_description|error_uri|charset)=",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex ChallengeParameterRegex();

	// A Digest authorization: the parameter list after "Digest". Replaced only when it carries what a CLIENT
	// sends (username=, response=, cnonce=); a server's challenge ("WWW-Authenticate: Digest realm=…, nonce=…")
	// and prose ("Digest authentication is off") stay. An unquoted value excludes a backslash, so on an escaped
	// envelope the list is not matched rather than cut inside an escape.
	[GeneratedRegex(
		EscapeAwareStartPrefix + @"\-" + EscapeAwareStartSuffix
		+ @"(?<scheme>Digest)(?<gap>[ \t]+)"
		+ @"(?<params>[A-Za-z][\w\-]*[ \t]*=[ \t]*(?:""[^""\r\n]*""|[^\s,""\\]+)"
		+ @"(?:[ \t]*,[ \t]*[A-Za-z][\w\-]*[ \t]*=[ \t]*(?:""[^""\r\n]*""|[^\s,""\\]+))*)",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex DigestCredentialRegex();

	[GeneratedRegex(@"(?:^|[\s,])(?:username|response|cnonce)[ \t]*=",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex DigestClientParameterRegex();

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
	// "credentials" is taken in every spelling; the one exact key clio's own flag uses is excluded where the rule
	// starts a key (JsonCredentialPropertyRegex) - see CredentialWordKeys.
	private const string JsonCredentialKeyPattern =
		$@"(?:[A-Za-z0-9]+[_.\-])*(?:{CredentialSecretKeys}|server|hostname|host|database|{CredentialWordKeys})"
		+ @"(?:[_.\-][A-Za-z0-9]+)*";

	// The exact key of clio's own read-only flag, which the JSON rule leaves alone - in this letter case only, and only
	// when its value is true or false, so "Constant-Credentials-Masked" and a string under the key are credentials.
	private const string OwnMaskedFlagKey = "constant-credentials-masked";

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
		$@"(?<q>{JsonQuoteSpellings})(?!(?-i:{OwnMaskedFlagKey})\k<q>\s*:\s*(?:true|false)\b)(?<key>{JsonCredentialKeyPattern})\k<q>\s*:\s*(?:\k<q>(?<value>{JsonStringContent})\k<q>|(?<literal>{JsonLiteralValue}))",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex JsonCredentialPropertyRegex();

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
	// The trailing (?!:\d) is the chain guard: "UsrOrderPage.js:42:13" is a file, a line and a column, not a
	// host and a port, and reading it as one left "Error in [redacted-uri]:13".
	[GeneratedRegex(
		@"(?<!\\u?[0-9A-Fa-f]{0,3})(?:(?<=\\u[0-9A-Fa-f]{4})|(?<=\\[nrt])|(?<![\w:./@-]))(?:\[[0-9A-Fa-f:]+\]|(?:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?\.)+[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?|\d{1,3}(?:\.\d{1,3}){3}):\d{1,5}(?!\d)(?!:\d)",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex HostPortRegex();

	// An IPv6 address, bracketed (with an optional port) or bare (with an optional zone), in the full form, the
	// compressed "::" form, or with an IPv4 tail. Times ("10:45:30:123"), a lone "::", "[1:2]" and a five-hex
	// group ("abcde:1:2:3") are not addresses; IsIpv6Address also requires a digit, so "Bad::Cafe"-style text
	// stays. Not after a word character, ":" or ".", so it never starts in the middle of another token.
	private const string Ipv6Group = "[0-9A-Fa-f]{1,4}";
	private const string Ipv6Body =
		$@"(?:(?:{Ipv6Group}:){{6}}\d{{1,3}}(?:\.\d{{1,3}}){{3}}"
		+ $@"|(?:{Ipv6Group}(?::{Ipv6Group}){{0,4}})?::(?:{Ipv6Group}:){{0,4}}\d{{1,3}}(?:\.\d{{1,3}}){{3}}"
		+ $@"|(?:{Ipv6Group}:){{7}}{Ipv6Group}"
		+ $@"|(?:{Ipv6Group}(?::{Ipv6Group}){{0,6}})?::(?:{Ipv6Group}(?::{Ipv6Group}){{0,6}})?)";

	[GeneratedRegex(
		EscapeAwareStartPrefix + ":." + EscapeAwareStartSuffix
		+ $@"(?:\[{Ipv6Body}(?:%[A-Za-z0-9._~\-]+)?\](?::\d{{1,5}})?|{Ipv6Body}(?:%[A-Za-z0-9._~\-]+)?)(?![\w:]|\.\d)",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex Ipv6Regex();

	// A single-label host and its port ("redis:6379", "SQLPROD01:1433", "localhost:5000"), which HostPortRegex
	// (dotted hosts and IPv4 only) never matched. A regex cannot tell "redis:6379" from "error:404" or "Line:42",
	// so IsSingleLabelHostPort decides from the label and the port; the chain guard keeps "x:1:2" and times out.
	[GeneratedRegex(
		EscapeAwareStartPrefix + ":./@\\-" + EscapeAwareStartSuffix
		+ @"(?<label>[A-Za-z](?:[A-Za-z0-9\-]{0,61}[A-Za-z0-9])?):(?<port>\d{2,5})(?![\w:]|\.\d)",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex SingleLabelHostPortRegex();

	// A private, loopback or link-local IPv4 address without a port (10/8, 127/8, 172.16/12, 192.168/16,
	// 169.254/16). ONLY those ranges: a regex cannot tell "10.0.0.5" from "8.1.0.5", and a four-part version is
	// load-bearing in clio's own output ("cliogate 2.0.0.32 is required", "Creatio 8.1.0.5 detected" - the
	// PR #1374 decision). The private ranges are what a site's own refusal names (its database, its cache), and a
	// version word before the address ("version 10.0.0.1") keeps it too (IsVersionContext). Not after "@": that
	// is EmailRegex's known gap, pinned by Redact_ShouldLeaveABareIpv4HostAlone_KnownGap.
	// It also starts right after letters ("retry10.0.3.17", "ZqDb10.0.3.17"), where a corpus or a site glued it to a word;
	// "v10.0.0.1" is still a version (IsVersionContext).
	[GeneratedRegex(
		@"(?<!\\u?[0-9A-Fa-f]{0,3})(?:(?<=\\u[0-9A-Fa-f]{4})|(?<=\\[nrt])|(?<=[A-Za-z_])|(?<![\w\\.@\-]))"
		+ @"(?:(?:10|127)(?:\.\d{1,3}){3}|172\.(?:1[6-9]|2\d|3[01])(?:\.\d{1,3}){2}|192\.168(?:\.\d{1,3}){2}"
		+ @"|169\.254(?:\.\d{1,3}){2})(?![\w]|\.\d)",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex PrivateIpv4Regex();

	// The words that make a dotted quad a version rather than an address, read just before it. "version" and "ver"
	// may be followed by a "=" or ":", as an assembly name and a key list write them ("Version=10.0.0.0,
	// Culture=neutral", "version: 10.0.0.5"); the other words only by spaces ("Creatio 8.1.0.5", "build 10.0.0.1"),
	// because "Creatio: 10.0.3.17", "release: …", "v=…" and "build:…" name an address, not a version.
	[GeneratedRegex(@"(?:\b(?:version|ver)[ \t]*[=:]?|\b(?:build|release|creatio|clio|cliogate|package|v))[ \t]*$",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex VersionContextRegex();

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
	//   2b. a dotted-quad IPv4 literal THAT IS FOLLOWED BY A PORT (PR #1493 review). Branch 2 refuses a
	//      bare quad - its final label must be alphabetic - and branch 3 refuses it too, since that one
	//      must start with a letter. So "sa@10.0.0.5:1433" was matched by NOTHING and shipped whole,
	//      account, host and service in one string. HostPortRegex is not the safety net the earlier
	//      comment assumed: its (?<![\w:./@-]) guard rejects a start preceded by "@", so it never sees
	//      the host half of an address. Placed AFTER branch 2 so a dotted alphabetic host still wins.
	//      THE (?=:\d) PORT REQUIREMENT IS THE WHOLE POINT OF THIS BRANCH'S SHAPE, and it is a deliberate
	//      trade, not a tightening for its own sake. A four-octet quad and a four-part version are the
	//      SAME TOKEN to a regex: "10.0.0.5" and "8.1.0.57" differ in nothing a pattern can read. An
	//      unguarded four-octet branch therefore redacts "clio@8.1.0.57", "cliogate@1.4.0.53" and
	//      "crtprocessbuilder@1.6.0.1" - the exact package-and-version strings the bundled-package
	//      convergence messages print on the console path - which is PR #1374's narrowing reopened by
	//      accident. Requiring a port keeps that narrowing intact and still closes the ported form, the
	//      one an authentication failure against an on-prem stand actually produces.
	//      KNOWN GAP, accepted knowingly: a bare "user@10.0.0.5" with no port still ships whole. It
	//      leaked before this PR as well, so nothing regresses; the alternative was trading a live leak
	//      for a live regression in readable version output, and this class prefers the smaller net gain
	//      over that swap. Closing it needs a signal a regex does not have.
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
	[GeneratedRegex(@"(?<!\\u?[0-9A-Fa-f]{0,3})[A-Za-z0-9._%+\-]+@(?:\[[^\]\s]{1,45}\]|[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?)*\.[A-Za-z]{2,}|\d{1,3}(?:\.\d{1,3}){3}(?=:\d)|[A-Za-z][A-Za-z0-9\-]*[A-Za-z0-9](?![A-Za-z0-9\-]))",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex EmailRegex();

	// ---------------------------------------------------------------------------------------------------------
	// SITE-ONLY rules. They run only in RedactSiteText, over text a Creatio site (its database, its IIS, its
	// application code) wrote, never over clio's own text: a serialized envelope has no line ends to stop at, a
	// backslash rule there would eat its escapes, and clio's own CLI and MCP errors name local relative paths
	// ("./MyPkg/descriptor.json") and logins the operator typed on purpose.
	// ---------------------------------------------------------------------------------------------------------

	// Rule 8: the cut before a stack frame or an HTML page. A line break - real, or written as text the way a
	// body escaped twice carries it ("\r\n", "\n") - that is followed by a .NET stack frame ("at X.Y"), an
	// "--- End of …" marker or an HTML document ends the site's message there. Only that break: the host lane
	// keeps the first line only, which loses the reason in "Value cannot be null.\r\nParameter name: password"
	// and in every later line of a multi-line Creatio validation message.
	[GeneratedRegex(
		@"(?:\r\n|\n|\r|\\r\\n|\\n|\\r)(?=(?:\s|\\[rnt])*(?:at[ \t]+(?:[A-Za-z_][\w`]*(?:\.[\w`<>]+)+|\[redacted(?:-[a-z]+)?\])|---[ \t]*End[ \t]+of\b"
		+ @"|<!DOCTYPE\b|<html\b))",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex StackOrPageBreakRegex();

	// Rule 3: the name a database signs in with, in the phrases the databases use: SQL Server's
	// "Login failed for user '…'" (18456) and "server principal \"…\"" (916), PostgreSQL's
	// "authentication failed for user \"…\"" (28P01), the pg_hba entry's ", user \"…\"", and PostgreSQL's role
	// refusals in exactly their own words. The quotes stay. Creatio's own role words ("Access denied for role
	// \"Sales managers\"", "User is not a member of role …", "Cannot set role …") and "locked by user …" do not
	// match: they name a CRM role or the user who holds a lock, which is the reason, not a login.
	[GeneratedRegex(
		@"(?<lead>\bfor[ \t]+user[ \t]+|,[ \t]*user[ \t]+|\bprincipal[ \t]+|\bpermission[ \t]+denied[ \t]+for[ \t]+role[ \t]+"
		+ @"|\btoo[ \t]+many[ \t]+connections[ \t]+for[ \t]+role[ \t]+|\bmust[ \t]+be[ \t]+member[ \t]+of[ \t]+role[ \t]+"
		+ @"|\bpermission[ \t]+denied[ \t]+to[ \t]+set[ \t]+role[ \t]+|\bdatabase[ \t]+|\b(?:open|access)[ \t]+server[ \t]+)"
		+ @"(?<q>\\u0022|\\u0027|\\""|[""'])(?<name>[^""'\\\r\n]{1,256}?)\k<q>",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex SignInOrDatabasePhraseRegex();

	// The same phrase with a name that a cut left OPEN at the end of the text ("Login failed for user 'zq inv" before the
	// preview's "..."): master's cut runs before the chain, and the half of the name before it goes too.
	[GeneratedRegex(
		@"(?<lead>\bfor[ \t]+user[ \t]+|,[ \t]*user[ \t]+|\bprincipal[ \t]+|\bpermission[ \t]+denied[ \t]+for[ \t]+role[ \t]+"
		+ @"|\btoo[ \t]+many[ \t]+connections[ \t]+for[ \t]+role[ \t]+|\bmust[ \t]+be[ \t]+member[ \t]+of[ \t]+role[ \t]+"
		+ @"|\bpermission[ \t]+denied[ \t]+to[ \t]+set[ \t]+role[ \t]+|\bdatabase[ \t]+|\b(?:open|access)[ \t]+server[ \t]+)"
		+ @"(?<q>\\u0022|\\u0027|\\""|[""'])(?<name>[^""'\\\r\n]{1,256}?)(?=(?:\.\.\.)?\z)",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex UnclosedSignInOrDatabasePhraseRegex();

	// PostgreSQL's role refusals that name the role BEFORE the reason.
	[GeneratedRegex(
		@"(?<lead>\brole[ \t]+)(?<q>\\u0022|\\u0027|\\""|[""'])(?<name>[^""'\\\r\n]{1,256}?)\k<q>"
		+ @"(?=[ \t]+(?:does[ \t]+not[ \t]+exist|is[ \t]+not[ \t]+permitted[ \t]+to[ \t]+log[ \t]+in))",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex RoleBeforeReasonRegex();

	// Rules 3 and 4: the sign-in and server keys a site's connection string carries that Redact's pair rule does
	// not take - "user", "username", "user name" and "login" (with "=" only: "The specified user: is not valid"
	// is a sentence), the db_user/dblogin family (with "=" or ": "; "DbUser:sa" with no space is how the host
	// lane's own reason reads), "credentials: …", and "Addr", "Address" and "Network Address" (with "=" only).
	private const string SiteKeys =
		@"user|username|user[ \t]name|login|addr|address|network[ \t]address|db_?user|db_?login|credentials?";

	[GeneratedRegex(
		$@"(?<![\w\-])(?<key>{SiteKeys})\b(?<sep>[ \t]*=[ \t]*|:[ \t]+)(?<value>{CredentialPairValue})",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex SiteKeyPairRegex();

	[GeneratedRegex(
		$@"(?<=(?:^|;)[ \t]*)(?<key>{SiteKeys})\b(?<sep>[ \t]*=[ \t]*){ListPairValue}",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex SiteKeyListPairRegex();

	// Rule 3: a uid key that is a login, CASE-SENSITIVELY: "db_uid=sa", "UserUid=x". Never "SchemaUId",
	// "PackageUId" or "Guid", which is how Creatio spells an identifier - the reason this rule cannot join the
	// case-insensitive pair rule.
	[GeneratedRegex(
		$@"(?<![\w\-])(?<key>[A-Za-z][A-Za-z0-9]*(?:[_\-](?:uid|UID)|[a-z]Uid))\b(?<sep>[ \t]*=[ \t]*|:[ \t]+)(?<value>{CredentialPairValue})",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex UidKeyPairRegex();

	// Rule 4: a Windows server instance or a DOMAIN\account - an identifier, then one or more "\identifier"
	// segments ("SQL01\SQLEXPRESS", "CORP\jdoe", and the "Files\Creatio\app.dll" tail that WindowsPathRegex
	// leaves of a path with a space). Never next to another backslash, and never across a "\r", "\n" or "\t"
	// written as text that is a line break of a body escaped twice ("null.\r\nParameter name: …"). Such an escape
	// before a LOWER-CASE letter is ambiguous: "SQL01\reporting", "CORP\nick" and "DOMAIN\tanya" are an instance and
	// two accounts, while "line one\nanother line", "Error 404\nnot found" and "Status: FAILED\nreason" are line
	// breaks. It is read as an account only when the segment before it is a SERVER NAME: capitals, digits, "-", "_",
	// "." and "$", at least one letter ("SQL01", "CORP", "DB-PROD", "SQL_PROD", "CORP.LOCAL", "SRV$"), and not a
	// status word (ERROR, FAILED, FAIL, OK, WARN, WARNING, INFO, DEBUG, TRUE, FALSE, NULL, NONE). After a number, a
	// status word or any other word it is a line break and the text stays.
	// The backslash and the escape after it are checked FIRST, by a lookahead, and only then does the rule look back
	// at the server name: looking back at every position of a long run of capitals made it quadratic.
	[GeneratedRegex(
		@"(?<!\\u?[0-9A-Fa-f]{0,3})(?:(?<=\\u[0-9A-Fa-f]{4})|(?<=\\[nrt])|(?<![\w\\.$\-]))[A-Za-z0-9_$](?:[\w.$\-]*[\w$])?"
		+ @"(?:(?:\\{1,2}(?![rnt\\]|u[0-9A-Fa-f]{4})|(?=\\{1,2}[rnt][a-z])(?<=(?<![\w.$\-])(?!(?:ERROR|FAILED|FAIL|OK|WARN|WARNING|INFO|DEBUG|TRUE|FALSE|NULL|NONE)\\)"
		+ @"(?=[A-Z0-9_.$\-]*[A-Z])[A-Z0-9_$](?:[A-Z0-9_.$\-]*[A-Z0-9_$])?)\\{1,2})"
		+ @"[A-Za-z0-9_$](?:[\w.$\-]*[\w$])?)+(?!\w)",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex WindowsInstanceRegex();

	// Rule 6 (site part): a relative or home path at a word start ("~/creatio/…", "./Terrasoft.WebApp/…",
	// "../conf/x.config"). Site-only: clio's own errors name relative paths the operator gave on purpose. A closing
	// bracket ends it unless a "/" and a segment follow (see WindowsPathRegex).
	// ESCAPE-AWARE, as the backslash rule below, because the OData error's raw text reaches this rule JSON-escaped:
	// it starts right after a complete \uXXXX, \n, \r or \t escape ("\u0022../x/y.config\u0022" kept "../x/"), and
	// its tail takes a backslash only as an escaped pair or as a single backslash that starts no escaped quote, no
	// escaped backslash and no \uXXXX that closes the token - before whitespace, a quote, "<", ">", "|", a closing
	// bracket or the end, where the tail ended before ("\"../x/y.config\"" lost the backslash of its closing escape
	// and left a bare quote behind). A \uXXXX glued to more text stays in the path, as it always did: the Redact rules before this one cannot
	// start after such an escape, so stopping there showed "\u0022Password=hunter2", "\u0022CORP\\jdoe" and
	// "\u0022;(SQL01\\SQLEXPRESS", which the path used to take with it (the differential runs of 20 Nov).
	[GeneratedRegex(EscapeAwareStartPrefix + @"./~\-" + EscapeAwareStartSuffix
		+ @"(?:~|\.{1,2})/(?:[^\s""'<>|)}\]\\]|\\\\|\\(?![""\\]|u[0-9A-Fa-f]{4}(?=[\s""'<>|)}\]]|\z))|[)}\]]+(?=/[^\s""'<>|)}\]/]))+",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex RelativePathRegex();

	// What a backslash in a site's text starts when it is NO path separator: a written line break or tab before
	// anything but a lower-case letter ("failed.\nNext step"), or a \uXXXX escape that closes the token, as
	// RelativePathRegex reads one ("..\\x\u0022 now"). An escaped quote (\") and an escaped backslash pair need no
	// arm of their own: a quote and a backslash are no path character, so a separator before them never finds the
	// segment it needs.
	private const string WrittenEscapeAfterBackslash = @"[nrt](?![a-z])|u[0-9A-Fa-f]{4}(?=[\s""'<>|)}\]]|\z)";

	// A separator of the backslash and drive-relative rules: a run of backslashes - one (decoded), two (JSON-escaped:
	// "..\\..\\Terrasoft.Configuration\\Web.config") or four (escaped twice) - that starts no written escape. Taken
	// whole and at any count, because one path can mix them ("..\\bin/x\y" kept "\y" when the first separator
	// decided the spelling) and the server-instance rule after this one cannot start after a backslash.
	private const string SitePathSeparator = @"(?>\\+)(?!" + WrittenEscapeAfterBackslash + @")";

	// One unit of a site path segment, for the backslash and drive-relative rules below:
	//   - a placeholder Redact already wrote inside the path ("..\[redacted-uri]\x", and the "..\\..[redacted-path]"
	//     that WindowsPathRegex's UNC arm leaves of an escaped "..\\..\\Terrasoft.Configuration\\Web.config");
	//   - any character but whitespace, a quote, an angle bracket, "|", ",", ";", a backslash, a dot and a closing
	//     bracket - a "/" included, so a mixed ".\conf/x.config" goes whole;
	//   - a dot that does not end the path, so "See ..\Web.config." keeps its full stop;
	//   - a closing bracket inside a folder name, only when a separator and a segment follow it (see WindowsPathRegex)
	//     or the placeholder WindowsPathRegex left of the rest ("..\\jdoe(old)[redacted-path]").
	// One unit cannot start where another does, so a run of them never backtracks.
	private const string SitePathUnit =
		@"(?:\[redacted(?:-[a-z]+)?\]|[^\s""'<>|,;.)}\]\\]|\.(?![\s""'<>|,;)}\]]|\z)"
		+ @"|[)}\]]+(?=" + SitePathSeparator + @"[^\s""'<>|,;)}\]\\]|\[redacted-path\]))";

	// A file name with a LOWER-CASE extension, as a file is named ("Web.config", "nlog.config", "db.bak"); a member or
	// an XML name ("Item.Value") ends in a capital. Read backwards from the end of a name, so it cannot backtrack.
	private const string LowerCaseExtensionEnd = @"(?<=\.[a-z][a-z0-9]{0,7})";

	// The FIRST segment after the dots may start like a written escape - "\n", "\r" or "\t" and a lower-case letter -
	// only when it is shown to be a path: another separator follows it ("..\temp\Web.config") or it is a file name
	// (".\nlog.config"). "Cannot delete "Order".\nthe record is referenced" is a sentence end and a line break. Later
	// segments need no such proof - the dots and a separator already made it a path, and when the two readings
	// disagree the one that removes wins ("..\x\nightly"), as in WindowsPathRegex.
	// A segment glued to master's path placeholder is shown to be a path too (".\tlnL.x[redacted-path]"): master's UNC
	// rule read the backslashes after it as a path's own.
	private const string FirstSegmentGuard =
		@"(?![nrt](?![a-z][^\s""'<>|,;)}\]\\]*" + SitePathSeparator + @"[^\s""'<>|,;)}\]\\]|[a-z][\w$\-.]*"
		+ LowerCaseExtensionEnd + @"(?![\w$\-]|\.[\w$\-])|[a-z][^\s""'<>|,;)}\]\\\[]*\[redacted-path\]))";

	// Where the backslash and drive-relative rules may start: the escape-aware start (see EscapeAwareStartPrefix), not
	// after a word character, a dot, "/", "~", "-", a backslash or a CLOSING bracket ("^[A-Z].\d{3}$" is a pattern) -
	// but right after one of clio's placeholders, which an earlier rule wrote over what stood before the path.
	private const string SitePathStart =
		@"(?<!\\u?[0-9A-Fa-f]{0,3})(?:(?<=\\u[0-9A-Fa-f]{4})|(?<=\\[nrt])|(?<=\[redacted(?:-[a-z]+)?\])"
		+ @"|(?<![\w\\./~\-";

	// Rule 6 (site part, backslash): a relative path a Windows site writes - "..\..\Terrasoft.Configuration\Web.config",
	// ".\conf\x.config", ".\SQLEXPRESS" - decoded, or JSON-escaped as the OData error's raw text carries it
	// ("..\\..\\Terrasoft.Configuration\\Web.config"). The WHOLE path goes, the leading dots included. On the escaped
	// spelling WindowsPathRegex has already taken the tail as a UNC path and left "..\\..[redacted-path]" or
	// ".[redacted-path]"; this rule takes the dots before that placeholder too. Site-only, like the forward-slash rule
	// above: Redact runs over clio's own text, which names relative paths on purpose.
	[GeneratedRegex(SitePathStart + @"]))"
		+ @"\.{1,2}(?:\[redacted-path\]|" + SitePathSeparator + FirstSegmentGuard + @"(?>" + SitePathUnit + @"+))"
		+ @"(?:" + SitePathSeparator + @"(?>" + SitePathUnit + @"+))*",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex RelativeBackslashPathRegex();

	// Rule 6 (site part, drive-relative): a drive letter with no separator after its colon - "C:foo\bar.config",
	// "C:Web.config", escaped "C:foo\\bar.config" - which WindowsPathRegex ("C:\") and DriveSlashPathRegex ("C:/")
	// never matched. Only these shapes count, so "Step C: retry", "10:30", "Range A:B", "x:Item.Value" and
	// "p:Item.name" stay:
	//   - a drive, a segment and at least one more segment behind a separator;
	//   - a drive and a segment glued to the placeholder WindowsPathRegex left of the rest ("C:foo[redacted-path]",
	//     from an escaped "C:foo\\bar.config\u0022");
	//   - an UPPER-CASE drive and a file name with a lower-case extension ("C:Web.config"); a lower-case letter before
	//     a colon is far more often an XML prefix or a key ("x:Item.Value", "p:Item.name") than a drive;
	//   - an UPPER-CASE drive and dotted or slashed segments glued to that placeholder ("C:foo.bar[redacted-path]",
	//     "C:foo/bar.x[redacted-path]"): master's chain took the backslash tail before this rule ran;
	//   - an UPPER-CASE drive and forward-slash segments ("C:foo/bar.config"), which DriveSlashPathRegex ("C:/") misses.
	// It starts like the backslash rule, and not after ":" or "@" either.
	[GeneratedRegex(SitePathStart + @":@]))"
		+ @"(?:[A-Za-z]:(?![\\/])(?:(?>" + SitePathUnit + @"+)(?:" + SitePathSeparator + @"(?>" + SitePathUnit + @"+))+"
		+ @"|(?>[^\s""'<>|,;.)}\]\\\[]+)\[redacted-path\])"
		+ @"|[A-Z]:(?![\\/])(?:\[redacted(?:-uri)?\]|[^\s""'<>|,;)}\]\\\[])+\[redacted-path\]"
		+ @"|[A-Z]:(?![\\/])(?>[^\s""'<>|,;)}\]\\\[/]+)(?:/(?>[^\s""'<>|,;)}\]\\\[/]*))+"
		+ @"|[A-Z]:(?>[\w$\-]+(?:\.[\w$\-]+)*)" + LowerCaseExtensionEnd + @")",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex DriveRelativePathRegex();

	// Rule 7: a key-like run (32+ base64/base64url characters, a digit and a letter among them) ONLY right after
	// a secret noun ("Key 3f9a…", "Invalid client secret Xk3_…", "Hash QUJD…=="). Not anywhere else: an index or
	// constraint name ("IX_UsrOrder_UsrNumber2_CreatedOn") says which field is the duplicate, and a record id or
	// an app code ("UsrPropertyManagement_v2kq9xt_ListPage") is not a secret. IsKeyLikeRun excludes those too.
	[GeneratedRegex(
		@"\b(?<noun>key|secret|token|hash|signature)\b(?<gap>[ \t]*[:=][ \t]*|[ \t]+)"
		+ @"(?<run>[A-Za-z0-9+/_\-]{32,}={0,2})(?![\w+/=\-])",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex KeyLikeRunRegex();

	[GeneratedRegex(@"^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex GuidRegex();

	// The first "_"-part of an app code: CamelCase humps of a capital and two or more small letters ("Usr",
	// "UsrPropertyManagement", "UsrApp").
	[GeneratedRegex(@"^(?:[A-Z][a-z]{2,}[0-9]*)+$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex AppCodeHeadRegex();

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
	public static string? RedactUntrustedOrNull(string? text) => NeutralizeOrNull(text, fenced: true, siteText: false);

	/// <summary>
	/// <see cref="RedactUntrustedOrNull"/> for text a Creatio site wrote: the same neutralization, with
	/// <see cref="RedactSiteText"/> as its redaction instead of <see cref="Redact"/>.
	/// </summary>
	/// <param name="text">The raw text the site sent.</param>
	/// <returns>The neutralized text, or <see langword="null"/> when there is nothing to report.</returns>
	public static string? RedactUntrustedSiteTextOrNull(string? text) => NeutralizeOrNull(text, fenced: true, siteText: true);

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
	public static string? RedactForConsoleOrNull(string? text) => NeutralizeOrNull(text, fenced: false, siteText: false);

	private static string? NeutralizeOrNull(string? text, bool fenced, bool siteText) {
		if (string.IsNullOrWhiteSpace(text)) {
			return null;
		}
		text = UnwrapOuterFence(text);
		// MASTER'S ORDER, master's bound (option B): the input is cut to UntrustedInputLimit BEFORE the chain, exactly as
		// master cut it, so every sink sees what master's chain saw and Extra and Site only replace spans of master's
		// result. The input is server-authored and arbitrarily large - a ResponseStatus.Message can be a whole page - and
		// the bound keeps the chain's work small. A clamp master did not have may run only after the chain; a long
		// quoted value master's cut leaves open is taken by Extra (UnclosedValueAfterPairPlaceholderRegex,
		// UnclosedJsonCredentialValueRegex).
		// The site variant cuts a stack trace or a page BEFORE the flattening, which would otherwise turn the line
		// break the cut keys on into a space.
		if (text.Length > UntrustedInputLimit) {
			text = text[..UntrustedInputLimit];
		}
		string flattened = FlattenDisplayHostileRuns(siteText ? RedactSiteText(text) : Redact(text));
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
	/// closer always survives. A caller that re-caps the finished message (<c>SysSettingFailureClassifier.SafeDetail</c>
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
	internal static string FlattenDisplayHostileRuns(string redacted) {
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
		return ExecuteRegex(nameof(Redact), text.Length, () => RedactCore(text));
	}

	/// <summary>
	/// Redacts text a Creatio SITE wrote - its message, its error code, its error page - with every rule of
	/// <see cref="Redact"/> plus the rules that are only safe on such text: the cut before a stack trace or an
	/// HTML page, sign-in names and database or server names in the database's own phrases, connection-string
	/// sign-in keys, Windows instance names, relative paths, and key-like runs after a secret noun.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Two entry points, not one. <see cref="Redact"/> also runs over whole serialized JSON envelopes
	/// (<c>ClioRunTool.RedactSerializedText</c>), MCP errors, logs and CLI lines, where a rule that runs to the end
	/// of a line, reads a backslash run as a path or takes a relative path would break JSON escapes, version
	/// numbers and clio's own paths. Those rules live here, and this method is called only where the text is the
	/// site's own. It is wired at <see cref="SiteResponse"/>'s message and code, the OData server-error and non-JSON
	/// preview paths, batch item errors, <c>DataWriteDiagnostic.message</c>, the designer and DataService error
	/// envelopes that <c>PageSchemaMetadataHelper.ParseSaveErrorMessage</c> and
	/// <c>DataServiceResponse.ThrowIfUnsuccessful</c> read, and compile-status's diagnostics and message tail. NOT
	/// every reader of a site's error text is wired yet: the readers that still pass the site's words through
	/// <see cref="Redact"/> only are listed, each with its file, member and the change that will wire it, in the
	/// known-unwired list of <c>SiteTextWiringTests</c>, whose source guard fails the build on a new one of the
	/// shapes it scans for by name (the site-error JSON names, the members that hold them, the constants that name
	/// them); a reader of another shape is not seen.
	/// </para>
	/// <para>
	/// It FAILS CLOSED as one unit: the cut, the whole <see cref="Redact"/> chain and every site rule run inside
	/// ONE <see cref="ExecuteRegex"/> guard, so a regex timeout in any of them returns the bare placeholder for
	/// the whole text - never the input, never a half-redacted text.
	/// </para>
	/// <para>
	/// The site's human-readable reason is never what goes: a database's own refusal sentence, a column, table,
	/// constraint or index name, a CRM role, a version number and the words around a placeholder all stay.
	/// </para>
	/// </remarks>
	/// <param name="text">The text the site sent. <see langword="null"/>/empty returns <see cref="string.Empty"/>.</param>
	/// <returns>The redacted text, or the bare placeholder when a rule timed out.</returns>
	public static string RedactSiteText(string? text) {
		if (string.IsNullOrEmpty(text)) {
			return string.Empty;
		}
		return ExecuteRegex(nameof(RedactSiteText), text.Length, () => Site(RedactCore(text)));
	}

	// OPTION B: Redact(x) = Extra(MasterChain.Redact(x)), RedactSiteText(x) = Site(Redact(x)). Master's chain runs first,
	// unchanged (MasterChain); Extra and Site only replace spans of what it returned, through ApplyEdits.
	private static string RedactCore(string text) {
		string result = ApplyInjectedRule(RedactFirstStage, text);
		result = MasterChain.Redact(result);
		result = ApplyInjectedRule(MasterLastStage, result);
		result = Extra(result);
		return ApplyInjectedRule(RedactLastStage, result);
	}

	// What clio's Redact removes beyond master's chain. Each rule reads the CURRENT text and returns edits; a rule that
	// master's chain already satisfied finds nothing left to do.
	internal static string Extra(string text) {
		string result = ApplyRule(text, ":", JsonCredentialPropertyRegex(), JsonValueEdits);
		result = ApplyRule(result, "://", UriRegex(), match => Whole(match, RedactedUri));
		result = ApplyRule(result, "eyJ", JwtRegex(), match => Whole(match, RedactedValue));
		result = ApplyRule(result, "eyJ", PartialJwtAtTheEndRegex(), match => Whole(match, RedactedValue));
		result = ApplyRule(result, "bearer", BearerCredentialRegex(), BearerEdits, StringComparison.OrdinalIgnoreCase);
		result = ApplyRule(result, "Bearer", GluedBearerCredentialRegex(), match =>
			ProseRunAfterPlaceholderRegex().IsMatch(SplitTrailingPunctuation(match.Groups["cred"].Value).Credential) ? NoEdit : BearerEdits(match));
		string schemed = result;
		result = ApplyRule(result, SchemeCredentialRegex(), match => SchemeEdits(schemed, match));
		result = ApplyRule(result, "digest", DigestCredentialRegex(), DigestEdits, StringComparison.OrdinalIgnoreCase);
		result = ApplyRule(result, "@", EmailRegex(), match => Whole(match, RedactedValue));
		string beforeHostPort = result;
		result = ApplyRule(result, ":", HostPortRegex(), match => GluedWordEdits(beforeHostPort, match, RedactedUri));
		result = ApplyRule(result, ":", Ipv6Regex(), match => match.Value.Any(char.IsAsciiDigit) ? Whole(match, RedactedUri) : NoEdit);
		CountLines countLines = new(result);
		result = ApplyRule(result, ":", SingleLabelHostPortRegex(),
			match => IsSingleLabelHostPort(countLines, match) ? Whole(match, RedactedUri) : NoEdit);
		string beforeIpv4 = result;
		result = ApplyRule(result, ".", PrivateIpv4Regex(), match => IsVersionContext(beforeIpv4, match) ? NoEdit : GluedWordEdits(beforeIpv4, match, RedactedUri));
		result = ApplyRule(result, ":/", DriveSlashPathRegex(), match => Whole(match, RedactedPath));
		result = ApplyRule(result, "\\", WindowsPathRegex(), match => Whole(match, RedactedPath));
		result = ApplyRule(result, "/", PosixPathRegex(), match => Whole(match, RedactedPath));
		string listed = result;
		result = ApplyRule(result, "=", CredentialListPairRegex(), match => ListPairEdits(listed, match));
		string paired = result;
		result = ApplyRuleWhen(result, PairSeparatorCondition, HasPairSeparator, CredentialPairRegex(), match => PairEdits(paired, match));
		return AfterMasterPlaceholders(result);
	}

	// The rules that read master's own placeholders (see the three regexes below).
	private static string AfterMasterPlaceholders(string text) {
		string result = ApplyRule(text, "=[redacted]", QuotedValueAfterPairPlaceholderRegex(), match => Part(match.Groups["value"], RedactedValue),
			StringComparison.OrdinalIgnoreCase);
		result = ApplyRule(result, "=[redacted]", UnclosedValueAfterPairPlaceholderRegex(), match => Part(match.Groups["value"], RedactedValue),
			StringComparison.OrdinalIgnoreCase);
		result = ApplyRule(result, ":", UnclosedJsonCredentialValueRegex(), match => Part(match.Groups["value"], RedactedValue));
		result = ApplyRule(result, "[redacted", DigestListAfterPlaceholderRegex(),
			match => DigestClientParameterRegex().IsMatch(match.Groups["params"].Value) ? Part(match.Groups["params"], RedactedValue) : NoEdit,
			StringComparison.OrdinalIgnoreCase);
		result = ApplyRule(result, "=[redacted]", CredentialRunAfterPairPlaceholderRegex(), RunAfterPairPlaceholderEdits,
			StringComparison.OrdinalIgnoreCase);
		result = ApplyRule(result, "=", ConnectionPartAfterPlaceholderRegex(), match => Part(match.Groups["value"], RedactedValue));
		result = ApplyRule(result, "[redacted-path]", TokenRunAfterPathPlaceholderRegex(), match =>
			match.Groups["token"].Length >= 12 && !VersionRunRegex().IsMatch(match.Groups["token"].Value)
				&& !DottedTypeNameRegex().IsMatch(match.Groups["token"].Value)
				? Part(match.Groups["run"], RedactedValue)
				: NoEdit);
		result = ApplyRule(result, "[redacted-path]:", DrivePlaceholderTailRegex(), match => Whole(match, RedactedPath));
		result = ApplyRule(result, "[redacted-path]", ServerGluedToPathPlaceholderRegex(), match => Whole(match, RedactedValue));
		return ApplyRule(result, "=[redacted]", HostLabelBeforePairPlaceholderRegex(), match => Whole(match, RedactedUri));
	}

	// AFTER MASTER'S PAIR RULE. Master's pair rule takes only the first word after a credential key, so when that word
	// is a scheme word the credential stands right after its placeholder: "Authorization: Basic X7q2" becomes
	// "Authorization=[redacted] X7q2", "password=Negotiate YII rejected" becomes "password=[redacted] YII rejected".
	// The run there goes, unless it is a challenge parameter or one of the words a header position keeps
	// (HeaderProseWordRegex: "rejected", "failed", "token", …). Its quotes stay.
	[GeneratedRegex(
		$@"(?<=(?<![\w.\-])[\w.\-]*(?:{CredentialSecretKeys}|{CredentialWordKeys}|{ConnectionStringPartKeys})=\[redacted\][ \t]*)"
		+ CredentialRunQuote + CredentialRun,
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex CredentialRunAfterPairPlaceholderRegex();

	// A run that reaches a placeholder ("abcdefgh.ijklmnopC:/x[redacted-path]") is read up to it, unless what stands
	// before the placeholder is the next pair's key ("token=[redacted]").
	private static IEnumerable<SpanEdit> RunAfterPairPlaceholderEdits(Match match) {
		string run = match.Groups["cred"].Value;
		int placeholder = run.IndexOf("[redacted", StringComparison.Ordinal);
		if (placeholder >= 0) {
			run = run[..placeholder];
			if (run.EndsWith('=') || run.EndsWith(':')) {
				return NoEdit;
			}
		}
		if (run.Length == 0 || ChallengeParameterRegex().IsMatch(run) || NextPairRegex().IsMatch(run)) {
			return NoEdit;
		}
		(string credential, string _) = SplitTrailingPunctuation(run);
		bool quoted = match.Groups["q"].Success && match.Groups["q"].Length > 0;
		bool credentialShaped = quoted || !ProseRunAfterPlaceholderRegex().IsMatch(credential);
		return credential.Length == 0 || HeaderProseWordRegex().IsMatch(credential) || !credentialShaped
			? NoEdit
			: CredentialRunEdit(match, credential);
	}

	// The next pair ("Catalog=ZqDb"), which the pair rules read; a padded token ("dXNlcjpwYXNz=") is not one.
	[GeneratedRegex(@"^[A-Za-z_][\w.\-]*=(?=[^=])", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex NextPairRegex();

	// A word a sentence goes on with after master's "key=[redacted]": lower case, or one capital then lower case, hyphen
	// joined, or a number. master's pair rule took the credential itself there ("password=hunter2 was rejected"), or
	// the Bearer placeholder ("Authorization: Bearer x returned 401"), so the word stays. A run with a digit, an inner
	// capital, an all-capitals run or "+", "/", "=", "_" is credential-shaped and goes. KNOWN RESIDUE: a prose-shaped
	// credential after a scheme word after a credential key ("password: Basic secrettokenvalue") shows; master's chain
	// keeps no trace of the scheme word it took, so the two cannot be told apart.
	[GeneratedRegex(@"^(?:[a-z]+|[A-Z][a-z]+|\d+(?:[.:]\d+)*)(?:-[A-Za-z][a-z]*)*$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex ProseRunAfterPlaceholderRegex();

	// A quoted value after master's "key=[redacted]": master's pair value class stops at a quote, so a value quoted with
	// escaped quotes ("Password=\\"a b c\\"") left its whole content after the placeholder. It goes to its closing quote:
	// a quote right before a word character opens a name inside the value ("…failed for user "zq_user"") and does
	// not close it, because master's pair rule had already taken the backslash of the escaped quote that did.
	[GeneratedRegex(
		$@"(?<=(?<![\w.\-])[\w.\-]*(?:{CredentialSecretKeys}|{CredentialWordKeys}|{ConnectionStringPartKeys})=\[redacted\])"
		+ @"(?<open>""|')(?<value>(?:[^""'\r\n]|[""'](?=\w))*?)\\?\k<open>(?!\w)",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex QuotedValueAfterPairPlaceholderRegex();

	// A quoted value after master's "key=[redacted]" that a cut left OPEN: master's own cuts (2,400 before the fenced
	// rendering, 500 before the non-JSON preview and the refused delete's inner message) cut a long quoted value before
	// its closing quote, and master's pair rule then took only its first character. The rest goes, to the end of the
	// text - where the cut is, before the preview's "..." - never across a JSON bracket or a line break. It starts on a
	// character that can open a value, so a stray closing quote before a sentence ("password=[redacted]' end") is not one.
	[GeneratedRegex(
		$@"(?<=(?<![\w.\-])[\w.\-]*(?:{CredentialSecretKeys}|{CredentialWordKeys}|{ConnectionStringPartKeys})=\[redacted\])"
		+ @"(?<open>""|')(?<value>(?:\[redacted(?:-[a-z]+)?\]|[^\s""'{}\[\]])(?:\[redacted(?:-[a-z]+)?\]|[^""'\r\n{}\[\]])*?)(?=(?:\.\.\.)?\z)",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex UnclosedValueAfterPairPlaceholderRegex();

	// A JSON credential property whose string value a cut left OPEN at the end of the text, in any quote spelling
	// ("{\u0022password\u0022:\u0022…" cut by master's clamp): master's JSON rule needs the closing quote, so none of its
	// rules took the value. Only at the end of the text, where a cut leaves it.
	[GeneratedRegex(
		$@"(?<q>{JsonQuoteSpellings})(?<key>{JsonCredentialKeyPattern})\k<q>\s*:\s*\k<q>(?<value>{JsonStringContent})\z",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex UnclosedJsonCredentialValueRegex();

	// A JWT that a cut left with one or two of its segments at the end of the text ("eyJhbGci…J9.eyJzdW" before the
	// preview's "..."): master's JWT rule needs all three, so the first ones stayed.
	[GeneratedRegex(@"(?:(?<=\\u[0-9A-Fa-f]{4})|(?<=\\[nrt])|\b)eyJ[A-Za-z0-9_-]*(?:\.[A-Za-z0-9_-]+){0,2}\.?(?=(?:\.\.\.)?\z)",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex PartialJwtAtTheEndRegex();

	// A Digest client list right after a placeholder: master's pair rule took "Digest" as the Authorization value and
	// left "username=…, nonce=…, response=…" after it. It goes when it carries what a client sends.
	[GeneratedRegex(
		@"(?<=\[redacted(?:-[a-z]+)?\][ \t]+)(?<params>[A-Za-z][\w\-]*[ \t]*=[ \t]*(?:""[^""\r\n]*""|\\u0022[^\s,]*?\\u0022|[^\s,""\\]+)"
		+ @"(?:[ \t]*,[ \t]*[A-Za-z][\w\-]*[ \t]*=[ \t]*(?:""[^""\r\n]*""|\\u0022[^\s,]*?\\u0022|[^\s,""\\]+))*)",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex DigestListAfterPlaceholderRegex();

	// A connection-string part whose first word master's chain took with a path ("\\SQLEXPRESS;Initial" became a path,
	// so "Catalog=…" was no key any more): after a placeholder or a ";", "Catalog=", "Source=" and "ID=" still name a
	// database, a server and a login, and the value goes to the next ";".
	// The value has the list shape a ";" list gives it (ListPairValue: to the next ";" or the end of the line), because
	// master's path took the ";" that made it a list; where a quote stops that, one run goes.
	[GeneratedRegex(
		@"(?<=(?:\[redacted(?:-[a-z]+)?\]|;)[ \t]*)(?:(?i:initial ?catalog|catalog|data ?source|source|database|server|user ?id|uid)|ID)"
		+ $@"[ \t]*=[ \t]*(?:{ListPairValue}|(?<value>[^;\s""']+))",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex ConnectionPartAfterPlaceholderRegex();

	// A token-shaped run after master's path placeholder: master's UNC rule took "\\nBearer" (an escaped written line
	// break and the scheme word) as a path, and left the credential after it. A dotted run of twelve or more characters
	// goes there; a version does not.
	// The run goes as far as master's Bearer value would have ("[^\s,;\"']+"), a closing sentence dot aside. A dotted
	// .NET name ("System.IO.FileNotFoundException") is not a token and stays.
	[GeneratedRegex(
		@"(?<=\[redacted-path\][ \t]+)(?<run>(?<token>[A-Za-z0-9][\w\-~+/]*(?:\.[\w\-~+/]+)+=*)[^\s,;""']*?)(?=\.?(?:[\s,;""']|\z))",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex TokenRunAfterPathPlaceholderRegex();

	[GeneratedRegex(@"^(?:[A-Z][a-z]+(?:[A-Z]+[a-z]*)*|[A-Z]{2})(?:\.(?:[A-Z][a-z]+(?:[A-Z]+[a-z]*)*|[A-Z]{2}))+$",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex DottedTypeNameRegex();

	// A drive, master's path placeholder and what master's POSIX rule left after the ":" it stops at: a port
	// ("D:/data/x.mdfredis:6379"), or the next path glued on ("D:/data/x.mdfC:/Secrets/a", "D:/x.mdfC:Pxp3"). The whole
	// becomes the one path placeholder; a backslash goes only in escaped units, as in WindowsPathRegex.
	[GeneratedRegex(@"(?:(?<=\\u[0-9A-Fa-f]{4})|(?<=\\[nrt])|(?<![\w.\-]))[A-Za-z]:\[redacted-path\]:(?:[^\s""'<>|,;\\]|\\\\|\\(?![""\\]))+",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex DrivePlaceholderTailRegex();

	[GeneratedRegex(@"^\d+(?:\.\d+)*$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex VersionRunRegex();

	// A host label before master's "host=[redacted]": master read "smtp-host:25" as the pair "host:25" and kept "smtp-".
	[GeneratedRegex(@"(?<![\w.\-])[A-Za-z0-9](?:[A-Za-z0-9\-.]*[A-Za-z0-9])?[\-.](?=(?i:host|hostname|server)=\[redacted\])",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex HostLabelBeforePairPlaceholderRegex();

	// What only a site's text loses, after Redact. The cut runs here too, AFTER the chain (condition 2): a cut before it
	// could stop master's quoted-value rule from matching ('password="hunter2\n at X.Y()"'). The cut replaces the trace
	// or page after the line break; its brackets stay, as every edit keeps them.
	internal static string Site(string text) {
		string result = ApplyInjectedRule(SiteFirstStage, text);
		Work += result.Length;
		Match cut = ForLength(StackOrPageBreakRegex(), result.Length).Match(result);
		if (cut.Success) {
			int from = cut.Index + cut.Length;
			result = ApplyEdits(result, [new SpanEdit(from, result.Length - from, RedactedValue, Collapse: true)]);
		}
		result = ApplyRule(result, SignInOrDatabasePhraseRegex(), match => Part(match.Groups["name"], RedactedValue));
		result = ApplyRule(result, UnclosedSignInOrDatabasePhraseRegex(), match => Part(match.Groups["name"], RedactedValue));
		result = ApplyRule(result, RoleBeforeReasonRegex(), match => Part(match.Groups["name"], RedactedValue));
		string listed = result;
		result = ApplyRule(result, "=", SiteKeyListPairRegex(), match => ListPairEdits(listed, match));
		result = ApplyRuleWhen(result, PairSeparatorCondition, HasPairSeparator, SiteKeyPairRegex(), SiteKeyPairEdits);
		result = ApplyRuleWhen(result, PairSeparatorCondition, HasPairSeparator, UidKeyPairRegex(), SiteKeyPairEdits);
		// The relative and drive-relative paths before the server instance, which reads a path's later segments as
		// "SERVER\instance", and once more after it, for a path that starts right after the placeholder it wrote.
		result = SitePaths(result);
		result = ApplyRule(result, "\\", WindowsInstanceRegex(), match => Whole(match, RedactedValue));
		result = ApplyRule(result, "\\", InstanceAfterPlaceholderRegex(), match => Part(match.Groups["instance"], RedactedValue));
		result = SitePaths(result);
		result = ApplyRule(result, KeyLikeRunRegex(), KeyLikeRunEdits);
		return ApplyInjectedRule(SiteLastStage, result);
	}

	// An instance name after the placeholder that took its server ("10.0.3.17SQL01\SQLEXPRESS" became
	// "[redacted]\SQLEXPRESS"). Never a JSON escape: a single backslash before b, f, n, r, t or u is one.
	[GeneratedRegex(@"(?<=\[redacted(?:-uri)?\])(?:\\\\|\\(?![bfnrtu]))(?<instance>[A-Za-z][\w$\-]{0,63})(?![\w$\\])",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex InstanceAfterPlaceholderRegex();

	// A server or domain name glued to the path placeholder master's UNC rule wrote: "CORP\\jdoe" became "CORP[redacted-path]".
	[GeneratedRegex(@"(?:(?<=\\u[0-9A-Fa-f]{4})|(?<=\\[nrt])|(?<=\.\.\.)|(?<![\w\\.$\-]))[A-Z][A-Z0-9_.$\-]*(?=\[redacted-path\])",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex ServerGluedToPathPlaceholderRegex();

	// A forward-slash tail glued to master's path placeholder ("[redacted-path]/nlj.config", "[redacted-path]/x[redacted-path]"):
	// the rest of the same path, which master's POSIX rule did not start on. It joins the placeholder.
	[GeneratedRegex(@"\[redacted-path\](?:/(?:\[redacted(?:-uri)?\]|[^\s""'<>|,;)}\]\\\[/])*)+(?:\[redacted-path\])?",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex SlashTailAfterPathPlaceholderRegex();

	// Rule 6, site part: the forward-slash, backslash and drive-relative relative paths.
	private static string SitePaths(string text) {
		string result = ApplyRule(text, RelativePathRegex(), match => Whole(match, RedactedPath));
		result = ApplyRule(result, RelativeBackslashPathRegex(), match => Whole(match, RedactedPath));
		result = ApplyRule(result, ":", DriveRelativePathRegex(), match => Whole(match, RedactedPath));
		return ApplyRule(result, "[redacted-path]/", SlashTailAfterPathPlaceholderRegex(), match => Whole(match, RedactedPath));
	}

	// The run without its trailing sentence punctuation, and that punctuation, which stays.
	private static (string Credential, string Trailing) SplitTrailingPunctuation(string run) {
		Match punctuation = TrailingSentencePunctuationRegex().Match(run);
		string trailing = punctuation.Success ? punctuation.Value : string.Empty;
		return (run[..(run.Length - trailing.Length)], trailing);
	}

	// The credential run without its trailing sentence punctuation, which stays.
	private static IEnumerable<SpanEdit> CredentialRunEdit(Match match, string credential) =>
		[new SpanEdit(match.Groups["cred"].Index, credential.Length, RedactedValue)];

	// The Bearer floor: the whole run goes, except a documented trade.
	private static IEnumerable<SpanEdit> BearerEdits(Match match) {
		string run = match.Groups["cred"].Value;
		if (PlaceholderRunRegex().IsMatch(run) || ChallengeParameterRegex().IsMatch(run)) {
			return NoEdit;
		}
		(string credential, string _) = SplitTrailingPunctuation(run);
		return credential.Length == 0 || BearerTradeRegex().IsMatch(credential) ? NoEdit : CredentialRunEdit(match, credential);
	}

	// Basic, NTLM and Negotiate: what stays depends on whether the word stands in a header position.
	private static IEnumerable<SpanEdit> SchemeEdits(string text, Match match) {
		string run = match.Groups["cred"].Value;
		if (PlaceholderRunRegex().IsMatch(run) || ChallengeParameterRegex().IsMatch(run)) {
			return NoEdit;
		}
		(string credential, string _) = SplitTrailingPunctuation(run);
		int from = Math.Max(0, match.Index - 64);
		bool headerPosition = HeaderPositionRegex().IsMatch(text.AsSpan(from, match.Index - from).ToString());
		bool kept = credential.Length == 0 || (headerPosition
			? HeaderProseWordRegex().IsMatch(credential)
			: ProseAfterSchemeWordRegex().IsMatch(credential));
		return kept ? NoEdit : CredentialRunEdit(match, credential);
	}

	private static IEnumerable<SpanEdit> DigestEdits(Match match) =>
		DigestClientParameterRegex().IsMatch(match.Groups["params"].Value) ? Part(match.Groups["params"], RedactedValue) : NoEdit;

	// A digit is required so that two hex WORDS around "::" ("Bad::Cafe") are not read as an address (see Extra).

	// The service ports a site's own refusal names its database, cache, mail or web server on.
	private static readonly HashSet<int> WellKnownServicePorts = [
		21, 22, 25, 53, 80, 88, 110, 143, 389, 443, 445, 465, 587, 636, 993, 995, 1433, 1434, 1521, 2375, 2376,
		3306, 3389, 5432, 5433, 5672, 5984, 6379, 8080, 8081, 8088, 8443, 8888, 9042, 9092, 9200, 9300, 11211,
		15672, 27017
	];

	// Words that a message writes before a colon and a number which are never a host.
	private static readonly HashSet<string> NotHostLabels = new(StringComparer.OrdinalIgnoreCase) {
		"timeout", "port", "attempt", "attempts", "retry", "retries", "line", "column", "col", "code", "error",
		"status", "count", "index", "size", "limit", "offset", "page", "step", "version", "build", "delay",
		"elapsed", "duration", "ms", "http", "https", "exit", "item", "row", "level", "id"
	};

	// The words of a count or a test-run summary ("Failed:22", "Total:443", "Passed:421"): 22 and 443 are service
	// ports, so without them a summary line read as hosts. A count word is a COUNT when another count word with a
	// number is on the same line - a real or a written line break ends it ("Passed:5432, Total:5432",
	// "Total:3306 Passed:8080 Skipped:1433"). Alone, "word:port" takes the general single-label rule like any label,
	// so "ok:6379" and "tests:5432" are hosts and "tests:4000" is not. Master had no single-label rule at all: it
	// removed none of these, so this rule keeps nothing master removed.
	private const string CountWords =
		"total|passed|failed|skipped|succeeded|success|successes|failure|failures|errors|warning|warnings|tests|ignored"
		+ "|inconclusive|executed|notexecuted|pending|completed|done|ok";

	private static readonly HashSet<string> CountLabels = new(CountWords.Split('|'), StringComparer.OrdinalIgnoreCase);

	[GeneratedRegex($@"\b(?:{CountWords}):\d", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
	private static partial Regex CountWordRegex();

	[GeneratedRegex(@"\r\n|\n|\r|\\r\\n|\\n|\\r", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex LineBreakRegex();

	// The count lines of a text: the index where each line ends and whether it holds two or more count words with a
	// number. Built ONCE per text, so many "word:port" matches cost one scan, not one scan each.
	private sealed class CountLines {
		private readonly List<(int End, bool Counts)> lines = [];

		internal CountLines(string text) {
			int start = 0;
			foreach (Match lineBreak in LineBreakRegex().Matches(text)) {
				Add(text, start, lineBreak.Index);
				start = lineBreak.Index + lineBreak.Length;
			}
			Add(text, start, text.Length);
		}

		// The search stays inside the line, so a text of many lines is scanned once.
		private void Add(string text, int start, int end) {
			Match first = CountWordRegex().Match(text, start, end - start);
			lines.Add((end, first.Success && first.NextMatch().Success));
		}

		// Whether the line that holds index is a count line.
		internal bool AtCountLine(int index) {
			int low = 0;
			int high = lines.Count - 1;
			while (low < high) {
				int middle = (low + high) / 2;
				if (lines[middle].End < index) {
					low = middle + 1;
				}
				else {
					high = middle;
				}
			}
			return lines[low].Counts;
		}
	}

	// Whether "label:port" is a host and its port. localhost always is. A label that names a number in prose
	// ("Status:500", "line:42", "timeout:5000") never is. Otherwise a well-known service port makes it one
	// ("redis:6379"), and any other port of 1024 or more does when the label looks like a machine name - a digit,
	// a hyphen, or three or more capitals ("db01:2000", "SQLPROD01:1433", "DBSERVER:4000").
	private static bool IsSingleLabelHostPort(CountLines countLines, Match match) {
		string label = match.Groups["label"].Value;
		if (!int.TryParse(match.Groups["port"].Value, out int port) || port is < 1 or > 65_535) {
			return false;
		}
		if (label.Equals("localhost", StringComparison.OrdinalIgnoreCase)) {
			return true;
		}
		if (NotHostLabels.Contains(label)) {
			return false;
		}
		if (CountLabels.Contains(label) && countLines.AtCountLine(match.Index)) {
			return false;
		}
		bool machineShaped = label.Any(char.IsAsciiDigit) || label.Contains('-')
			|| (label.Length >= 3 && label.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character)));
		return WellKnownServicePorts.Contains(port) || (port >= 1024 && machineShaped);
	}

	// An address glued to a word ("retry10.0.3.17", "db01.internal:1433Range") takes the glued letters with it, so the
	// placeholder never splits one run into two words of which only one stood in the input. Never into an escape: the
	// letter of "\n" or "\u0027" before the word stays.
	private static IEnumerable<SpanEdit> GluedWordEdits(string text, Match match, string placeholder) {
		int start = match.Index;
		int end = match.Index + match.Length;
		bool afterEscape = (start >= 6 && text[start - 6] == '\\' && text[start - 5] == 'u'
				&& text.AsSpan(start - 4, 4).ToString().All(char.IsAsciiHexDigit))
			|| (start >= 2 && text[start - 2] == '\\' && text[start - 1] is 'n' or 'r' or 't');
		if (!afterEscape) {
			while (start > 0 && (char.IsAsciiLetter(text[start - 1]) || text[start - 1] == '_')) {
				start--;
			}
			if (start > 0 && text[start - 1] == '\\') {
				start = text[start] == 'u' && start + 5 <= match.Index && text.AsSpan(start + 1, 4).ToString().All(char.IsAsciiHexDigit)
					? start + 5
					: start + 1;
			}
		}
		while (end < text.Length && (char.IsAsciiLetterOrDigit(text[end]) || text[end] == '_')) {
			end++;
		}
		return [new SpanEdit(start, end - start, placeholder)];
	}

	private static bool IsVersionContext(string text, Match match) {
		foreach (string octet in match.Value.Split('.')) {
			if (!int.TryParse(octet, out int value) || value > 255) {
				return true;
			}
		}
		int from = Math.Max(0, match.Index - 24);
		return VersionContextRegex().IsMatch(text.AsSpan(from, match.Index - from).ToString());
	}

	// A ";"-list pair: the value goes to the next ";" when the pair follows a ";", or when the text starts with
	// it and another "name=" follows; anything else is left for the ordinary pair rule.
	private static IEnumerable<SpanEdit> ListPairEdits(string text, Match match) {
		bool afterSeparator = text.AsSpan(0, match.Index).TrimEnd(" \t").EndsWith(";");
		bool listFollows = !afterSeparator && ListContinuationRegex().IsMatch(text, match.Index + match.Length);
		return afterSeparator || listFollows ? Part(match.Groups["value"], RedactedValue) : NoEdit;
	}

	private static IEnumerable<SpanEdit> PairEdits(string text, Match match) {
		if (match.Groups["kept"].Success) {
			return NoEdit;
		}
		string key = match.Groups["key"].Value;
		bool colon = match.Groups["sep"].Value.Contains(':');
		if (colon && IdentifierValuedKeyRegex().IsMatch(key)
			&& IdentifierPairValueRegex().IsMatch(match.Groups["value"].Value)) {
			return NoEdit;
		}
		// "key" or "auth" with a colon in a sentence, followed by a scheme word and a word the scheme rules keep there
		// ("Primary key: Basic info", "auth: Basic mode is off"), is prose: the scheme rules already ruled that word
		// is no credential. After any OTHER credential key - password, secret, cookie, token, access_token,
		// client_secret, … - the value is the secret whatever it looks like, so it goes ("password: Basic
		// secrettokenvalue"). THE TRADE, removing wins: such a key in a sentence ("The token: NTLM handshake failed")
		// loses the words after it too. A "key" or "auth" joined by "-" or "_" to the word before it ("X-Auth:",
		// "Proxy-Auth:") is a header name, not prose, and keeps nothing either. With "=" it is a pair after every key,
		// and the value goes.
		int keyAt = match.Groups["key"].Index;
		bool joined = keyAt > 0 && text[keyAt - 1] is '-' or '_';
		if (colon && !match.Groups["kq"].Success && !joined && ProseKeyRegex().IsMatch(key)
			&& SchemeWordAndKeptWordRegex().Match(match.Groups["value"].Value) is { Success: true } schemed
			&& (schemed.Groups["scheme"].Value.Equals("Bearer", StringComparison.OrdinalIgnoreCase)
				? BearerTradeRegex().IsMatch(SplitTrailingPunctuation(schemed.Groups["word"].Value).Credential)
				: ProseAfterSchemeWordRegex().IsMatch(SplitTrailingPunctuation(schemed.Groups["word"].Value).Credential))) {
			return NoEdit;
		}
		// Only the value goes: the key, its quotes and its separator stay as the text wrote them.
		return Part(match.Groups["value"], RedactedValue);
	}

	// The site keys "user", "username", "user name", "login" and the address keys take "=" only; with ":" the
	// word is a sentence ("The specified user: is not valid").
	private static IEnumerable<SpanEdit> SiteKeyPairEdits(Match match) {
		if (match.Groups["kept"].Success) {
			return NoEdit;
		}
		string key = match.Groups["key"].Value;
		bool colon = match.Groups["sep"].Value.Contains(':');
		if (colon && !key.StartsWith("db", StringComparison.OrdinalIgnoreCase)
			&& !key.StartsWith("credential", StringComparison.OrdinalIgnoreCase)
			&& !key.EndsWith("uid", StringComparison.OrdinalIgnoreCase)) {
			return NoEdit;
		}
		return Part(match.Groups["value"], RedactedValue);
	}


	private static IEnumerable<SpanEdit> KeyLikeRunEdits(Match match) {
		string run = match.Groups["run"].Value;
		bool hasDigit = run.Any(char.IsAsciiDigit);
		bool hasLetter = run.Any(char.IsAsciiLetter);
		if (!hasDigit || !hasLetter || GuidRegex().IsMatch(run) || IsAppCode(run)) {
			return NoEdit;
		}
		return Part(match.Groups["run"], RedactedValue);
	}

	// An app or schema code: a CamelCase first part and "_"-parts of 31 characters or fewer.
	private static bool IsAppCode(string run) {
		string[] parts = run.Split('_');
		return parts.Length > 1 && AppCodeHeadRegex().IsMatch(parts[0]) && parts.All(part => part.Length <= 31);
	}

	#region Test seam: an injected rule that times out

	/// <summary>Stage of the <see cref="Redact"/> chain before its first rule.</summary>
	internal const string RedactFirstStage = "redact:first";

	/// <summary>Stage of the <see cref="Redact"/> chain after its last rule.</summary>
	internal const string RedactLastStage = "redact:last";

	/// <summary>Stage of the <see cref="Redact"/> chain after master's chain, before <see cref="Extra"/>.</summary>
	internal const string MasterLastStage = "master:last";

	/// <summary>Stage of <see cref="RedactCredentials"/> before master's chain.</summary>
	internal const string CredentialsFirstStage = "credentials:first";

	/// <summary>Stage of <see cref="RedactCredentials"/> after its extra rules.</summary>
	internal const string CredentialsLastStage = "credentials:last";

	/// <summary>Stage of <see cref="RedactJsonCredentialProperties"/> after its extra rule.</summary>
	internal const string JsonLastStage = "json:last";

	/// <summary>Stage of <see cref="RedactSiteText"/> before its first rule (the cut).</summary>
	internal const string SiteFirstStage = "site:first";

	/// <summary>Stage of <see cref="RedactSiteText"/> after its last site rule.</summary>
	internal const string SiteLastStage = "site:last";

	[ThreadStatic]
	private static Regex? injectedRule;

	[ThreadStatic]
	private static string? injectedStage;

	/// <summary>
	/// Runs <paramref name="rule"/> over the text at <paramref name="stage"/> of the chain on THIS thread until the
	/// returned scope is disposed, so a test can prove that a rule which times out fails the whole text closed.
	/// </summary>
	/// <param name="stage">One of the stage constants.</param>
	/// <param name="rule">The pattern to run; a test passes one that always times out on its input.</param>
	/// <returns>The scope that removes the rule.</returns>
	internal static IDisposable InjectRuleForTests(string stage, Regex rule) {
		injectedStage = stage;
		injectedRule = rule;
		return new InjectedRuleScope();
	}

	private static string ApplyInjectedRule(string stage, string text) =>
		injectedRule is not null && string.Equals(injectedStage, stage, StringComparison.Ordinal)
			? injectedRule.Replace(text, "$0")
			: text;

	private static void ClearInjectedRule() {
		injectedRule = null;
		injectedStage = null;
	}

	private sealed class InjectedRuleScope : IDisposable {
		public void Dispose() => ClearInjectedRule();
	}

	#endregion

	/// <summary>
	/// Runs ONLY the JSON credential-property rule over <paramref name="text"/>, leaving paths, URIs,
	/// hosts and key=value pairs exactly as they were found.
	/// </summary>
	/// <remarks>
	/// For a caller that must keep a payload's full fidelity but cannot publish a secret with it — the MCP
	/// e2e harness writes an unreadable tool result to a file that the CI build publishes as an artifact,
	/// and that result can be a <c>show-webApp-list</c> envelope carrying a registered environment's
	/// password and client secret (PR #1539).
	/// <para>
	/// One anchored pass, not the full chain <see cref="Redact"/> runs: the chain's ten-plus passes over a
	/// multi-megabyte input are what exhausted the regex budget and collapsed the whole diagnostic into a
	/// bare placeholder (issue #1537). This rule begins each match on a complete quote spelling and cannot
	/// backtrack across the document, so it does not reinstate that cost.
	/// </para>
	/// <para>
	/// The same ACCEPTED LIMITS stated on <c>JsonCredentialPropertyRegex</c> apply unchanged, and one more
	/// follows from running this rule alone: a credential written as a <c>key=value</c> pair rather than a
	/// JSON property is NOT touched here. This is a narrowing of the blast radius, not a full scrub.
	/// </para>
	/// <para>
	/// On a regex timeout this FAILS CLOSED like every other entry point — the whole text is replaced by
	/// the placeholder, so a caller gets no text rather than unscrubbed text. A caller that would rather
	/// keep the original must decide that for itself; this method will not hand back an input it did not
	/// finish scanning.
	/// </para>
	/// </remarks>
	/// <param name="text">The text to scrub. <see langword="null"/>/empty returns <see cref="string.Empty"/>.</param>
	/// <returns>The text with every credential-keyed JSON property's value replaced.</returns>
	public static string RedactJsonCredentialProperties(string? text) {
		if (string.IsNullOrEmpty(text)) {
			return string.Empty;
		}
		return ExecuteRegex(nameof(RedactJsonCredentialProperties), text.Length, () => {
			string result = ExtraJson(MasterChain.RedactJsonCredentialProperties(text));
			return ApplyInjectedRule(JsonLastStage, result);
		});
	}

	/// <summary>
	/// The CONSOLE-facing variant of <see cref="Redact"/> for text whose PROSE clio itself wrote: it runs
	/// the credential rules only - embedded URI userinfo, JWTs, <c>Bearer</c> values and
	/// <c>key=value</c> secret pairs - and deliberately leaves absolute paths, scheme-less
	/// <c>host:port</c> endpoints, e-mail addresses and plain URLs intact.
	/// </summary>
	/// <remarks>
	/// Issue #1505. Wrapping <see cref="ExceptionReadableMessageExtension"/>'s non-carrier arms in the full
	/// <see cref="Redact"/> also scrubbed the operator's OWN local paths and endpoints, which are not
	/// secrets on their own terminal and are the entire content of the diagnosis. Measured:
	/// <c>clio compress /Users/&lt;user&gt;/nope1505dir -d /tmp/x.gz</c> printed
	/// <c>Could not find a part of the path '[redacted-path]'.</c> - an error that names nothing.
	/// <para>
	/// The full <see cref="Redact"/> stays the rule for text a SERVER authored and for anything copied into
	/// an MCP envelope, a log an operator pastes into a ticket, or a third-party LLM's context: there a path
	/// or a host IS a leak. This variant is only for a line whose sole reader is the person who typed the
	/// command.
	/// </para>
	/// <para>
	/// <see cref="UriRegex"/> is reused rather than duplicated, with a match evaluator that removes only the
	/// <c>user:pass@</c> authority prefix and keeps the host - so <c>https://user:pw@host/x</c> loses the
	/// credential and stays diagnosable. A matched URI whose authority carries no <c>@</c> is returned
	/// untouched, host, port, path and query included.
	/// </para>
	/// </remarks>
	/// <param name="text">The raw text of a console line clio composed itself.</param>
	/// <returns>The text with credential shapes replaced; <see cref="string.Empty"/> for null/empty input.</returns>
	public static string RedactCredentials(string? text) {
		if (string.IsNullOrEmpty(text)) {
			return string.Empty;
		}
		return ExecuteRegex(nameof(RedactCredentials), text.Length, () => {
			string result = ApplyInjectedRule(CredentialsFirstStage, text);
			result = MasterChain.RedactCredentials(result);
			result = ExtraCredentials(result);
			return ApplyInjectedRule(CredentialsLastStage, result);
		});
	}

	// What clio's RedactJsonCredentialProperties removes beyond master's: the compound credential keys.
	internal static string ExtraJson(string text) => ApplyRule(text, JsonCredentialPropertyRegex(), JsonValueEdits);

	// What clio's RedactCredentials removes beyond master's: the credential rules of Extra, and of a URI only its userinfo.
	internal static string ExtraCredentials(string text) {
		string result = ApplyRule(text, ":", JsonCredentialPropertyRegex(), JsonValueEdits);
		result = ApplyRule(result, "://", UriRegex(), UserInfoEdits);
		result = ApplyRule(result, "eyJ", JwtRegex(), match => Whole(match, RedactedValue));
		result = ApplyRule(result, "bearer", BearerCredentialRegex(), BearerEdits, StringComparison.OrdinalIgnoreCase);
		string schemed = result;
		result = ApplyRule(result, SchemeCredentialRegex(), match => SchemeEdits(schemed, match));
		result = ApplyRule(result, "digest", DigestCredentialRegex(), DigestEdits, StringComparison.OrdinalIgnoreCase);
		string listed = result;
		result = ApplyRule(result, "=", CredentialListPairRegex(), match => ListPairEdits(listed, match));
		string paired = result;
		result = ApplyRuleWhen(result, PairSeparatorCondition, HasPairSeparator, CredentialPairRegex(), match => PairEdits(paired, match));
		return AfterMasterPlaceholders(result);
	}

	/// <summary>
	/// Returns a matched URI unchanged unless its authority carries userinfo, in which case only the
	/// <c>user:pass@</c> prefix is replaced and the host, port, path and query survive.
	/// </summary>
	/// <remarks>
	/// The authority ends at the first <c>/</c>, <c>?</c> or <c>#</c>. Cutting at <c>/</c> alone was wrong
	/// on a measured input: <c>https://host:88?u=john@acme.com</c> has no <c>/</c> at all, so the authority
	/// ran into the query, the <c>@</c> of an e-mail was taken for userinfo, and the host was replaced
	/// instead - leaving <c>https://[redacted]@acme.com</c>, which names the wrong endpoint.
	/// <c>LastIndexOf</c> is searched inside those bounds because a password may itself contain <c>@</c>.
	/// A raw <c>/</c> inside userinfo is not a valid URI (RFC 3986 requires it percent-encoded) and is not
	/// handled: such a value ends the authority early and the pair is returned unchanged.
	/// </remarks>
	private static IEnumerable<SpanEdit> UserInfoEdits(Match match) {
		string value = match.Value;
		int authorityStart = value.IndexOf("://", StringComparison.Ordinal) + 3;
		int authorityEnd = value.IndexOfAny(['/', '?', '#'], authorityStart);
		if (authorityEnd < 0) {
			authorityEnd = value.Length;
		}
		int at = value.LastIndexOf('@', authorityEnd - 1, authorityEnd - authorityStart);
		return at <= authorityStart ? NoEdit : [new SpanEdit(match.Index + authorityStart, at - authorityStart, RedactedValue)];
	}

	/// <summary>
	/// Rewrites a credential-keyed JSON property as <c>"key":"[redacted]"</c> in the SAME quote spelling
	/// the match was written in, so a document written with one spelling never gains a second one.
	/// </summary>
	// A JSON credential property master's JSON rule did not know (a compound key): its value goes - a string's content, or
	// a literal whole. A literal becomes the bare placeholder, so such a property is no JSON value any more; master's own
	// keys never reach this rule.
	private static IEnumerable<SpanEdit> JsonValueEdits(Match match) =>
		match.Groups["literal"].Success ? Part(match.Groups["literal"], RedactedValue) : Part(match.Groups["value"], RedactedValue);

	#region The one edit function

	/// <summary>
	/// One edit: the span of <paramref name="Length"/> characters at <paramref name="Start"/> becomes the placeholder.
	/// A <paramref name="Collapse"/> edit becomes ONE placeholder, brackets and placeholders inside it included: the site
	/// cut's, whose tail is a stack trace or a page that only the placeholder may stand for.
	/// </summary>
	internal readonly record struct SpanEdit(int Start, int Length, string Placeholder, bool Collapse = false);

	// What an edit never deletes: one of clio's placeholders, a fence word and a bracket. They stay in place, and every
	// run of other characters between them becomes the placeholder.
	[GeneratedRegex(@"\[redacted(?:-[a-z]+)?\]|\[untrusted-source-text (?:begin|end)\]|[()\[\]{}]",
		RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex ProtectedPieceRegex();

	[GeneratedRegex(@"\[redacted(?:-[a-z]+)?\]|\[untrusted-source-text (?:begin|end)\]", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
	private static partial Regex PlaceholderPieceRegex();

	/// <summary>
	/// THE ONE EDIT FUNCTION every rule after master's chain goes through (Extra, Site, ExtraCredentials and the extra
	/// JSON rule): it replaces each span with its placeholder and nothing else.
	/// </summary>
	/// <remarks>
	/// The output is the input's own pieces in their order, with a placeholder at every join: inside a replaced span a
	/// placeholder, a fence word and a bracket stay, and every run between them becomes the placeholder; an edit that
	/// starts or ends inside a placeholder is widened to the whole placeholder; edits that overlap are joined into one
	/// span, which takes the placeholder of the edit that starts first. So no rule can delete a placeholder or a bracket,
	/// and every run of letters or digits in the output outside a placeholder is a piece of the input. The one exception
	/// is a <see cref="SpanEdit.Collapse"/> edit, which becomes a single placeholder.
	/// Linear in the text and the edits: the edits are sorted once, the placeholders are found once in text order and
	/// each widening is a binary search over them. It checks the whole-call budget (<see cref="CheckBudget"/>).
	/// </remarks>
	internal static string ApplyEdits(string text, IEnumerable<SpanEdit> edits) {
		List<SpanEdit> sorted = edits.Where(edit => edit.Length > 0).ToList();
		if (sorted.Count == 0) {
			return text;
		}
		sorted.Sort(static (left, right) => left.Start != right.Start ? left.Start.CompareTo(right.Start) : right.Length.CompareTo(left.Length));
		MatchCollection found = ForLength(PlaceholderPieceRegex(), text.Length).Matches(text);
		Work += text.Length + sorted.Count + found.Count;
		int[] placeholderStarts = new int[found.Count];
		int[] placeholderEnds = new int[found.Count];
		for (int index = 0; index < found.Count; index++) {
			placeholderStarts[index] = found[index].Index;
			placeholderEnds[index] = found[index].Index + found[index].Length;
		}
		StringBuilder output = new(text.Length);
		int position = 0;
		int current = 0;
		int spans = 0;
		while (current < sorted.Count) {
			if ((++spans & 255) == 0) {
				CheckBudget();
			}
			SpanEdit first = sorted[current];
			int start = WidenToPlaceholder(first.Start, placeholderStarts, placeholderEnds, true);
			int end = WidenToPlaceholder(first.Start + first.Length, placeholderStarts, placeholderEnds, false);
			bool collapse = first.Collapse;
			current++;
			// Join every edit that starts inside the span so far (an overlap), however far it reaches.
			while (current < sorted.Count && sorted[current].Start < end) {
				int nextEnd = WidenToPlaceholder(sorted[current].Start + sorted[current].Length, placeholderStarts, placeholderEnds, false);
				end = Math.Max(end, nextEnd);
				collapse |= sorted[current].Collapse;
				current++;
			}
			start = Math.Max(start, position);
			if (start >= end) {
				continue;
			}
			output.Append(text, position, start - position);
			if (collapse) {
				output.Append(first.Placeholder);
			}
			else {
				AppendReplaced(output, text, start, end, first.Placeholder);
			}
			position = end;
		}
		output.Append(text, position, text.Length - position);
		return output.ToString();
	}

	// The position widened to the whole placeholder it falls strictly inside, if any: a start moves to the placeholder's
	// start, an end to its end. The placeholders do not overlap and are in text order, so a binary search finds the one.
	// Each placeholder examined is one unit of work (Work), so the work counter sees the search's cost.
	private static int WidenToPlaceholder(int at, int[] starts, int[] ends, bool isStart) {
		int low = 0;
		int high = starts.Length - 1;
		int index = -1;
		while (low <= high) {
			Work++;
			int middle = low + (high - low) / 2;
			if (starts[middle] < at) {
				index = middle;
				low = middle + 1;
			}
			else {
				high = middle - 1;
			}
		}
		if (index < 0 || at >= ends[index]) {
			return at;
		}
		return isStart ? starts[index] : ends[index];
	}

	// A run next to a kept placeholder needs no placeholder of its own: the kept one already marks the join.
	private static void AppendReplaced(StringBuilder output, string text, int start, int end, string placeholder) {
		int position = start;
		bool afterPlaceholder = false;
		int pieces = 0;
		foreach (Match kept in ForLength(ProtectedPieceRegex(), end - start).Matches(text[start..end])) {
			Work++;
			if ((++pieces & 1023) == 0) {
				CheckBudget();
			}
			int keptAt = start + kept.Index;
			bool keptIsPlaceholder = kept.Length > 1;
			if (keptAt > position && !afterPlaceholder && !keptIsPlaceholder) {
				output.Append(placeholder);
			}
			output.Append(kept.Value);
			position = keptAt + kept.Length;
			afterPlaceholder = keptIsPlaceholder;
		}
		if (position < end && !afterPlaceholder) {
			output.Append(placeholder);
		}
	}

	// A rule through the one edit function: every match of the rule over the CURRENT text gives its edits.
	private static string ApplyRule(string text, Regex rule, Func<Match, IEnumerable<SpanEdit>> edits) {
		Work += text.Length;
		CheckBudget();
		List<SpanEdit> all = [];
		int matches = 0;
		foreach (Match match in ForLength(rule, text.Length).Matches(text)) {
			Work++;
			if ((++matches & 255) == 0) {
				CheckBudget();
			}
			all.AddRange(edits(match));
		}
		return all.Count == 0 ? text : ApplyEdits(text, all);
	}

	// A rule that can match only where the text holds NEED, skipped where it does not. Exact: every match of the rule
	// contains NEED (an IgnoreCase rule takes an OrdinalIgnoreCase need), so the output is the same; only a long text
	// without it takes less time. Every prefiltered call is recorded in Prefiltered, and RedactorOptionBTests proves, over
	// the corpus and its upper-case spellings, that a match of the rule implies its need.
	private static string ApplyRule(string text, string need, Regex rule, Func<Match, IEnumerable<SpanEdit>> edits,
			StringComparison comparison = StringComparison.Ordinal) =>
		ApplyRuleWhen(text, $"contains \"{need}\" ({comparison})", value => value.Contains(need, comparison), rule, edits);

	// A rule that can match only where MAY_MATCH holds (a cheap test of a literal every match contains).
	private static string ApplyRuleWhen(string text, string condition, Func<string, bool> mayMatch, Regex rule,
			Func<Match, IEnumerable<SpanEdit>> edits) {
		Prefiltered.TryAdd(rule + "\u0001" + condition, new PrefilteredRule(rule, condition, mayMatch));
		Work++;
		return mayMatch(text) ? ApplyRule(text, rule, edits) : text;
	}

	/// <summary>One prefiltered rule: the rule, its condition in words, and the condition.</summary>
	internal sealed record PrefilteredRule(Regex Rule, string Condition, Func<string, bool> MayMatch);

	/// <summary>Every prefiltered rule that has run, for the test that proves each condition exact.</summary>
	internal static readonly System.Collections.Concurrent.ConcurrentDictionary<string, PrefilteredRule> Prefiltered = new();

	// A pair rule needs its "=" or ":".
	private const string PairSeparatorCondition = "contains \"=\" or \":\"";

	private static bool HasPairSeparator(string text) => text.AsSpan().IndexOfAny('=', ':') >= 0;

	private static IEnumerable<SpanEdit> Whole(Match match, string placeholder) => [new SpanEdit(match.Index, match.Length, placeholder)];

	private static IEnumerable<SpanEdit> Part(Group group, string placeholder) =>
		group.Success && group.Length > 0 ? [new SpanEdit(group.Index, group.Length, placeholder)] : [];

	private static readonly SpanEdit[] NoEdit = [];

	#endregion

	#region The work budget

	/// <summary>
	/// The work this thread's redaction has done, in units that do not depend on the clock: every character a rule or a
	/// step of master's chain scans, every match, every edit, every placeholder found or examined, every piece kept.
	/// Ever increasing; read it before and after a call to see what the call did.
	/// </summary>
	[ThreadStatic]
	internal static long Work;

	/// <summary>How many entry-point calls on this thread failed closed, to <see cref="RedactedTimeout"/>.</summary>
	[ThreadStatic]
	internal static long FailedClosed;

	/// <summary>
	/// The work one entry-point call may do, per character of its input, and the constant on top: a call past
	/// <c>WorkPerCharacter * length + WorkConstant</c> fails closed. It does not depend on the machine or its load, so a
	/// big legitimate answer comes out the same everywhere. The worst shape measured (20 Nov, RedactorTimingTests' shapes
	/// and the differential corpus) does 48.5 units per character (pair-dense text through RedactSiteText); the budget is ten times that, rounded.
	/// </summary>
	internal const long WorkPerCharacter = 500;

	internal const long WorkConstant = 200_000;

	/// <summary>A far backstop on the clock, for a host so starved that even linear work cannot finish.</summary>
	internal static readonly TimeSpan WallClockBackstop = TimeSpan.FromSeconds(60);

	/// <summary>
	/// The input length one second of a rule's match timeout covers on a long input. A rule that never matches scans the
	/// whole text in ONE match attempt, so master's fixed 1 s per attempt turned a long legitimate answer into a timeout on
	/// a loaded host (5 MB took up to 0.9 s per attempt here, idle). For a text longer than this, every rule - master's
	/// chain and clio's - runs with a timeout of one second per this many characters, in power-of-two steps, never under
	/// its own and never over <see cref="WallClockBackstop"/>. Below it, every rule keeps its own timeout. The timeout
	/// changes no output: only where an input would have failed closed.
	/// </summary>
	internal const int CharactersPerTimeoutSecond = 250_000;

	private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Pattern, RegexOptions Options, int Seconds), Regex>
		LongInputRules = new();

	/// <summary>The rule to run over <paramref name="length"/> characters: itself, or the same pattern and options with the
	/// timeout <see cref="CharactersPerTimeoutSecond"/> gives a long input.</summary>
	internal static Regex ForLength(Regex rule, int length) {
		if (length <= CharactersPerTimeoutSecond) {
			return rule;
		}
		int seconds = 1;
		while (seconds < WallClockBackstop.TotalSeconds && seconds * (long)CharactersPerTimeoutSecond < length) {
			seconds *= 2;
		}
		seconds = (int)Math.Min(seconds, WallClockBackstop.TotalSeconds);
		if (seconds <= rule.MatchTimeout.TotalSeconds) {
			return rule;
		}
		return LongInputRules.GetOrAdd((rule.ToString(), rule.Options, seconds),
			key => new Regex(key.Pattern, key.Options | RegexOptions.Compiled, TimeSpan.FromSeconds(key.Seconds)));
	}

	[ThreadStatic]
	private static long workLimit;

	[ThreadStatic]
	private static long wallDeadline;

	[ThreadStatic]
	private static int budgetDepth;

	[ThreadStatic]
	private static long? injectedWorkBudget;

	/// <summary>Raised when a call runs past its work budget or the clock backstop; the outermost guard turns it into
	/// <see cref="RedactedTimeout"/>.</summary>
	internal sealed class RedactionBudgetExceededException(string budget) : Exception(budget);

	/// <summary>Throws <see cref="RedactionBudgetExceededException"/> when the current call is past its budget.</summary>
	internal static void CheckBudget() {
		if (workLimit != 0 && Work > workLimit) {
			throw new RedactionBudgetExceededException("work budget");
		}
		if (wallDeadline != 0 && System.Diagnostics.Stopwatch.GetTimestamp() > wallDeadline) {
			throw new RedactionBudgetExceededException("clock backstop");
		}
	}

	/// <summary>Test seam: the next entry-point calls on this thread get <paramref name="units"/> of work each.</summary>
	internal static IDisposable InjectWorkBudgetForTests(long units) {
		injectedWorkBudget = units;
		return new InjectedBudgetScope();
	}

	private sealed class InjectedBudgetScope : IDisposable {
		public void Dispose() => injectedWorkBudget = null;
	}

	/// <summary>Where a fail-closed result is logged: once, with the entry point and the input length, never the text.</summary>
	internal static Action<string> FailedClosedLog = message => ConsoleLogger.Instance.WriteWarning(message);

	private static string FailClosed(string entryPoint, int inputLength, string reason) {
		FailedClosed++;
		FailedClosedLog?.Invoke($"clio redacted a text whole: {entryPoint} on {inputLength} characters stopped on its {reason}.");
		return RedactedTimeout;
	}

	#endregion

	/// <summary>The guard of a nested or internal redaction, which shares the budget of the entry point around it.</summary>
	internal static string ExecuteRegex(Func<string> operation) => ExecuteRegex("a redaction", -1, operation);

	/// <summary>
	/// The guard of every entry point: a regex timeout anywhere, or a call past its work budget or the clock backstop,
	/// gives <see cref="RedactedTimeout"/> for the whole text. The outermost guard on a thread sets the budget from the
	/// input length (none when the length is not known); a nested one shares it, and a budget overrun goes up to the
	/// outermost, so no partly redacted text comes out.
	/// </summary>
	internal static string ExecuteRegex(string entryPoint, int inputLength, Func<string> operation) {
		bool outermost = budgetDepth++ == 0;
		if (outermost) {
			workLimit = inputLength < 0 && injectedWorkBudget is null
				? 0
				: Work + (injectedWorkBudget ?? WorkPerCharacter * inputLength + WorkConstant);
			wallDeadline = System.Diagnostics.Stopwatch.GetTimestamp()
				+ (long)(WallClockBackstop.TotalSeconds * System.Diagnostics.Stopwatch.Frequency);
		}
		try {
			return operation();
		}
		catch (RegexMatchTimeoutException) {
			return FailClosed(entryPoint, inputLength, "regex timeout");
		}
		catch (RedactionBudgetExceededException exception) when (outermost) {
			return FailClosed(entryPoint, inputLength, exception.Message);
		}
		finally {
			if (--budgetDepth == 0) {
				workLimit = 0;
				wallDeadline = 0;
			}
		}
	}
}
