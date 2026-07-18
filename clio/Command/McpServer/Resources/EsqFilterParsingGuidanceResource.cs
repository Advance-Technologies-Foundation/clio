using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Serves externally delivered runtime C# ESQ filter parsing guidance.
/// </summary>
[McpServerResourceType]
internal sealed class EsqFilterParsingGuidanceResource {
	internal const string ResourceUri = "docs://mcp/guides/esq-filter-parsing";
	private readonly IKnowledgeGuidanceResourceAdapter _adapter;

	/// <summary>
	/// Initializes a new instance of the <see cref="EsqFilterParsingGuidanceResource"/> class.
	/// </summary>
	public EsqFilterParsingGuidanceResource(IKnowledgeGuidanceResourceAdapter adapter) {
		_adapter = adapter;
	}

	internal EsqFilterParsingGuidanceResource() : this(KnowledgeGuidanceResourceAdapter.CreateUnavailable()) {
	}

	/// <summary>
	/// Returns active verified runtime C# ESQ filter parsing guidance.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "esq-filter-parsing-guidance")]
	[Description("Returns externally delivered guidance for parsing EntitySchemaQuery.Filters.")]
	public ResourceContents GetGuide() => _adapter.Get(ResourceUri);
}
