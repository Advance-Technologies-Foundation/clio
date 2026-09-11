using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
	private const int RegexTimeoutMilliseconds = 1_000;
	/// <summary>
	/// Permitted shape: starts with one of the four documentation namespaces the
	/// static-files-mcp producer publishes —
	/// <c>docs/</c> (web component/composite docs, from <c>ComponentRegistry.json</c>),
	/// <c>mobile-docs/</c> (mobile component docs, from <c>MobileComponentRegistry.json</c>),
	/// <c>request-docs/</c> (web Freedom UI request docs, from <c>RequestRegistry.json</c>), or
	/// <c>mobile-request-docs/</c> (mobile request docs, from <c>MobileRequestRegistry.json</c>) —
	/// followed by one or more dot/dash/underscore-friendly segments separated by <c>/</c>, ending
	/// in <c>.md</c>. No <c>..</c>, no leading slash, no backslashes, no whitespace. All four flavors
	/// share this single validator and the same docs CDN/cache pipeline.
	/// </summary>
	[GeneratedRegex(@"^(?:docs|mobile-docs|request-docs|mobile-request-docs)/[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)*\.md$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
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

	/// <summary>
	/// Maps each documentation namespace to the registry flavor that publishes it. This is
	/// the single place that encodes the producer-side convention "one registry flavor owns
	/// exactly one docs namespace" — verified against the live CDN payloads
	/// (<c>ComponentRegistry.json</c> → only <c>docs/</c>, <c>MobileComponentRegistry.json</c>
	/// → only <c>mobile-docs/</c>, <c>RequestRegistry.json</c> → only <c>request-docs/</c>,
	/// <c>MobileRequestRegistry.json</c> → only <c>mobile-request-docs/</c>) and against the
	/// live-snapshot fixtures under <c>clio.tests/Command/McpServer/Fixtures/</c>.
	/// </summary>
	private static readonly IReadOnlyList<(string Prefix, RegistryFlavor Flavor)> NamespaceFlavors = [
		("mobile-request-docs/", RegistryFlavor.MobileRequests),
		("mobile-docs/", RegistryFlavor.Mobile),
		("request-docs/", RegistryFlavor.Requests),
		("docs/", RegistryFlavor.Web)
	];

	/// <summary>
	/// Resolves which registry flavor owns a normalised documentation path, so the docs
	/// client can consult that flavor's <c>*_LOCAL_FILE</c> developer override. The four
	/// prefixes are disjoint (each is anchored and ends in <c>/</c>), so at most one matches.
	/// </summary>
	/// <param name="normalisedPath">A path already accepted by <see cref="TryNormalise"/>.</param>
	/// <param name="flavor">On success, the owning registry flavor.</param>
	/// <returns><see langword="true"/> when the namespace is recognised.</returns>
	public static bool TryResolveFlavor(string normalisedPath, [NotNullWhen(true)] out RegistryFlavor? flavor) {
		foreach ((string prefix, RegistryFlavor candidate) in NamespaceFlavors) {
			if (normalisedPath.StartsWith(prefix, StringComparison.Ordinal)) {
				flavor = candidate;
				return true;
			}
		}
		flavor = null;
		return false;
	}
}
