using System.ComponentModel;
using System.IO.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

[McpServerResourceType]
public class FlushDbResource(IFileSystem fileSystem) : BaseResource(fileSystem){
	private const string CommandName = "flushdb";
	protected override string ResourceName { get; init; } = CommandName;
	
	[McpServerResource(UriTemplate = $"docs://help/{CommandName}", Name = CommandName)]
	[Description($"Returns help article for : {CommandName} command")]
	public override ResourceContents GetHelpArticle() => GetHelpFileContent();
}
