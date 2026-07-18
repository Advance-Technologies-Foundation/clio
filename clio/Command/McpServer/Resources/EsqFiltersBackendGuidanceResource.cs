using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Serves externally delivered native C# ESQ filter guidance.
/// </summary>
[McpServerResourceType]
internal sealed class EsqFiltersBackendGuidanceResource {
	internal const string ResourceUri = "docs://mcp/guides/esq-filters/backend";
	private readonly IKnowledgeGuidanceResourceAdapter _adapter;

	/// <summary>
	/// Initializes a new instance of the <see cref="EsqFiltersBackendGuidanceResource"/> class.
	/// </summary>
	public EsqFiltersBackendGuidanceResource(IKnowledgeGuidanceResourceAdapter adapter) {
		_adapter = adapter;
	}

	internal EsqFiltersBackendGuidanceResource() : this(KnowledgeGuidanceResourceAdapter.CreateUnavailable()) {
	}

	/// <summary>
	/// Returns active verified native C# ESQ filter guidance.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "esq-filters-backend-guidance")]
	[Description("Returns externally delivered native C# EntitySchemaQuery filter guidance.")]
	public ResourceContents GetGuide() => _adapter.Get(ResourceUri);
}
