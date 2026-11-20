using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Clio.Common;

internal static partial class SensitiveErrorTextRedactor {

	/// <summary>
	/// MASTER'S CHAIN, FROZEN: the patterns, options and call order of master <c>1915d7543</c>'s
	/// <c>SensitiveErrorTextRedactor</c> (<c>clio/Common/SensitiveErrorTextRedactor.cs</c>), copied verbatim into members
	/// of their own. Every entry point runs it first; what clio adds runs after it and may only replace spans of its output
	/// (<see cref="ApplyEdits"/>). Do not edit a pattern, a call or its order here: <c>MasterChainHashTests</c> pins each
	/// pattern string, its options, the calls in order and what each writes, and an output pin recorded from master's build
	/// checks what the chain returns for every corpus string. A rule that should remove more belongs in
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

		/// <summary>
		/// One call of master's chain: a rule and what master replaced each match with - a placeholder string, or a named
		/// evaluator. The chains below ARE the calls: <see cref="Redact"/>, <see cref="RedactCredentials"/> and
		/// <see cref="RedactJsonCredentialProperties"/> run their list in order and nothing else, so the hash over the
		/// lists pins the calls, their order and their evaluators, and the output pin pins what the evaluators write.
		/// </summary>
		internal sealed record ChainStep(Regex Rule, string? Replacement, MatchEvaluator? Evaluator) {
			/// <summary>The step over <paramref name="text"/>, as master's call wrote it: master's rule, under the per-call deadline
			/// (<see cref="Bounded"/>: master's own 1 s below <see cref="CharactersPerTimeoutSecond"/> while time is left), with a
			/// budget check after every evaluator call.</summary>
			public string Apply(string text) {
				Regex rule = Bounded(Rule, text.Length);
				return Evaluator is null ? rule.Replace(text, Replacement!) : rule.Replace(text, match => {
					string written = Evaluator(match);
					CheckBudget();
					return written;
				});
			}

			/// <summary>What the step writes: the placeholder, or the evaluator's declaring type and name.</summary>
			public string Writes => Evaluator is null ? Replacement! : "evaluator:" + Evaluator.Method.DeclaringType!.Name + "." + Evaluator.Method.Name;
		}

		/// <summary>Master's Redact calls, in master's order.</summary>
		internal static IReadOnlyList<ChainStep> RedactSteps { get; } = [
			new(JsonCredentialPropertyRegex(), null, RedactJsonCredentialProperty),
			new(UriRegex(), RedactedUri, null),
			new(JwtRegex(), RedactedValue, null),
			new(BearerTokenRegex(), RedactedValue, null),
			new(EmailRegex(), RedactedValue, null),
			new(HostPortRegex(), RedactedUri, null),
			new(WindowsPathRegex(), RedactedPath, null),
			new(PosixPathRegex(), RedactedPath, null),
			new(CredentialPairRegex(), null, CredentialPairToPlaceholder)
		];

		/// <summary>Master's RedactCredentials calls, in master's order.</summary>
		internal static IReadOnlyList<ChainStep> CredentialsSteps { get; } = [
			new(JsonCredentialPropertyRegex(), null, RedactJsonCredentialProperty),
			new(UriRegex(), null, StripUriUserInfo),
			new(JwtRegex(), RedactedValue, null),
			new(BearerTokenRegex(), RedactedValue, null),
			new(CredentialPairRegex(), null, CredentialPairToPlaceholder)
		];

		/// <summary>Master's RedactJsonCredentialProperties call.</summary>
		internal static IReadOnlyList<ChainStep> JsonSteps { get; } = [
			new(JsonCredentialPropertyRegex(), null, RedactJsonCredentialProperty)
		];

		/// <summary>The rules of <see cref="RedactSteps"/>, in order: what the pattern hash recorded from master's build reads.</summary>
		internal static IReadOnlyList<Regex> RedactOrder => [.. RedactSteps.Select(step => step.Rule)];

		/// <summary>The rules of <see cref="CredentialsSteps"/>, in order.</summary>
		internal static IReadOnlyList<Regex> CredentialsOrder => [.. CredentialsSteps.Select(step => step.Rule)];

		/// <summary>The rule of <see cref="JsonSteps"/>.</summary>
		internal static IReadOnlyList<Regex> JsonOrder => [.. JsonSteps.Select(step => step.Rule)];

		/// <summary>Master's Redact body, without its guard: the caller's single guard covers the whole entry point.</summary>
		internal static string Redact(string text) => Run(RedactSteps, text);

		/// <summary>Master's RedactJsonCredentialProperties body, without its guard.</summary>
		internal static string RedactJsonCredentialProperties(string text) => Run(JsonSteps, text);

		/// <summary>Master's RedactCredentials body, without its guard.</summary>
		internal static string RedactCredentials(string text) => Run(CredentialsSteps, text);

		// The calls in order. Each step counts the characters it scans as work, and the work budget is checked between two
		// calls: master had no such check, and it changes nothing a call writes; past the budget the entry point fails closed.
		private static string Run(IReadOnlyList<ChainStep> steps, string text) {
			string result = text;
			foreach (ChainStep step in steps) {
				Work += result.Length;
				CheckBudget();
				result = step.Apply(result);
			}
			return result;
		}

		// Master's pair evaluator: the key, "=" and the placeholder ("Password: x" became "Password=[redacted]").
		private static string CredentialPairToPlaceholder(Match match) => $"{match.Groups[1].Value}={RedactedValue}";

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
