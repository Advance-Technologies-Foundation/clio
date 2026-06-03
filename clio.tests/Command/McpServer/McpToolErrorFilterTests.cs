using System;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpToolErrorFilterTests
{
	[Test]
	[Category("Unit")]
	[Description("Does not convert runtime JSON failures from tool execution into argument binding errors.")]
	public async Task HandleCallToolErrors_Should_Not_Convert_Runtime_Json_Exception_To_Argument_Error() {
		// Arrange
		JsonException jsonException = new("The JSON value could not be converted.");
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((_, _) => throw jsonException);
		RequestContext<CallToolRequestParams> context = CreateContext("sample-tool");

		// Act
		Func<Task> act = async () => await handler(context, CancellationToken.None);

		// Assert
		await act.Should().ThrowAsync<JsonException>(
			because: "only the explicit preflight argument binding path should create MCP deserialization diagnostics");
	}

	[Test]
	[Category("Unit")]
	[Description("Delegates to the next MCP handler when no preflight argument binding error is detected.")]
	public async Task HandleCallToolErrors_Should_Return_Next_Handler_Result_When_No_Argument_Error_Is_Detected() {
		// Arrange
		CallToolResult expected = new() { IsError = false };
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler = McpToolErrorFilter.HandleCallToolErrors(
			(_, _) => ValueTask.FromResult(expected));
		RequestContext<CallToolRequestParams> context = CreateContext("get-package-list");

		// Act
		CallToolResult result = await handler(context, CancellationToken.None);

		// Assert
		result.Should().BeSameAs(expected,
			because: "the filter should not alter successful tool execution results");
	}

	private static RequestContext<CallToolRequestParams> CreateContext(string toolName) {
		RequestContext<CallToolRequestParams> context =
			(RequestContext<CallToolRequestParams>)RuntimeHelpers.GetUninitializedObject(
				typeof(RequestContext<CallToolRequestParams>));
		context.Params = new CallToolRequestParams {
			Name = toolName
		};
		return context;
	}
}
