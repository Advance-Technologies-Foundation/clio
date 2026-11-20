using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Clio.Common;

internal static partial class SensitiveErrorTextRedactor {

	/// <summary>
	/// MASTER'S CHAIN, FROZEN: the patterns, options and call order of master <c>1915d7543</c>'s
	/// <c>SensitiveErrorTextRedactor</c> (<c>clio/Common/SensitiveErrorTextRedactor.cs</c>), copied verbatim into members
	/// of their own. Every entry point runs it first; what clio adds runs after it and may only replace spans of its output
	/// (<see cref="ApplyEdits"/>). Do not edit a pattern here: <c>MasterChainHashTests</c> pins each pattern string, its
	/// options and the order against constants recorded from master's build. A rule that should remove more belongs in
	/// <see cref="Extra"/>, <see cref="Site"/> or <see cref="ExtraCredentials"/>.
	/// </summary>
	internal static partial class MasterChain {

		[GeneratedRegex(@"(?<!\\u?[0-9A-Fa-f]{0,3})(?:(?<=\\u[0-9A-Fa-f]{4})|\b)[a-zA-Z][a-zA-Z0-9+.\-]*://"
			+ @"(?:(?:[A-Za-z0-9._~!$&'()*+,;=:\-]|%[0-9A-Fa-f]{2}|\\u002[67bB])*@)?[^\s""'<>)\\]+",
			RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
		private static partial Regex UriRegex();

		[GeneratedRegex(@"(?:[A-Za-z]:\\|\\\\)[^\s""'<>|]*", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
		private static partial Regex WindowsPathRegex();

		[GeneratedRegex(@"/(?:Users|home|root|var|etc|opt|usr|tmp|private|mnt|srv|Library|Applications|System|app|data|config)(?:/[^\s""'<>:]*)+", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
		private static partial Regex PosixPathRegex();

		private const string CredentialSecretKeys =
			@"password|pwd|pass|secret|(?:access|refresh|id|auth|bearer|api|client|session|[xc]srf)?[_-]?token|"
			+ @"api[_-]?key|client[_-]?secret|client[_-]?id|private[_-]?key|access[_-]?key|connection ?string|"
			+ @"authorization|auth|bearer|set-cookie|cookie|asp\.net_sessionid|aspxauth|bpmcsrf|jsessionid|"
			+ @"phpsessid|session[_-]?id|[xc]srf[_-]?token";

		private const string ConnectionStringPartKeys =
			@"data ?source|server|hostname|host|initial ?catalog|database|uid|user ?id";

		[GeneratedRegex(
			$@"\b({CredentialSecretKeys}|{ConnectionStringPartKeys})\b\s*[=:]\s*(?:""[^""]*""|'[^']*'|[^\s,;""']+)",
			RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
		private static partial Regex CredentialPairRegex();

		private const string JsonCredentialKeyPattern =
			$@"(?:[A-Za-z0-9]+[_.\-])*(?:{CredentialSecretKeys}|server|hostname|host|database)(?:[_.\-][A-Za-z0-9]+)*";

		private const string JsonQuoteSpellings = @"\\u0022|\\""|""";

		private const string JsonStringContent = @"(?:(?!\k<q>)(?:[^""\\]|\\.))*";

		private const string JsonLiteralValue = @"null|true|false|-?\d+(?:\.\d+)?(?:[eE][+\-]?\d+)?";

		[GeneratedRegex(
			$@"(?<q>{JsonQuoteSpellings})(?<key>{JsonCredentialKeyPattern})\k<q>\s*:\s*(?:\k<q>{JsonStringContent}\k<q>|{JsonLiteralValue})",
			RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
		private static partial Regex JsonCredentialPropertyRegex();

		[GeneratedRegex(@"\bBearer\s+[^\s,;""']+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
		private static partial Regex BearerTokenRegex();

		[GeneratedRegex(@"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
		private static partial Regex JwtRegex();

		[GeneratedRegex(
			@"(?<!\\u?[0-9A-Fa-f]{0,3})(?:(?<=\\u[0-9A-Fa-f]{4})|(?<![\w:./@-]))(?:\[[0-9A-Fa-f:]+\]|(?:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?\.)+[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?|\d{1,3}(?:\.\d{1,3}){3}):\d{1,5}\b",
			RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
		private static partial Regex HostPortRegex();

		[GeneratedRegex(@"(?<!\\u?[0-9A-Fa-f]{0,3})[A-Za-z0-9._%+\-]+@(?:\[[^\]\s]{1,45}\]|[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9\-]*[A-Za-z0-9])?)*\.[A-Za-z]{2,}|\d{1,3}(?:\.\d{1,3}){3}(?=:\d)|[A-Za-z][A-Za-z0-9\-]*[A-Za-z0-9](?![A-Za-z0-9\-]))",
			RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
		private static partial Regex EmailRegex();

		/// <summary>Master's Redact chain, in master's order.</summary>
		internal static IReadOnlyList<Regex> RedactOrder => [
			JsonCredentialPropertyRegex(), UriRegex(), JwtRegex(), BearerTokenRegex(), EmailRegex(), HostPortRegex(),
			WindowsPathRegex(), PosixPathRegex(), CredentialPairRegex()
		];

		/// <summary>Master's RedactCredentials chain, in master's order.</summary>
		internal static IReadOnlyList<Regex> CredentialsOrder => [
			JsonCredentialPropertyRegex(), UriRegex(), JwtRegex(), BearerTokenRegex(), CredentialPairRegex()
		];

		/// <summary>Master's RedactJsonCredentialProperties chain.</summary>
		internal static IReadOnlyList<Regex> JsonOrder => [JsonCredentialPropertyRegex()];

		/// <summary>Master's Redact body, without its guard: the caller's single guard covers the whole entry point.</summary>
		internal static string Redact(string text) {
			string result = JsonCredentialPropertyRegex().Replace(text, RedactJsonCredentialProperty);
			result = UriRegex().Replace(result, RedactedUri);
			result = JwtRegex().Replace(result, RedactedValue);
			result = BearerTokenRegex().Replace(result, RedactedValue);
			result = EmailRegex().Replace(result, RedactedValue);
			result = HostPortRegex().Replace(result, RedactedUri);
			result = WindowsPathRegex().Replace(result, RedactedPath);
			result = PosixPathRegex().Replace(result, RedactedPath);
			result = CredentialPairRegex().Replace(result, match => $"{match.Groups[1].Value}={RedactedValue}");
			return result;
		}

		/// <summary>Master's RedactJsonCredentialProperties body, without its guard.</summary>
		internal static string RedactJsonCredentialProperties(string text) =>
			JsonCredentialPropertyRegex().Replace(text, RedactJsonCredentialProperty);

		/// <summary>Master's RedactCredentials body, without its guard.</summary>
		internal static string RedactCredentials(string text) {
			string result = JsonCredentialPropertyRegex().Replace(text, RedactJsonCredentialProperty);
			result = UriRegex().Replace(result, StripUriUserInfo);
			result = JwtRegex().Replace(result, RedactedValue);
			result = BearerTokenRegex().Replace(result, RedactedValue);
			return CredentialPairRegex().Replace(result, match => $"{match.Groups[1].Value}={RedactedValue}");
		}

		private static string StripUriUserInfo(Match match) {
			string value = match.Value;
			int authorityStart = value.IndexOf("://", System.StringComparison.Ordinal) + 3;
			int authorityEnd = value.IndexOfAny(['/', '?', '#'], authorityStart);
			if (authorityEnd < 0) {
				authorityEnd = value.Length;
			}
			int at = value.LastIndexOf('@', authorityEnd - 1, authorityEnd - authorityStart);
			if (at < 0) {
				return value;
			}
			return string.Concat(value.AsSpan(0, authorityStart), RedactedValue, "@", value.AsSpan(at + 1));
		}

		private static string RedactJsonCredentialProperty(Match match) {
			string quote = match.Groups["q"].Value;
			return $"{quote}{match.Groups["key"].Value}{quote}:{quote}{RedactedValue}{quote}";
		}
	}
}
