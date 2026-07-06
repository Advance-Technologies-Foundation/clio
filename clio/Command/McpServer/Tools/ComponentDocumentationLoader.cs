using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Shared helper that concatenates every long-form documentation file referenced by a
/// component registry entry into the single <c>documentation</c> field returned on
/// detail responses. Used by both the MCP <see cref="ComponentInfoTool"/> and the CLI
/// <see cref="ComponentInfoCommand"/> so the two surfaces produce identical payloads
/// — when the entry has no <c>references.docs[]</c>, returns <see langword="null"/>;
/// when every fetch fails, also returns <see langword="null"/> (graceful degradation
/// matches the registry chain itself, see <c>clio/Command/McpServer/AGENTS.md</c>).
/// </summary>
internal static class ComponentDocumentationLoader {
	internal const string DocumentationSeparator = "\n\n---\n\n";

	internal static Task<string?> LoadAsync(
		IComponentRegistryDocsClient docsClient,
		ComponentRegistryEntry entry,
		string resolvedVersion,
		CancellationToken cancellationToken) =>
		LoadAsync(docsClient, entry.References?.Docs, resolvedVersion, cancellationToken);

	/// <summary>
	/// Overload over a raw list of doc paths, used for composite Designer elements
	/// (<see cref="CompositeDefinition.Docs"/>) which carry their docs directly rather
	/// than under a <c>references.docs</c> block. Same fetch → concatenate → graceful
	/// degradation contract as the entry overload: <see langword="null"/> when the list
	/// is empty or every fetch fails.
	/// </summary>
	internal static async Task<string?> LoadAsync(
		IComponentRegistryDocsClient docsClient,
		IReadOnlyList<string>? docs,
		string resolvedVersion,
		CancellationToken cancellationToken) {
		if (docs is null || docs.Count == 0) {
			return null;
		}

		List<string> blocks = new(capacity: docs.Count);
		foreach (string docPath in docs) {
			string? block = await docsClient.GetDocAsync(resolvedVersion, docPath, cancellationToken).ConfigureAwait(false);
			if (!string.IsNullOrEmpty(block)) {
				blocks.Add(block);
			}
		}

		return blocks.Count == 0 ? null : string.Join(DocumentationSeparator, blocks);
	}
}
