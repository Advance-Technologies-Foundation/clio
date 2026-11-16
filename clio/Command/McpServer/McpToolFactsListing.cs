using System;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer;

/// <summary>
/// Puts each listed tool's declared facts into its <c>_meta</c> under <see cref="McpToolFacts.MetaKey"/>, so a host
/// reads them from <c>tools/list</c> next to the standard <c>readOnlyHint</c> / <c>destructiveHint</c> annotations.
/// </summary>
/// <remarks>
/// Runs once, as a post-configuration of <see cref="McpServerOptions"/>, after the SDK has filled
/// <see cref="McpServerOptions.ToolCollection"/> from the registered tools. Writing the shared <c>Tool</c> objects
/// there, before the server serves a request, means no list request ever races a write. A tool reached only through
/// <c>clio-run</c> is not in <c>tools/list</c>; <c>get-tool-contract</c> publishes the same facts for every tool.
/// </remarks>
public static class McpToolFactsListing {

	/// <summary>Writes the facts into the <c>_meta</c> of every tool in <paramref name="options"/> that declares them.</summary>
	/// <param name="options">The server options, after the SDK's own configuration.</param>
	public static void Attach(McpServerOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		if (options.ToolCollection is null) {
			return;
		}
		foreach (McpServerTool tool in options.ToolCollection) {
			if (!McpToolFactsReader.TryGet(tool.ProtocolTool.Name, out McpToolFacts facts)) {
				continue;
			}
			JsonObject meta = tool.ProtocolTool.Meta ?? new JsonObject();
			meta[McpToolFacts.MetaKey] = facts.ToJson();
			tool.ProtocolTool.Meta = meta;
		}
	}
}
