using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Concatenated documentation for one registry entry, together with the provenance the
/// detail response surfaces so a mixed or missing result stops being silent.
/// </summary>
/// <param name="Documentation">
/// Every successfully-fetched markdown block joined with
/// <see cref="ComponentDocumentationLoader.DocumentationSeparator"/>, or
/// <see langword="null"/> when the entry declares no docs or nothing could be served.
/// </param>
/// <param name="Source">
/// Wire value for <c>documentationSource</c>: <c>local</c>, <c>cache</c>, <c>cdn</c>,
/// <c>mixed</c> when the declared files came from different tiers, <c>none</c> when
/// nothing was served, and <see langword="null"/> when the entry declares no docs at all.
/// </param>
/// <param name="Warning">
/// Wire value for <c>documentationWarning</c>: set only when a flavor's <c>*_LOCAL_FILE</c>
/// override is active and at least one declared file was not present in the working copy,
/// naming each path and where it was expected. <see langword="null"/> otherwise.
/// </param>
internal sealed record ComponentDocumentationOutcome(
	string? Documentation,
	string? Source,
	string? Warning) {

	/// <summary>The entry declares no <c>references.docs[]</c> — no provenance to report.</summary>
	public static ComponentDocumentationOutcome NotDeclared { get; } =
		new(Documentation: null, Source: null, Warning: null);
}

/// <summary>
/// Shared helper that concatenates every long-form documentation file referenced by a
/// component registry entry into the single <c>documentation</c> field returned on
/// detail responses, and reports which tier served them. Used by both the MCP
/// <see cref="ComponentInfoTool"/> and the CLI <see cref="ComponentInfoCommand"/> so the
/// two surfaces produce identical payloads — when the entry has no <c>references.docs[]</c>
/// it returns <see cref="ComponentDocumentationOutcome.NotDeclared"/>; when every fetch
/// fails the documentation is <see langword="null"/> with source <c>none</c> (graceful
/// degradation matches the registry chain itself, see <c>clio/Command/McpServer/AGENTS.md</c>).
/// </summary>
internal static class ComponentDocumentationLoader {
	internal const string DocumentationSeparator = "\n\n---\n\n";

	internal const string SourceLocal = "local";
	internal const string SourceCache = "cache";
	internal const string SourceCdn = "cdn";
	internal const string SourceMixed = "mixed";
	internal const string SourceNone = "none";

	internal static Task<ComponentDocumentationOutcome> LoadAsync(
		IComponentRegistryDocsClient docsClient,
		ComponentRegistryEntry entry,
		string resolvedVersion,
		CancellationToken cancellationToken) =>
		LoadAsync(docsClient, entry.References?.Docs, resolvedVersion, cancellationToken);

	/// <summary>
	/// Overload over a raw list of doc paths, used for composite Designer elements
	/// (<see cref="CompositeDefinition.Docs"/>) which carry their docs directly rather
	/// than under a <c>references.docs</c> block. Same fetch → concatenate → graceful
	/// degradation contract as the entry overload.
	/// </summary>
	internal static async Task<ComponentDocumentationOutcome> LoadAsync(
		IComponentRegistryDocsClient docsClient,
		IReadOnlyList<string>? docs,
		string resolvedVersion,
		CancellationToken cancellationToken) {
		if (docs is null || docs.Count == 0) {
			return ComponentDocumentationOutcome.NotDeclared;
		}

		List<string> blocks = new(capacity: docs.Count);
		HashSet<ComponentDocumentationSource> servedBy = [];
		List<string> localMisses = [];
		foreach (string docPath in docs) {
			ComponentDocumentationFetchResult result = await docsClient
				.GetDocAsync(resolvedVersion, docPath, cancellationToken).ConfigureAwait(false);
			if (!string.IsNullOrEmpty(result.Content)) {
				blocks.Add(result.Content);
				servedBy.Add(result.Source);
				continue;
			}
			// A local override was active for this namespace and did not carry the file: the
			// chain deliberately stopped instead of substituting the published CDN copy, so the
			// response has to say which file is missing and where it was looked for.
			if (result.ExpectedLocalPath is not null) {
				localMisses.Add($"'{docPath}' (expected at '{result.ExpectedLocalPath}')");
			}
		}

		string? documentation = blocks.Count == 0 ? null : string.Join(DocumentationSeparator, blocks);
		return new ComponentDocumentationOutcome(
			documentation,
			DescribeSource(servedBy),
			BuildWarning(localMisses));
	}

	private static string DescribeSource(HashSet<ComponentDocumentationSource> servedBy) =>
		servedBy.Count switch {
			0 => SourceNone,
			1 => servedBy.Single() switch {
				ComponentDocumentationSource.Local => SourceLocal,
				ComponentDocumentationSource.FileCache => SourceCache,
				ComponentDocumentationSource.Cdn => SourceCdn,
				_ => SourceNone
			},
			_ => SourceMixed
		};

	private static string? BuildWarning(List<string> localMisses) =>
		localMisses.Count == 0
			? null
			: "A component-registry local-file override is active, so documentation is served only from the "
			+ "working copy next to the override file; the published CDN copy is deliberately NOT substituted. "
			+ "Not found locally: " + string.Join("; ", localMisses)
			+ ". Generate the missing file into that directory, or unset the override to read published documentation.";
}
