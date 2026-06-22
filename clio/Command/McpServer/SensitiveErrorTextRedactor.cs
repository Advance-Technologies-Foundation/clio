using System.Text.RegularExpressions;

namespace Clio.Command.McpServer;

/// <summary>
/// Scrubs sensitive tokens out of an exception-derived message before it is surfaced to the MCP
/// client. The MCP tool result is copied verbatim into the model/host transcript and is frequently
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

	private const string RedactedUri = "[redacted-uri]";
	private const string RedactedPath = "[redacted-path]";
	private const string RedactedValue = "[redacted]";

	// scheme://[user[:pass]@]host[:port][/path…] — also catches credentials embedded in the authority.
	[GeneratedRegex(@"\b[a-zA-Z][a-zA-Z0-9+.\-]*://[^\s""'<>]+", RegexOptions.CultureInvariant)]
	private static partial Regex UriRegex();

	// Windows drive-rooted (C:\…) and UNC (\\host\share\…) absolute paths.
	[GeneratedRegex(@"(?:[A-Za-z]:\\|\\\\)[^\s""'<>|]*", RegexOptions.CultureInvariant)]
	private static partial Regex WindowsPathRegex();

	// POSIX absolute paths under well-known home/system roots only, so generic URL fragments
	// (e.g. "/rest/CreatioApiGateway/…") and prose are left intact.
	[GeneratedRegex(@"/(?:Users|home|root|var|etc|opt|usr|tmp|private|mnt|srv)(?:/[^\s""'<>:]*)+", RegexOptions.CultureInvariant)]
	private static partial Regex PosixPathRegex();

	// key=value / key: value pairs whose key denotes a secret or a connection-string host/db; the
	// key is kept (so the message still reads sensibly) and only the value is redacted.
	[GeneratedRegex(
		@"\b(password|pwd|pass|secret|token|api[_-]?key|client[_-]?secret|access[_-]?key|connection ?string|data ?source|server|host|hostname|initial ?catalog|database|uid|user ?id)\b\s*[=:]\s*[^\s,;""']+",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex CredentialPairRegex();

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
		// URIs first: a scheme://user:pass@host authority must be removed whole before the narrower
		// path/credential passes run, so its embedded host/credentials never survive.
		string result = UriRegex().Replace(text, RedactedUri);
		result = WindowsPathRegex().Replace(result, RedactedPath);
		result = PosixPathRegex().Replace(result, RedactedPath);
		result = CredentialPairRegex().Replace(result, match => $"{match.Groups[1].Value}={RedactedValue}");
		return result;
	}
}
