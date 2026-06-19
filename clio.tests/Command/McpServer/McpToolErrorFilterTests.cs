using System;
using System.Linq;
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
	[Description("Surfaces an execution exception as a structured tool-failure result (with the real message) instead of letting the SDK return a generic 'An error occurred invoking' string the agent cannot act on.")]
	public async Task HandleCallToolErrors_Should_Surface_Execution_Exception_As_Structured_Error() {
		// Arrange
		InvalidOperationException executionException = new("Environment with key 'NoSuchEnv' not found.");
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((_, _) => throw executionException);
		RequestContext<CallToolRequestParams> context = CreateContext("find-entity-schema");

		// Act
		CallToolResult result = await handler(context, CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(because: "an unhandled tool exception must become a structured error result");
		string text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(b => b.Text));
		text.Should().Contain("find-entity-schema", because: "the message must name the failing tool");
		text.Should().Contain("Environment with key 'NoSuchEnv' not found",
			because: "the real cause must be surfaced so the agent can self-correct");
		text.Should().NotContain("deserialize",
			because: "an execution failure must not be mislabeled as an argument-binding diagnostic");
	}

	[Test]
	[Category("Unit")]
	[Description("Lets cancellation propagate so the host sees a cancellation, not a masked tool error.")]
	public async Task HandleCallToolErrors_Should_Propagate_Cancellation() {
		// Arrange
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((_, _) => throw new OperationCanceledException());
		RequestContext<CallToolRequestParams> context = CreateContext("sample-tool");

		// Act
		Func<Task> act = async () => await handler(context, CancellationToken.None);

		// Assert
		await act.Should().ThrowAsync<OperationCanceledException>(
			because: "cancellation/timeout must not be swallowed into a tool-failure result");
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
