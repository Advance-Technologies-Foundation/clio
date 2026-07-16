using System;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Composition helpers for feature-aware guidance articles: a feature-gated fragment is spliced into
/// (or removed from) a base article around a unique anchor. A missing or ambiguous anchor throws, so
/// content drift fails loudly in every unit-test run instead of silently serving an article without
/// the gated fragment. All output uses <c>\n</c> line endings regardless of the source checkout's
/// endings, so multi-line anchors match on every platform.
/// </summary>
internal static class GuidanceArticleText {
	/// <summary>Normalizes CRLF line endings to LF so anchors and output are checkout-independent.</summary>
	internal static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n");

	/// <summary>
	/// Replaces exactly one occurrence of <paramref name="anchor"/> in <paramref name="text"/> with
	/// <paramref name="replacement"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">The anchor is absent or matches more than once.</exception>
	internal static string ReplaceUnique(string text, string anchor, string replacement) {
		string normalized = NormalizeNewlines(text);
		int first = normalized.IndexOf(anchor, StringComparison.Ordinal);
		if (first < 0) {
			throw new InvalidOperationException($"Guidance article anchor not found: '{anchor}'.");
		}
		if (normalized.IndexOf(anchor, first + 1, StringComparison.Ordinal) >= 0) {
			throw new InvalidOperationException($"Guidance article anchor is not unique: '{anchor}'.");
		}
		return normalized[..first] + replacement + normalized[(first + anchor.Length)..];
	}

	/// <summary>
	/// Removes the single line containing <paramref name="marker"/>, together with its line break.
	/// </summary>
	/// <exception cref="InvalidOperationException">No line, or more than one line, carries the marker.</exception>
	internal static string RemoveUniqueLine(string text, string marker) {
		string normalized = NormalizeNewlines(text);
		int index = normalized.IndexOf(marker, StringComparison.Ordinal);
		if (index < 0) {
			throw new InvalidOperationException($"Guidance article line marker not found: '{marker}'.");
		}
		if (normalized.IndexOf(marker, index + marker.Length, StringComparison.Ordinal) >= 0) {
			throw new InvalidOperationException($"Guidance article line marker is not unique: '{marker}'.");
		}
		int lineStart = normalized.LastIndexOf('\n', index);
		int lineEnd = normalized.IndexOf('\n', index + marker.Length);
		if (lineStart < 0) {
			return lineEnd < 0 ? string.Empty : normalized[(lineEnd + 1)..];
		}
		return lineEnd < 0 ? normalized[..lineStart] : normalized[..lineStart] + normalized[lineEnd..];
	}
}
