namespace Clio.Command;

using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

internal static class PageSchemaSectionReader {
	private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

	public static bool TryRead(string body, out string content, params string[] markers) {
		foreach (string marker in markers) {
			Regex regex = RegexCache.GetOrAdd(marker, m => new Regex(
				$@"/\*\*{Regex.Escape(m)}\*/(?<content>[\s\S]*?)/\*\*{Regex.Escape(m)}\*/",
				RegexOptions.CultureInvariant | RegexOptions.Compiled,
				RegexTimeout));
			Match match = regex.Match(body);
			if (!match.Success) {
				continue;
			}

			content = match.Groups["content"].Value;
			return true;
		}

		content = null;
		return false;
	}
}
