using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
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

	[Test]
	[Category("Unit")]
	[Description("Detects flat arguments that match a composite parameter's JSON properties.")]
	public void TryDetectFlatArgsMismatch_ShouldReturnTrue_WhenFlatArgsMatchCompositeProperties() {
		// Arrange
		MethodInfo method = GetFakeToolMethod();
		Dictionary<string, JsonElement> arguments = new() {
			["environment-name"] = JsonSerializer.SerializeToElement("local"),
			["filter"] = JsonSerializer.SerializeToElement("some-filter")
		};

		// Act
		bool detected = McpToolErrorFilter.TryDetectFlatArgsMismatch(
			"list-apps", method, arguments, out CallToolResult? result);

		// Assert
		detected.Should().BeTrue(because: "flat arguments matching composite type properties should be detected");
		result.Should().NotBeNull();
		result!.IsError.Should().BeTrue(because: "the result should be an error guiding the caller");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false when the composite parameter is correctly wrapped.")]
	public void TryDetectFlatArgsMismatch_ShouldReturnFalse_WhenArgsParameterIsPresent() {
		// Arrange
		MethodInfo method = GetFakeToolMethod();
		Dictionary<string, JsonElement> arguments = new() {
			["args"] = JsonSerializer.SerializeToElement(new { EnvironmentName = "local" })
		};

		// Act
		bool detected = McpToolErrorFilter.TryDetectFlatArgsMismatch(
			"list-apps", method, arguments, out CallToolResult? result);

		// Assert
		detected.Should().BeFalse(because: "correctly wrapped arguments should not trigger the hint");
		result.Should().BeNull();
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false when flat arguments do not match any composite parameter properties.")]
	public void TryDetectFlatArgsMismatch_ShouldReturnFalse_WhenFlatArgsDontMatchProperties() {
		// Arrange
		MethodInfo method = GetFakeToolMethod();
		Dictionary<string, JsonElement> arguments = new() {
			["unrelated-key"] = JsonSerializer.SerializeToElement("value")
		};

		// Act
		bool detected = McpToolErrorFilter.TryDetectFlatArgsMismatch(
			"test-tool", method, arguments, out CallToolResult? result);

		// Assert
		detected.Should().BeFalse(because: "unrelated flat keys should not trigger a false positive");
		result.Should().BeNull();
	}

	[Test]
	[Category("Unit")]
	[Description("Error message includes tool name, wrapper parameter, matched keys, and all property names.")]
	public void TryDetectFlatArgsMismatch_ShouldShowToolNameAndAllProperties_WhenFlatArgDetected() {
		// Arrange
		MethodInfo method = GetFakeToolMethod();
		Dictionary<string, JsonElement> arguments = new() {
			["environment-name"] = JsonSerializer.SerializeToElement("local")
		};

		// Act
		McpToolErrorFilter.TryDetectFlatArgsMismatch(
			"list-apps", method, arguments, out CallToolResult? result);

		// Assert
		string text = ((TextContentBlock)result!.Content[0]).Text;
		text.Should().Contain("list-apps", because: "the tool name should appear in the message");
		text.Should().Contain("\"args\"", because: "the wrapper parameter name should appear");
		text.Should().Contain("\"environment-name\"", because: "matched flat key should appear");
		text.Should().Contain("\"filter\"", because: "all composite properties should appear in the example");
	}

	[Test]
	[Category("Unit")]
	[Description("Error message shows the correct wrapping format as an example.")]
	public void TryDetectFlatArgsMismatch_ShouldShowCorrectFormat_WhenFlatArgDetected() {
		// Arrange
		MethodInfo method = GetFakeToolMethod();
		Dictionary<string, JsonElement> arguments = new() {
			["environment-name"] = JsonSerializer.SerializeToElement("local")
		};

		// Act
		McpToolErrorFilter.TryDetectFlatArgsMismatch(
			"list-apps", method, arguments, out CallToolResult? result);

		// Assert
		string text = ((TextContentBlock)result!.Content[0]).Text;
		text.Should().Contain("{\"args\":", because: "the correct wrapping format should be shown");
	}

	[Test]
	[Category("Unit")]
	[Description("Skips CancellationToken and MCP framework parameters during composite detection.")]
	public void TryDetectFlatArgsMismatch_ShouldIgnoreFrameworkParameters_WhenCheckingCompositeTypes() {
		// Arrange — method with CancellationToken param and a composite args param
		MethodInfo method = typeof(FakeToolWithCancellationToken)
			.GetMethod(nameof(FakeToolWithCancellationToken.Execute), BindingFlags.Public | BindingFlags.Instance)!;
		Dictionary<string, JsonElement> arguments = new() {
			["environment-name"] = JsonSerializer.SerializeToElement("local")
		};

		// Act
		bool detected = McpToolErrorFilter.TryDetectFlatArgsMismatch(
			"test-tool", method, arguments, out CallToolResult? result);

		// Assert
		detected.Should().BeTrue(
			because: "CancellationToken should be skipped and the composite args param should still be detected");
		result.Should().NotBeNull();
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

	private static MethodInfo GetFakeToolMethod() =>
		typeof(FakeToolWithCompositeArgs)
			.GetMethod(nameof(FakeToolWithCompositeArgs.Execute), BindingFlags.Public | BindingFlags.Instance)!;

	// --- Fake tool types for testing ---

	public sealed record FakeCompositeArgs(
		[property: JsonPropertyName("environment-name")]
		string EnvironmentName,

		[property: JsonPropertyName("filter")]
		string? Filter = null
	);

	public sealed class FakeToolWithCompositeArgs {
		public string Execute(FakeCompositeArgs args) => "ok";
	}

	public sealed class FakeToolWithCancellationToken {
		public string Execute(FakeCompositeArgs args, CancellationToken cancellationToken = default) => "ok";
	}
}
