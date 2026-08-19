using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Folds a resolved Creatio target URI into the canonical TARGET identity used by the MCP session key
/// (ENG-95262 story 7, AC-00). One resolved target must produce ONE identity whether it was reached by
/// registered environment name or by explicit URI; two targets that are not provably the same must never
/// produce one.
/// </summary>
/// <remarks>
/// <para>
/// The algorithm is the binding component-by-component table in the credential threat model
/// <c>T-5 — Target normalisation collision</c>. It is deliberately CONSERVATIVE: over-normalising merges
/// two targets, and on a sticky worker that is a CREDENTIAL CROSSOVER rather than a cache miss. Anything
/// T-5 does not name is left byte-exact and therefore distinguishing — when in doubt, spawn another
/// worker; the cost is 0.7 s.
/// </para>
/// <list type="table">
///   <listheader><term>Component</term><description>Rule</description></listheader>
///   <item><term>Scheme</term><description>lowercased (folded); <c>http</c> and <c>https</c> stay DISTINCT — a downgrade is a different security context</description></item>
///   <item><term>Host, ASCII</term><description>lowercased (DNS is case-insensitive)</description></item>
///   <item><term>Host, non-ASCII</term><description>IDNA 2008 A-label (Punycode, <c>UseStd3AsciiRules</c>) then lowercased</description></item>
///   <item><term>Host, IPv6 literal</term><description>RFC 5952 canonical form, brackets kept</description></item>
///   <item><term>Host, IPv4 literal</term><description>canonical dotted-quad only; octal / decimal-integer / <c>0x</c> forms are REJECTED, not normalised</description></item>
///   <item><term>Host vs IP</term><description>a hostname and an IP address are different targets even when DNS resolves one to the other</description></item>
///   <item><term>Port</term><description>the scheme default (<c>:80</c> for http, <c>:443</c> for https) is elided; any other port matches exactly</description></item>
///   <item><term>Userinfo</term><description>REJECTED — credentials never travel in the target (T-1, T-2)</description></item>
///   <item><term>Path</term><description>percent-encoding normalised (uppercase hex, unreserved characters decoded per RFC 3986 §6.2.2.2), dot segments resolved, exactly one trailing <c>/</c> stripped; path CASE is preserved — Creatio paths are case-sensitive</description></item>
///   <item><term>Query, fragment</term><description>REJECTED — a target is an origin plus base path, never a query</description></item>
/// </list>
/// <para>
/// Rejection means the call FAILS CLOSED with an explicit <see cref="EnvironmentResolutionException"/>;
/// it is never a silent fallback to a looser key.
/// </para>
/// </remarks>
public interface ISessionTargetNormalizer {

	/// <summary>
	/// Folds <paramref name="target"/> into its canonical session-target identity per the T-5 table.
	/// </summary>
	/// <param name="target">The resolved target URI (<c>EnvironmentSettings.Uri</c>).</param>
	/// <returns>
	/// The canonical identity — <c>scheme://host[:port][path]</c>. An input that is not an absolute
	/// hierarchical URI at all is returned byte-exact behind an opaque marker prefix, which is strictly
	/// more distinguishing than the input and therefore cannot merge two targets.
	/// </returns>
	/// <exception cref="EnvironmentResolutionException">
	/// The target carries userinfo, a query or a fragment, or a non-canonical IPv4 literal. These are the
	/// four T-5 rejections; each one fails the call rather than falling back to a looser key.
	/// </exception>
	string Normalize(string target);
}

/// <inheritdoc />
public sealed class SessionTargetNormalizer : ISessionTargetNormalizer {

	/// <summary>
	/// Marker for an input that is not an absolute hierarchical URI and therefore cannot be decomposed
	/// into the T-5 components. The value is carried byte-exact behind this prefix: strictly more
	/// distinguishing than the raw input, so the safety valve can only ever cost an extra worker, never
	/// merge two targets.
	/// </summary>
	/// <remarks>
	/// <b>The prefix contains a character no URI scheme may contain, and that is load-bearing.</b> An
	/// earlier version used the plain text <c>raw:</c> and claimed "a normalised identity never starts
	/// with it" — which was false, because <c>raw</c> is a syntactically valid scheme name. So
	/// <c>//x</c> (schemeless, opaque) and <c>raw://x</c> (scheme <c>raw</c>, normalised) both produced
	/// <c>raw://x</c>: two distinct targets sharing one session key, which on a sticky worker is the
	/// credential crossover T-5 exists to prevent rather than a cache miss. RFC 3986 restricts a scheme
	/// to ALPHA / DIGIT / "+" / "-" / "." — a control character can never appear in one, so no normalised
	/// identity can collide with this marker and the invariant the summary states is now actually true.
	/// </remarks>
	private const string OpaqueTargetPrefix = "\u0000raw:";

	private static readonly char[] AuthorityTerminators = ['/', '?', '#'];

	/// <inheritdoc />
	public string Normalize(string target) {
		ArgumentException.ThrowIfNullOrWhiteSpace(target);
		string raw = target.Trim();

		int schemeEnd = raw.IndexOf("://", StringComparison.Ordinal);
		if (schemeEnd <= 0) {
			return Opaque(raw);
		}
		string scheme = raw[..schemeEnd].ToLowerInvariant();
		if (!IsSchemeName(scheme)) {
			return Opaque(raw);
		}

		string rest = raw[(schemeEnd + 3)..];
		int authorityEnd = rest.IndexOfAny(AuthorityTerminators);
		string authority = authorityEnd < 0 ? rest : rest[..authorityEnd];
		string remainder = authorityEnd < 0 ? string.Empty : rest[authorityEnd..];

		// The three textual rejections run FIRST, before any structural doubt can divert the input into
		// the opaque safety valve: fail-closed beats byte-exact when the component is one T-5 forbids.
		if (authority.Contains('@')) {
			throw Reject("userinfo (a 'user:password@' prefix)");
		}
		if (remainder.Contains('?')) {
			throw Reject("a query string");
		}
		if (remainder.Contains('#')) {
			throw Reject("a fragment");
		}
		if (authority.Length == 0 || !TrySplitAuthority(authority, out string hostText, out string portText)) {
			return Opaque(raw);
		}

		string host = NormalizeHost(hostText);
		string port = NormalizePort(scheme, portText);
		string path = NormalizePath(remainder);
		return string.Concat(scheme, "://", host, port, path);
	}

	private static bool IsSchemeName(string scheme) {
		if (!char.IsAsciiLetter(scheme[0])) {
			return false;
		}
		return scheme.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.');
	}

	// Splits an authority (userinfo already rejected) into its host and its ":port" suffix. Returns false
	// for anything it cannot split unambiguously — an unterminated IPv6 bracket, a bare unbracketed IPv6
	// literal, an empty host, or a non-numeric port — so the caller can fall back to the opaque identity
	// instead of guessing.
	private static bool TrySplitAuthority(string authority, out string host, out string port) {
		host = authority;
		port = string.Empty;
		if (authority[0] == '[') {
			int close = authority.IndexOf(']');
			if (close < 0) {
				return false;
			}
			host = authority[..(close + 1)];
			port = authority[(close + 1)..];
		}
		else {
			int colon = authority.LastIndexOf(':');
			if (colon >= 0) {
				host = authority[..colon];
				port = authority[colon..];
			}
			if (host.Contains(':')) {
				return false;
			}
		}
		if (host.Length == 0) {
			return false;
		}
		if (port.Length == 0) {
			return true;
		}
		if (port[0] != ':') {
			return false;
		}
		for (int i = 1; i < port.Length; i++) {
			if (!char.IsAsciiDigit(port[i])) {
				return false;
			}
		}
		return true;
	}

	// The host is normalised from the RAW authority text, never from System.Uri.Host or IPAddress.Parse.
	// Both of those silently perform the exact fold T-5 rejects — measured on this repo's target framework
	// on 2026-08-18: new Uri("http://0177.0.0.1/").Host and IPAddress.Parse("0177.0.0.1").ToString() BOTH
	// return "127.0.0.1". Reading the host through either API would accept an octal literal as if it were
	// the canonical dotted-quad and merge two targets. Do not "simplify" this back to Uri.Host.
	private static string NormalizeHost(string hostText) {
		if (hostText[0] == '[') {
			string inner = hostText[1..^1];
			// The ZONE is split off BEFORE parsing and re-appended byte-exact. Measured on net8.0, because
			// the earlier comment here guessed the opposite and the guess was the defect:
			//
			//   IPAddress.TryParse("fe80::1%ethA")  -> succeeds, ToString() gives "fe80::1"
			//   IPAddress.TryParse("fe80::1%ethB")  -> succeeds, ToString() gives "fe80::1"
			//   IPAddress.TryParse("fe80::1%3")     -> succeeds, ToString() gives "fe80::1%3"
			//
			// So a NAMED zone parses fine and is silently discarded, and two different link-local
			// destinations fold into one target — a credential crossover on a sticky worker rather than a
			// cache miss (T-5). A numeric zone happens to survive, which is exactly the kind of accident
			// that makes the named case easy to miss. T-5 names no rule for zones, and its default is
			// explicit: anything the algorithm does not name is left byte-exact and therefore
			// distinguishing.
			int zoneStart = inner.IndexOf('%');
			string addressText = zoneStart < 0 ? inner : inner[..zoneStart];
			string zone = zoneStart < 0 ? string.Empty : inner[zoneStart..];
			if (IPAddress.TryParse(addressText, out IPAddress address)
				&& address.AddressFamily == AddressFamily.InterNetworkV6) {
				// IPAddress.ToString() emits the RFC 5952 canonical form (lowercase hex, "::" at the
				// longest zero run). It is managed code, so the result is identical on macOS, Linux and
				// Windows — unlike the platform-backed IDNA path below.
				return string.Concat("[", address.ToString(), zone, "]");
			}
			// A bracketed literal this runtime cannot parse is not an RFC 5952 form we may canonicalise;
			// hex case is the only fold T-5 names for it. The zone rides along untouched for the same
			// reason as above — it is case-sensitive on Unix, so it is not lowercased either.
			return string.Concat("[", addressText.ToLowerInvariant(), zone, "]");
		}
		if (LooksLikeIPv4Literal(hostText)) {
			if (!IsCanonicalDottedQuad(hostText)) {
				throw Reject("a non-canonical IPv4 literal (an octal, decimal-integer or 0x form)");
			}
			return hostText;
		}
		return NormalizeRegisteredName(hostText);
	}

	// A registered name is folded by case only; a non-ASCII name is first converted to its IDNA 2008
	// A-label so the Unicode and Punycode spellings of ONE host converge. IdnMapping is created per call
	// rather than cached: it is not documented as thread-safe and this runs once per resolve.
	private static string NormalizeRegisteredName(string host) {
		if (Ascii.IsValid(host)) {
			return host.ToLowerInvariant();
		}
		try {
			return new IdnMapping { UseStd3AsciiRules = true }.GetAscii(host).ToLowerInvariant();
		}
		catch (ArgumentException) {
			// Not a valid IDN name (an underscore label, for instance). Folding case is still safe;
			// inventing an A-label is not.
			return host.ToLowerInvariant();
		}
	}

	// WHATWG's rule, and the one that keeps the T-5 IPv4 rejection from swallowing ordinary hostnames: a
	// host is an IPv4-literal ATTEMPT when its LAST label is all digits or is 0x-prefixed hex. That admits
	// "0177.0.0.1", "2130706433", "0x7f.0.0.1" and "127.1" for rejection while leaving "1and1.com" and the
	// all-hex-looking "face.beef" as ordinary registered names.
	private static bool LooksLikeIPv4Literal(string host) {
		string lastLabel = host[(host.LastIndexOf('.') + 1)..];
		if (lastLabel.Length == 0) {
			return false;
		}
		return lastLabel.All(char.IsAsciiDigit) || IsHexPrefixed(lastLabel);
	}

	private static bool IsHexPrefixed(string label) {
		if (label.Length <= 2 || label[0] != '0' || (label[1] != 'x' && label[1] != 'X')) {
			return false;
		}
		for (int i = 2; i < label.Length; i++) {
			if (!char.IsAsciiHexDigit(label[i])) {
				return false;
			}
		}
		return true;
	}

	private static bool IsCanonicalDottedQuad(string host) {
		string[] labels = host.Split('.');
		if (labels.Length != 4) {
			return false;
		}
		foreach (string label in labels) {
			if (label.Length is 0 or > 3) {
				return false;
			}
			// A leading zero is the octal form T-5 rejects, so "01" is not the canonical spelling of 1.
			if (label.Length > 1 && label[0] == '0') {
				return false;
			}
			if (!label.All(char.IsAsciiDigit)) {
				return false;
			}
			// Cannot overflow: the length and all-digit checks above cap the label at 999.
			if (int.Parse(label, CultureInfo.InvariantCulture) > 255) {
				return false;
			}
		}
		return true;
	}

	// Only the scheme DEFAULT is elided. Every other port matches exactly, so ":8080" and no port stay two
	// targets, and ":443" on an http target is NOT the http default and is therefore kept.
	private static string NormalizePort(string scheme, string portText) {
		if (portText.Length <= 1) {
			return string.Empty;
		}
		if (!int.TryParse(portText.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int port)) {
			return portText;
		}
		int defaultPort = scheme switch {
			"http" => 80,
			"https" => 443,
			_ => -1
		};
		return port == defaultPort ? string.Empty : string.Concat(":", port.ToString(CultureInfo.InvariantCulture));
	}

	// RFC 3986 §6.2.2 order: normalise percent-encoding (decoding only unreserved octets) BEFORE removing
	// dot segments, so an encoded "%2E%2E" is resolved as the "..' it denotes rather than left to look like
	// a literal segment. Exactly ONE trailing slash is then stripped, so "/app/" folds onto "/app" while
	// "/app//" does not.
	private static string NormalizePath(string path) {
		if (path.Length == 0) {
			return string.Empty;
		}
		string resolved = RemoveDotSegments(NormalizePercentEncoding(path));
		if (resolved.Length > 1 && resolved[^1] == '/') {
			return resolved[..^1];
		}
		return resolved == "/" ? string.Empty : resolved;
	}

	private static string NormalizePercentEncoding(string value) {
		StringBuilder builder = new(value.Length);
		for (int i = 0; i < value.Length; i++) {
			char current = value[i];
			if (current != '%' || i + 2 >= value.Length
				|| !char.IsAsciiHexDigit(value[i + 1]) || !char.IsAsciiHexDigit(value[i + 2])) {
				builder.Append(current);
				continue;
			}
			char decoded = (char)int.Parse(value.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
			if (IsUnreserved(decoded)) {
				builder.Append(decoded);
			}
			else {
				builder.Append('%')
					.Append(char.ToUpperInvariant(value[i + 1]))
					.Append(char.ToUpperInvariant(value[i + 2]));
			}
			i += 2;
		}
		return builder.ToString();
	}

	private static bool IsUnreserved(char c) =>
		char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~';

	// RFC 3986 §5.2.4, verbatim. Empty segments are preserved, so "/app//" keeps both slashes and stays a
	// different target from "/app/".
	private static string RemoveDotSegments(string path) {
		string input = path;
		StringBuilder output = new(path.Length);
		while (input.Length > 0) {
			if (input.StartsWith("../", StringComparison.Ordinal)) {
				input = input[3..];
			}
			else if (input.StartsWith("./", StringComparison.Ordinal)) {
				input = input[2..];
			}
			else if (input.StartsWith("/./", StringComparison.Ordinal)) {
				input = string.Concat("/", input[3..]);
			}
			else if (input == "/.") {
				input = "/";
			}
			else if (input.StartsWith("/../", StringComparison.Ordinal)) {
				input = string.Concat("/", input[4..]);
				RemoveLastSegment(output);
			}
			else if (input == "/..") {
				input = "/";
				RemoveLastSegment(output);
			}
			else if (input is "." or "..") {
				input = string.Empty;
			}
			else {
				int next = input.IndexOf('/', input[0] == '/' ? 1 : 0);
				if (next < 0) {
					output.Append(input);
					input = string.Empty;
				}
				else {
					output.Append(input[..next]);
					input = input[next..];
				}
			}
		}
		return output.ToString();
	}

	private static void RemoveLastSegment(StringBuilder output) {
		for (int i = output.Length - 1; i >= 0; i--) {
			if (output[i] == '/') {
				output.Length = i;
				return;
			}
		}
		output.Length = 0;
	}

	private static string Opaque(string raw) => string.Concat(OpaqueTargetPrefix, raw);

	// The message names the REASON and never echoes the offending value: a rejected target can carry
	// userinfo, and T-6 forbids a credential reaching a log, an error envelope or a test snapshot.
	private static EnvironmentResolutionException Reject(string reason) =>
		new($"The target URI cannot be used as an MCP session target because it contains {reason}. "
			+ "A target is a scheme, host, optional port and base path only — supply credentials through the "
			+ "registered environment or the credential channel, drop any query string or fragment, and write "
			+ "an IPv4 literal in canonical dotted-quad form (for example 127.0.0.1).");
}
