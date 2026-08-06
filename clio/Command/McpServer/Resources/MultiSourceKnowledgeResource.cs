using System;
using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Exposes exact publisher-namespaced knowledge items without requiring one compiled resource class
/// per partner library or article.
/// </summary>
[McpServerResourceType]
internal sealed class MultiSourceKnowledgeResource {
	internal const string ResourceTemplate = "docs://knowledge/{libraryId}/{itemId}";
	private readonly IKnowledgeGuidanceResourceAdapter _adapter;

	/// <summary>
	/// Initializes the dynamic knowledge resource.
	/// </summary>
	/// <param name="adapter">The verified local knowledge adapter.</param>
	public MultiSourceKnowledgeResource(IKnowledgeGuidanceResourceAdapter adapter) {
		_adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
	}

	/// <summary>
	/// Returns one exact item from one enabled trusted library.
	/// </summary>
	/// <param name="libraryId">Stable reverse-DNS library identifier.</param>
	/// <param name="itemId">Stable item identifier inside the library.</param>
	/// <returns>The verified text resource.</returns>
	[McpServerResource(UriTemplate = ResourceTemplate, Name = "knowledge-library-item")]
	[Description("Returns one verified knowledge item by exact trusted library ID and item ID.")]
	public ResourceContents Get(string libraryId, string itemId) {
		ArgumentException.ThrowIfNullOrWhiteSpace(libraryId);
		ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
		string uri = $"docs://knowledge/{Uri.EscapeDataString(libraryId)}/{Uri.EscapeDataString(itemId)}";
		return _adapter.Get(uri);
	}

	/// <summary>
	/// Resolves a publisher-declared one-segment legacy guidance URI.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/guides/{guideName}", Name = "legacy-knowledge-guide")]
	[Description("Resolves a publisher-declared legacy guidance URI from installed trusted knowledge.")]
	public ResourceContents GetLegacy(string guideName) {
		ArgumentException.ThrowIfNullOrWhiteSpace(guideName);
		return _adapter.Get($"docs://mcp/guides/{Uri.EscapeDataString(guideName)}");
	}

	/// <summary>
	/// Resolves a publisher-declared two-segment legacy guidance URI.
	/// </summary>
	[McpServerResource(UriTemplate = "docs://mcp/guides/{family}/{guideName}", Name = "legacy-nested-knowledge-guide")]
	[Description("Resolves a publisher-declared nested legacy guidance URI from installed trusted knowledge.")]
	public ResourceContents GetLegacy(string family, string guideName) {
		ArgumentException.ThrowIfNullOrWhiteSpace(family);
		ArgumentException.ThrowIfNullOrWhiteSpace(guideName);
		return _adapter.Get($"docs://mcp/guides/{Uri.EscapeDataString(family)}/{Uri.EscapeDataString(guideName)}");
	}

	/// <summary>
	/// Resolves a publisher-declared two-segment legacy reference URI.
	/// </summary>
	/// <param name="guideName">The owning guide item ID.</param>
	/// <param name="referenceName">The reference fragment ID.</param>
	/// <returns>The verified reference contents.</returns>
	[McpServerResource(UriTemplate = "docs://mcp/references/{guideName}/{referenceName}", Name = "legacy-knowledge-reference")]
	[Description("Resolves a publisher-declared legacy reference URI from installed trusted knowledge.")]
	public ResourceContents GetLegacyReference(string guideName, string referenceName) {
		ArgumentException.ThrowIfNullOrWhiteSpace(guideName);
		ArgumentException.ThrowIfNullOrWhiteSpace(referenceName);
		return _adapter.Get(
			$"docs://mcp/references/{Uri.EscapeDataString(guideName)}/{Uri.EscapeDataString(referenceName)}");
	}
}
