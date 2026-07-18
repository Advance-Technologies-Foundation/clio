using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Serves externally delivered ESQ filter routing and frontend guidance.
/// </summary>
[McpServerResourceType]
internal sealed class EsqFiltersGuidanceResource {
	internal const string ResourceUri = "docs://mcp/guides/esq-filters";
	internal const string FrontendResourceUri = "docs://mcp/guides/esq-filters/frontend";
	private readonly IKnowledgeGuidanceResourceAdapter _adapter;

	/// <summary>
	/// Initializes a new instance of the <see cref="EsqFiltersGuidanceResource"/> class.
	/// </summary>
	public EsqFiltersGuidanceResource(IKnowledgeGuidanceResourceAdapter adapter) {
		_adapter = adapter;
	}

	internal EsqFiltersGuidanceResource() : this(KnowledgeGuidanceResourceAdapter.CreateUnavailable()) {
	}

	/// <summary>
	/// Returns the active verified ESQ filter router.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "esq-filters-guidance")]
	[Description("Returns the externally delivered ESQ filter guidance router.")]
	public ResourceContents GetGuide() => _adapter.Get(ResourceUri);

	/// <summary>
	/// Returns active verified frontend ESQ filter guidance.
	/// </summary>
	[McpServerResource(UriTemplate = FrontendResourceUri, Name = "esq-filters-frontend-guidance")]
	[Description("Returns externally delivered frontend and DataService ESQ filter guidance.")]
	public ResourceContents GetFrontendGuide() => _adapter.Get(FrontendResourceUri);
}
