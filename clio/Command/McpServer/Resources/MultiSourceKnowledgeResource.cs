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
}
