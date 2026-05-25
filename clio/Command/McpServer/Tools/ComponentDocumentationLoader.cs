using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Shared helper that concatenates every long-form documentation file referenced by a
/// component registry entry into the single <c>documentation</c> field returned on
/// detail responses. Used by both the MCP <see cref="ComponentInfoTool"/> and the CLI
/// <see cref="ComponentInfoCommand"/> so the two surfaces produce identical payloads
/// — when the entry has no <c>content.docs[]</c>, returns <see langword="null"/>;
/// when every fetch fails, also returns <see langword="null"/> (graceful degradation
/// matches the registry chain itself, see <c>clio/Command/McpServer/AGENTS.md</c>).
/// </summary>
internal static class ComponentDocumentationLoader {
	internal const string DocumentationSeparator = "\n\n---\n\n";

	internal static async Task<string?> LoadAsync(
		IComponentRegistryDocsClient docsClient,
		ComponentRegistryEntry entry,
		string resolvedVersion,
		CancellationToken cancellationToken) {
		IReadOnlyList<string>? docs = entry.Content?.Docs;
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
