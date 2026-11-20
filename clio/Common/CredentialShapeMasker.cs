using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Clio.Common;

/// <summary>
/// Hides credentials recognisable by their shape in text a process run returns - its error, its log line, a result
/// value: an <c>Authorization: Basic</c> pair, a <c>Bearer</c> token, a JWT, and the value of a secret-named header,
/// pair or JSON property. A key whose name and shape say nothing (an echoed header value on its own) is not
/// recognisable; the caller withholds such values by their source instead.
/// </summary>
/// <remarks>
/// Deliberately narrower than <see cref="SensitiveErrorTextRedactor"/>: a result value is business data, so hosts,
/// databases, identifiers and URLs stay as they are - only what names or looks like a credential is replaced.
/// </remarks>
internal static partial class CredentialShapeMasker {

	private const string Redacted = "[redacted]";
	private const int TimeoutMs = 1_000;

	// "Basic <base64>" whose payload decodes to user:password. "[redacted] <base64>" is the same pair after
	// SensitiveErrorTextRedactor's key=value rule took "Authorization: Basic" for a pair and left the token after it.
	[GeneratedRegex(@"(?<scheme>\bBasic|\[redacted\])(?<gap>\s+)(?<token>[A-Za-z0-9+/]{8,}={0,2})(?![A-Za-z0-9+/=])",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex BasicCredentialRegex();

	// A token-shaped bearer value: long, and carrying a digit, so "pay to bearer on demand" is prose.
	[GeneratedRegex(@"\b(?<scheme>Bearer)(?<gap>\s+)(?<token>(?=[A-Za-z0-9\-._~+/]*[0-9])[A-Za-z0-9\-._~+/]{16,}=*)",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex BearerTokenRegex();

	[GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}",
		RegexOptions.CultureInvariant, TimeoutMs)]
	private static partial Regex JwtRegex();

	private const string SecretKey =
		@"[A-Za-z0-9_.\-]*(?:authorization|api[-_]?key|apikey|access[-_]?token|refresh[-_]?token|auth[-_]?token|"
		+ @"client[-_]?secret|secret|password|passwd|pwd|credential|cookie|session[-_]?id)[A-Za-z0-9_.\-]*";

	// "X-Api-Key": "value" and the like, in an echoed JSON body: the string value goes, the key stays. The end of the
	// text also ends the value, so a body cut short (a column limit, a clamp) does not keep its key.
	[GeneratedRegex("(?<head>\"" + SecretKey + "\"\\s*:\\s*\")(?<value>(?:[^\"\\\\]|\\\\.)*)(?<close>\"|\\z)",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex JsonSecretPropertyRegex();

	// "X-Api-Key: value", "password=value": a header line or a pair. A value that is a scheme word or already
	// replaced is left to the rules above. A backslash ends the value, so an escape inside JSON text survives.
	[GeneratedRegex(@"(?<head>(?<![A-Za-z0-9_\-""])" + SecretKey + @"\s*[:=]\s*)(?!(?:Basic|Bearer|Digest)\b|\[redacted\])"
		+ @"(?<value>[^\s,;&""'<>\\]+)",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex SecretPairRegex();

	[GeneratedRegex("^" + SecretKey + "$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex SecretNameRegex();

	/// <summary>Whether a property or column name names a credential (an API key, a password, a token...).</summary>
	/// <param name="name">The name.</param>
	/// <returns><see langword="true"/> when the value under that name is to be hidden whole.</returns>
	public static bool IsSecretName(string name) =>
		!string.IsNullOrEmpty(name) && name.Length <= 128 && TimeoutMeansSecret(() => SecretNameRegex().IsMatch(name));

	private static bool TimeoutMeansSecret(Func<bool> match) {
		try {
			return match();
		}
		catch (RegexMatchTimeoutException) {
			return true;
		}
	}

	/// <summary>Replaces every credential recognisable by its shape.</summary>
	/// <param name="text">The text; <see langword="null"/> or empty is returned as is.</param>
	/// <returns>The text with each credential replaced by <c>[redacted]</c>; <c>[redacted-timeout]</c> whole on a timeout or past its budget.</returns>
	public static string Mask(string text) {
		if (string.IsNullOrEmpty(text)) {
			return text;
		}
		return SensitiveErrorTextRedactor.ExecuteRegex(nameof(CredentialShapeMasker) + "." + nameof(Mask), text.Length, () => {
			string result = BasicCredentialRegex().Replace(text, match => IsUserPassword(match.Groups["token"].Value)
				? $"{match.Groups["scheme"].Value}{match.Groups["gap"].Value}{Redacted}"
				: match.Value);
			result = BearerTokenRegex().Replace(result, match => $"{match.Groups["scheme"].Value}{match.Groups["gap"].Value}{Redacted}");
			result = JwtRegex().Replace(result, Redacted);
			result = JsonSecretPropertyRegex().Replace(result,
				match => $"{match.Groups["head"].Value}{Redacted}{match.Groups["close"].Value}");
			return SecretPairRegex().Replace(result, match => $"{match.Groups["head"].Value}{Redacted}");
		});
	}

	// Decodes the longest prefix whose length is a multiple of four, so that a token cut short (a clamp, a missing
	// padding) is still recognised, and asks for printable text with a colon after its first character.
	private static bool IsUserPassword(string token) {
		string body = token.TrimEnd('=');
		int usable = body.Length - body.Length % 4;
		string candidate = usable == body.Length ? token : body[..usable];
		if (usable < 4) {
			return false;
		}
		byte[] buffer = new byte[candidate.Length];
		if (!Convert.TryFromBase64String(candidate, buffer, out int written)) {
			return false;
		}
		string decoded;
		try {
			decoded = new UTF8Encoding(false, true).GetString(buffer, 0, written);
		}
		catch (DecoderFallbackException) {
			return false;
		}
		return decoded.IndexOf(':') > 0 && decoded.All(c => !char.IsControl(c));
	}
}
