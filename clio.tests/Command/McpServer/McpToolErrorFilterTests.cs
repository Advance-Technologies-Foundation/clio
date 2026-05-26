using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
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
	[Description("Returns the original deserialization message before SDK invocation can hide a matched tool argument binding error.")]
	public async Task HandleCallToolErrors_Should_Return_Deserialization_Message_For_Matched_Tool_Argument_Error() {
		// Arrange
		NotSupportedException metadataException = new(
			"The JSON payload for polymorphic interface or abstract type 'SampleAction' must specify a type discriminator.");
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler = McpToolErrorFilter.HandleCallToolErrors(
			(_, _) => throw new InvalidOperationException("Invocation failed.", metadataException));
		RequestContext<CallToolRequestParams> context = CreateContext("sample-tool");

		// Act
		CallToolResult result = await handler(context, CancellationToken.None);
		string message = GetText(result);

		// Assert
		result.IsError.Should().BeTrue(
			because: "argument deserialization failure must be returned as a tool error result");
		message.Should().Contain("Failed to deserialize arguments for MCP tool 'sample-tool'",
			because: "the caller needs to know which tool rejected the payload");
		message.Should().Contain(metadataException.Message,
			because: "the original deserialization diagnostic should be preserved");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the original JSON deserialization message without metadata-order guidance for unrelated JSON failures.")]
	public async Task HandleCallToolErrors_Should_Return_Json_Message_Without_Metadata_Order_Hint_For_Other_Failures() {
		// Arrange
		JsonException jsonException = new("The JSON value could not be converted.");
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler = McpToolErrorFilter.HandleCallToolErrors(
			(_, _) => throw new InvalidOperationException("Invocation failed.", jsonException));
		RequestContext<CallToolRequestParams> context = CreateContext("get-package-list");

		// Act
		CallToolResult result = await handler(context, CancellationToken.None);
		string message = GetText(result);

		// Assert
		result.IsError.Should().BeTrue(
			because: "wrapped JSON failures should be surfaced as MCP error results");
		message.Should().Contain("The JSON value could not be converted.",
			because: "the actual deserialization error is the actionable part for the caller");
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

	private static string GetText(CallToolResult result) =>
		result.Content.OfType<TextContentBlock>().Single().Text;
}
