using System;
using System.Collections.Generic;
using System.Linq;
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
	[Description("Redacts absolute paths, URIs (with credentials), and connection-string hosts from the surfaced exception message while keeping the logical reason the agent self-corrects on — the message lands in the model/host transcript.")]
	public async Task HandleCallToolErrors_Should_Redact_Sensitive_Tokens_From_Execution_Exception() {
		// Arrange
		InvalidOperationException executionException = new(
			"Login failed for 'NoSuchEnv' at https://admin:s3cret@crm.contoso.com/0/ServiceModel; config /Users/alex/.clio/appsettings.json; password=hunter2");
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((_, _) => throw executionException);
		RequestContext<CallToolRequestParams> context = CreateContext("restore-from-package-backup");

		// Act
		CallToolResult result = await handler(context, CancellationToken.None);

		// Assert
		string text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(b => b.Text));
		text.Should().Contain("Login failed for 'NoSuchEnv'",
			because: "the logical reason must survive so the agent can still self-correct");
		text.Should().NotContain("crm.contoso.com",
			because: "the target host inside the URI must not leak into the transcript");
		text.Should().NotContain("s3cret",
			because: "credentials embedded in the URI authority must be redacted");
		text.Should().NotContain("/Users/alex",
			because: "absolute file paths must not leak into the transcript");
		text.Should().NotContain("hunter2",
			because: "a password=… value must be redacted");
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
		result.Should().NotBeNull(because: "a detected flat-args mismatch must produce a corrective hint result");
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
		result.Should().BeNull(because: "a fully-wrapped args payload must not produce a corrective hint");
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
		result.Should().BeNull(because: "no hint should be produced when nothing matches the composite contract");
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
		result.Should().NotBeNull(because: "a detected composite mismatch must produce a corrective hint result");
	}

	[Test]
	[Category("Unit")]
	[Description("Excludes [JsonExtensionData] and [JsonIgnore] properties from the correct-format hint.")]
	public void TryDetectFlatArgsMismatch_ShouldExcludeNonContractProperties_WhenBuildingHint() {
		// Arrange
		MethodInfo method = typeof(FakeToolWithNonContractArgs)
			.GetMethod(nameof(FakeToolWithNonContractArgs.Execute), BindingFlags.Public | BindingFlags.Instance)!;
		Dictionary<string, JsonElement> arguments = new() {
			["name"] = JsonSerializer.SerializeToElement("routing")
		};

		// Act
		bool detected = McpToolErrorFilter.TryDetectFlatArgsMismatch(
			"get-guidance", method, arguments, out CallToolResult? result);

		// Assert
		detected.Should().BeTrue(because: "flat 'name' matches a real composite property");
		string text = ((TextContentBlock)result!.Content[0]).Text;
		text.Should().Contain("\"name\"", because: "real wire-contract properties should appear in the example");
		text.Should().NotContain(nameof(FakeArgsWithNonContractProperties.ExtensionData),
			because: "[JsonExtensionData] overflow buckets are not real arguments and must not be advertised");
		text.Should().NotContain(nameof(FakeArgsWithNonContractProperties.IgnoredAlias),
			because: "[JsonIgnore] properties are not part of the wire contract and must not be advertised");
	}

	[Test]
	[Category("Unit")]
	[Description("Does not treat properties of primitive or string parameters as composite arguments.")]
	public void TryDetectFlatArgsMismatch_ShouldReturnFalse_WhenParameterTypeIsNotAClass() {
		// Arrange
		MethodInfo method = typeof(FakeToolWithStringArg)
			.GetMethod(nameof(FakeToolWithStringArg.Execute), BindingFlags.Public | BindingFlags.Instance)!;
		Dictionary<string, JsonElement> arguments = new() {
			["Length"] = JsonSerializer.SerializeToElement(5)
		};

		// Act
		bool detected = McpToolErrorFilter.TryDetectFlatArgsMismatch(
			"test-tool", method, arguments, out CallToolResult? result);

		// Assert
		detected.Should().BeFalse(
			because: "phantom members of primitive/string parameters like Length must not trigger the hint");
		result.Should().BeNull(because: "no hint should be produced for a non-class parameter type");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false when the request context carries no Params.")]
	public void TryCreateMissingCompositeArgumentHint_ShouldReturnFalse_WhenParamsIsNull() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateContext("list-apps");
		context.Params = null;

		// Act
		bool detected = McpToolErrorFilter.TryCreateMissingCompositeArgumentHint(
			context, out CallToolResult? result);

		// Assert
		detected.Should().BeFalse(because: "a request without Params carries nothing to diagnose");
		result.Should().BeNull(because: "no hint can be produced without request parameters");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false when the request carries an empty arguments dictionary.")]
	public void TryCreateMissingCompositeArgumentHint_ShouldReturnFalse_WhenArgumentsAreEmpty() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateContext(
			"list-apps", new Dictionary<string, JsonElement>());
		context.MatchedPrimitive = CreateRealTool();

		// Act
		bool detected = McpToolErrorFilter.TryCreateMissingCompositeArgumentHint(
			context, out CallToolResult? result);

		// Assert
		detected.Should().BeFalse(because: "an empty argument set cannot be a flat-args mistake");
		result.Should().BeNull(because: "no hint should be produced for an empty argument set");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false when no MCP tool primitive matched the request.")]
	public void TryCreateMissingCompositeArgumentHint_ShouldReturnFalse_WhenMatchedPrimitiveIsNotATool() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateContext(
			"list-apps", new Dictionary<string, JsonElement> {
				["environment-name"] = JsonSerializer.SerializeToElement("local")
			});
		context.MatchedPrimitive = null;

		// Act
		bool detected = McpToolErrorFilter.TryCreateMissingCompositeArgumentHint(
			context, out CallToolResult? result);

		// Assert
		detected.Should().BeFalse(because: "without a matched tool there is no parameter contract to compare against");
		result.Should().BeNull(because: "no hint should be produced when no MCP tool matched the request");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false when the matched tool exposes no MethodInfo metadata.")]
	public void TryCreateMissingCompositeArgumentHint_ShouldReturnFalse_WhenToolHasNoMethodInfo() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateContext(
			"list-apps", new Dictionary<string, JsonElement> {
				["environment-name"] = JsonSerializer.SerializeToElement("local")
			});
		context.MatchedPrimitive = new FakeToolWithoutMethodInfo();

		// Act
		bool detected = McpToolErrorFilter.TryCreateMissingCompositeArgumentHint(
			context, out CallToolResult? result);

		// Assert
		detected.Should().BeFalse(because: "without MethodInfo metadata the parameter contract is unknown");
		result.Should().BeNull(because: "no hint should be produced when the tool exposes no MethodInfo metadata");
	}

	[Test]
	[Category("Unit")]
	[Description("Produces the wrapper hint for a real kebab-case flat payload against a real MCP tool.")]
	public void TryCreateMissingCompositeArgumentHint_ShouldReturnHint_WhenFlatKebabArgsSentToRealTool() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateContext(
			"list-apps", new Dictionary<string, JsonElement> {
				["environment-name"] = JsonSerializer.SerializeToElement("local")
			});
		context.MatchedPrimitive = CreateRealTool();

		// Act
		bool detected = McpToolErrorFilter.TryCreateMissingCompositeArgumentHint(
			context, out CallToolResult? result);

		// Assert
		detected.Should().BeTrue(because: "a flat kebab-case payload on a composite-args tool must trigger the hint");
		string text = ((TextContentBlock)result!.Content[0]).Text;
		text.Should().Contain("\"environment-name\"",
			because: "the matched flat key should appear so the hint keeps firing if property casing drifts");
		text.Should().Contain("{\"args\":", because: "the correct wrapping format should be shown");
	}

	private static RequestContext<CallToolRequestParams> CreateContext(
		string toolName, IDictionary<string, JsonElement>? arguments = null) {
		RequestContext<CallToolRequestParams> context =
			(RequestContext<CallToolRequestParams>)RuntimeHelpers.GetUninitializedObject(
				typeof(RequestContext<CallToolRequestParams>));
		context.Params = new CallToolRequestParams {
			Name = toolName,
			Arguments = arguments
		};
		return context;
	}

	private static McpServerTool CreateRealTool() =>
		McpServerTool.Create(GetFakeToolMethod(), new FakeToolWithCompositeArgs());

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

	public sealed record FakeArgsWithNonContractProperties(
		[property: JsonPropertyName("name")]
		string? Name = null
	) {
		[JsonExtensionData]
		public Dictionary<string, JsonElement>? ExtensionData { get; init; }

		[JsonIgnore]
		public string? IgnoredAlias { get; init; }
	}

	public sealed class FakeToolWithNonContractArgs {
		public string Execute(FakeArgsWithNonContractProperties args) => "ok";
	}

	public sealed class FakeToolWithStringArg {
		public string Execute(string value) => value;
	}

	private sealed class FakeToolWithoutMethodInfo : McpServerTool {
		public override Tool ProtocolTool { get; } = new() { Name = "fake-tool" };

		public override IReadOnlyList<object> Metadata { get; } = [];

		public override ValueTask<CallToolResult> InvokeAsync(
			RequestContext<CallToolRequestParams> request,
			CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(new CallToolResult());
	}
}
