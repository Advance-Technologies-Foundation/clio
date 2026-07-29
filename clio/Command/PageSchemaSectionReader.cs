namespace Clio.Command;

using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

internal static class PageSchemaSectionReader {
	private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

	public static bool TryRead(string body, out string content, params string[] markers) {
		foreach (string marker in markers) {
			Match match = GetRegex(marker).Match(body);
			if (!match.Success) {
				continue;
			}

			content = match.Groups["content"].Value;
			return true;
		}

		content = null;
		return false;
	}

	/// <summary>
	/// Replaces the content between the first matching marker pair with <paramref name="newContent"/>,
	/// preserving the marker wrappers. Returns <c>false</c> (and the original body) when no marker matches.
	/// </summary>
	public static bool TryReplaceSection(string body, string newContent, out string updated, params string[] markers) {
		foreach (string marker in markers) {
			Match match = GetRegex(marker).Match(body);
			if (!match.Success) {
				continue;
			}

			Group content = match.Groups["content"];
			updated = string.Concat(body.AsSpan(0, content.Index), newContent, body.AsSpan(content.Index + content.Length));
			return true;
		}

		updated = body;
		return false;
	}

	private static Regex GetRegex(string marker) => RegexCache.GetOrAdd(marker, m => new Regex(
		$@"/\*\*{Regex.Escape(m)}\*/(?<content>[\s\S]*?)/\*\*{Regex.Escape(m)}\*/",
		RegexOptions.CultureInvariant | RegexOptions.Compiled,
		RegexTimeout));
}
