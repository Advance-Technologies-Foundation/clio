using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Serves externally delivered EntitySchemaQuery guidance.
/// </summary>
[McpServerResourceType]
internal sealed class EsqGuidanceResource {
	internal const string ResourceUri = "docs://mcp/guides/esq";
	private readonly IKnowledgeGuidanceResourceAdapter _adapter;

	/// <summary>
	/// Initializes a new instance of the <see cref="EsqGuidanceResource"/> class.
	/// </summary>
	public EsqGuidanceResource(IKnowledgeGuidanceResourceAdapter adapter) {
		_adapter = adapter;
	}

	internal EsqGuidanceResource() : this(KnowledgeGuidanceResourceAdapter.CreateUnavailable()) {
	}

	/// <summary>
	/// Returns the active verified ESQ article.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "esq-guidance")]
	[Description("Returns externally delivered guidance for EntitySchemaQuery authoring.")]
	public ResourceContents GetGuide() => _adapter.Get(ResourceUri);
}
