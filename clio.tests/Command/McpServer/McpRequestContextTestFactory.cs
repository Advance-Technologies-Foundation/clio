using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.McpServer;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Creates SDK-valid MCP request contexts for unit tests that invoke real
/// <see cref="McpServerTool"/> adapters.
/// </summary>
internal static class McpRequestContextTestFactory {
	/// <summary>
	/// A service provider carrying the execution-metadata reader, which is what the call-tool filter
	/// service-locates to decide whether a call owes the private worker completion signal.
	/// </summary>
	/// <remarks>
	/// Deliberately carries NO <c>IMcpExecutionRouter</c>: the matched dispatch site is fail-closed, so a
	/// context that also sets <c>MatchedPrimitive</c> gets a routing refusal answered before any tool runs —
	/// the cheapest reproduction of the filter's pre-execution exits.
	/// </remarks>
	internal static IServiceProvider CreateExecutionMetadataServices() =>
		new ServiceCollection()
			.AddSingleton<IMcpToolExecutionMetadataReader>(
				new McpToolExecutionMetadataReader(new McpToolCompatibilityCatalog()))
			.BuildServiceProvider();

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
