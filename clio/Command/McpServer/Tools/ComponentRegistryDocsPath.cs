using System;
using System.Text.RegularExpressions;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Validates and normalises documentation paths that arrive from the producer-side
/// registry payload before clio uses them to build a CDN URL or a local cache path.
/// The producer is a writable GitLab repository — a malicious or buggy push could
/// place something like <c>../../../etc/passwd</c> in <c>content.docs[]</c>, so
/// every consumer of those paths MUST run them through <see cref="TryNormalise"/>
/// before touching the network or the filesystem.
/// </summary>
internal static partial class ComponentRegistryDocsPath {
	/// <summary>
	/// Permitted shape: starts with one of the docs namespaces — <c>docs/</c>
	/// (web/component registry), <c>mobile-docs/</c> (mobile registry), or
	/// <c>request-docs/</c> (Freedom UI request docs referenced from
	/// <c>RequestRegistry.json</c>) — followed by one or more dot/dash/underscore-friendly
	/// segments separated by <c>/</c>, ending in <c>.md</c>. No <c>..</c>, no leading
	/// slash, no backslashes, no whitespace.
	/// </summary>
	[GeneratedRegex(@"^(?:mobile-|request-)?docs/[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)*\.md$", RegexOptions.CultureInvariant)]
	private static partial Regex AllowedPathRegex();

	/// <summary>
	/// Validates and normalises a registry-provided documentation path. Returns
	/// <see langword="true"/> with the trimmed canonical form on success, or
	/// <see langword="false"/> when the input is missing, empty, malformed, or attempts
	/// to escape the documentation namespace.
	/// </summary>
	/// <param name="rawPath">The raw value from <c>content.docs[]</c>.</param>
	/// <param name="normalisedPath">
	/// On success, the canonical form (currently equal to <paramref name="rawPath"/> after
	/// trimming surrounding whitespace). On failure, the trimmed input as it was
	/// rejected (for log diagnostics; never use this value for filesystem or network
	/// access).
	/// </param>
	public static bool TryNormalise(string? rawPath, out string normalisedPath) {
		normalisedPath = (rawPath ?? string.Empty).Trim();
		if (normalisedPath.Length == 0) {
			return false;
		}
		// Explicit sanity checks ahead of the regex so the rejection reason is obvious
		// in logs (the regex alone would reject these but with a less useful failure).
		if (normalisedPath.Contains("..", StringComparison.Ordinal)
			|| normalisedPath.Contains('\\', StringComparison.Ordinal)
			|| normalisedPath.StartsWith('/')) {
			return false;
		}
		return AllowedPathRegex().IsMatch(normalisedPath);
	}
}
