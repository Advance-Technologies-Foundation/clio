using System.Collections.Generic;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Creates SDK-valid MCP request contexts for unit tests that invoke real
/// <see cref="McpServerTool"/> adapters.
/// </summary>
internal static class McpRequestContextTestFactory {
	internal static RequestContext<CallToolRequestParams> CreateCallToolContext(
		string toolName,
		IDictionary<string, JsonElement>? arguments = null) {
		ModelContextProtocol.Server.McpServer server =
			Substitute.For<ModelContextProtocol.Server.McpServer>();
		server.NegotiatedProtocolVersion.Returns("2025-11-25");
		JsonRpcRequest request = new() { Method = "tools/call" };
		return new RequestContext<CallToolRequestParams>(
			server,
			request,
			new CallToolRequestParams { Name = toolName, Arguments = arguments });
	}
}
