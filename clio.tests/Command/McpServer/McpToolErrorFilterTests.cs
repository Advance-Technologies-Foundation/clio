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
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;
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
	[Description("Uses the contracted invalid-parameter-type code, the nested wire name, and the expected JSON type when a composite MCP argument contains the wrong value type.")]
	public async Task HandleCallToolErrors_Should_Report_Contracted_Type_Error_When_Nested_Argument_Has_Wrong_Type() {
		// Arrange
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((_, _) => throw new AssertionException("tool body must not run"));
		ODataReadTool toolInstance = new(Substitute.For<IToolCommandResolver>());
		MethodInfo method = typeof(ODataReadTool).GetMethod(nameof(ODataReadTool.Read), BindingFlags.Public | BindingFlags.Instance)!;
		RequestContext<CallToolRequestParams> context = CreateContext(
			"odata-read",
			new Dictionary<string, JsonElement> {
				["args"] = JsonDocument.Parse("{\"entity\":\"Lead\",\"environment-name\":\"dev\",\"order-by\":[\"CreatedOn desc\"]}").RootElement
			});
		context.MatchedPrimitive = McpServerTool.Create(method, toolInstance);

		// Act
		CallToolResult result = await handler(context, CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(
			because: "wrong argument types must be rejected before the OData tool body executes");
		string text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
		text.Should().Contain("invalid-parameter-type",
			because: "the failure must use the error code advertised by get-tool-contract");
		text.Should().Contain("order-by",
			because: "the nested wire parameter must be named instead of exposing only the composite args wrapper");
		text.Should().Contain("string",
			because: "the caller must be told the expected JSON shape");
		text.Should().NotContain("Cannot get the value of a token type",
			because: "the raw System.Text.Json implementation message is not an agent-facing contract diagnostic");
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps the message of an IAuthoritativeErrorMessage exception instead of unwrapping to the inner parser exception, so a classified non-JSON response reaches the agent (ENG-93365).")]
	public async Task HandleCallToolErrors_Should_Keep_Authoritative_Message_Instead_Of_Inner_Parser_Text() {
		// Arrange
		JsonException parserException = new("'<' is an invalid start of a value. LineNumber: 0 | BytePositionInLine: 0.");
		AuthoritativeMessageException executionException = new(
			"SelectQuery returned an HTML page instead of JSON (URL: endpoint). Verify the environment credentials.",
			parserException);
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((_, _) => throw executionException);
		RequestContext<CallToolRequestParams> context = CreateContext("find-entity-schema");

		// Act
		CallToolResult result = await handler(context, CancellationToken.None);

		// Assert
		string text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(b => b.Text));
		text.Should().Contain("HTML page instead of JSON",
			because: "the classified message was built for the agent and must survive the unwrap");
		text.Should().NotContain("is an invalid start of a value",
			because: "the raw parser text the classified message replaces must not reach the transcript (ENG-93365)");
	}

	[Test]
	[Category("Unit")]
	[Description("Still unwraps to the inner-most message for an ordinary wrapped exception, so a dispatch wrapper never hides the real cause.")]
	public async Task HandleCallToolErrors_Should_Unwrap_To_Inner_Message_For_Ordinary_Wrapped_Exception() {
		// Arrange
		InvalidOperationException executionException = new(
			"Outer wrapper message.",
			new InvalidOperationException("Environment with key 'NoSuchEnv' not found."));
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((_, _) => throw executionException);
		RequestContext<CallToolRequestParams> context = CreateContext("find-entity-schema");

		// Act
		CallToolResult result = await handler(context, CancellationToken.None);

		// Assert
		string text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(b => b.Text));
		text.Should().Contain("Environment with key 'NoSuchEnv' not found",
			because: "an unmarked wrapper must keep yielding the inner-most cause, unchanged by the ENG-93365 guard");
	}

	private sealed class AuthoritativeMessageException : InvalidOperationException, Clio.Common.IAuthoritativeErrorMessage
	{
		public AuthoritativeMessageException(string message, Exception innerException)
			: base(message, innerException) {
		}
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

	[Test]
	[Category("Unit")]
	[Description("A retry-safe matched tool is wrapped by the read-response deadline yet stays transparent on a fast completion — the real result is returned unchanged (ENG-93373).")]
	public async Task HandleCallToolErrors_ShouldReturnResultUnchanged_WhenRetrySafeToolCompletesFast() {
		// Arrange
		CallToolResult expected = new() {
			IsError = false,
			Content = [new TextContentBlock { Text = "fast-read-payload" }]
		};
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((_, _) => new ValueTask<CallToolResult>(expected));
		RequestContext<CallToolRequestParams> context = WithRoutingAuthority(CreateContext("fake-read-tool"));
		context.MatchedPrimitive = CreateRetrySafeTool();

		// Act
		CallToolResult result = await handler(context, CancellationToken.None);

		// Assert
		result.Should().BeSameAs(expected,
			because: "a fast retry-safe read must pass through the deadline wrapper unchanged");
	}

	[Test]
	[Category("Unit")]
	[Description("An exception thrown by a retry-safe tool still surfaces as a structured error through the deadline wrapper, not as a timeout (ENG-93373).")]
	public async Task HandleCallToolErrors_ShouldSurfaceException_WhenRetrySafeToolThrows() {
		// Arrange
		InvalidOperationException executionException = new("Environment with key 'NoSuchEnv' not found.");
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((_, _) => throw executionException);
		RequestContext<CallToolRequestParams> context = WithRoutingAuthority(CreateContext("fake-read-tool"));
		context.MatchedPrimitive = CreateRetrySafeTool();

		// Act
		CallToolResult result = await handler(context, CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(
			because: "a tool exception must still become a structured error even on the deadline-wrapped path");
		string text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(b => b.Text));
		text.Should().Contain("Environment with key 'NoSuchEnv' not found",
			because: "the real cause must survive the deadline wrapper so the agent can self-correct");
		text.Should().NotContain("timed out",
			because: "an immediate exception is not a deadline timeout and must not be mislabeled");
	}

	// The matched dispatch site is FAIL-CLOSED on an unreachable routing authority (ENG-95262 Stage 4b), so
	// a context that carries a MatchedPrimitive and continues into the pipeline must also carry the router —
	// exactly as every real host does. The real router over the real declared metadata is used rather than a
	// stub: these tools are unclassified, so it answers in-process and the behaviour pinned here is the
	// pre-router behaviour. The EMPTY Stage 6 cohort keeps that true no matter which real tools these cases
	// later name, and no worker dispatcher is registered — a relay reaching this fixture would be a defect.
	private static RequestContext<CallToolRequestParams> WithRoutingAuthority(
		RequestContext<CallToolRequestParams> context) {
		context.Services = new ServiceCollection()
			.AddSingleton<IMcpExecutionRouter>(
				new McpExecutionRouter(
					new McpToolExecutionMetadataReader(new McpToolCompatibilityCatalog()),
					new McpWorkerCohort([]),
					new McpWorkerPathGate(() => McpHostTransportKind.Stdio, () => false),
					workerPathWired: true))
			.BuildServiceProvider();
		return context;
	}

	// ---------------------------------------------------------------------------------------------
	// ENG-95885 — flat-argument classification matrix.
	// Every branch of the classifier has a case here, and the two ways this change can go wrong are
	// both pinned: (a) a canonical flat payload that never reaches the tool, and (b) a normalizer that
	// turns a validation error into a plausible-but-wrong success, or fights clio-run for the payload.
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Category("Unit")]
	[Description("T1: a canonical flat payload on a single-composite-args tool is rewritten to the wrapped shape and forwarded, with EVERY top-level key moved inside the wrapper (ENG-95885 R1).")]
	public async Task Normalization_ShouldWrapAllTopLevelKeys_WhenPayloadIsCanonicalFlat() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateContext(
			"list-apps", new Dictionary<string, JsonElement> {
				["environment-name"] = JsonSerializer.SerializeToElement("local"),
				["filter"] = JsonSerializer.SerializeToElement("some-filter")
			});
		context.MatchedPrimitive = CreateRealTool();
		CallToolRequestParams? forwardedParams = null;
		// ENG-95262 Stage 4b: the matched dispatch site is FAIL-CLOSED without a routing authority, so a
		// context that carries a MatchedPrimitive and continues into the pipeline must carry the router
		// too — exactly as a real host does.
		context = WithRoutingAuthority(context);
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((forwardedContext, _) => {
				forwardedParams = forwardedContext.Params;
				return ValueTask.FromResult(new CallToolResult { IsError = false });
			});

		// Act
		CallToolResult result = await handler(context, CancellationToken.None);

		// Assert
		result.IsError.Should().BeFalse(because: "a canonical flat payload is a valid call, not a caller error");
		forwardedParams.Should().NotBeNull(because: "the call must reach the next handler, not be short-circuited");
		forwardedParams!.Arguments.Should().ContainSingle(because: "the whole payload collapses into one wrapper key")
			.Which.Key.Should().Be("args", because: "the synthesized wrapper key is the args-parameter name");
		JsonElement wrapped = forwardedParams.Arguments!["args"];
		wrapped.ValueKind.Should().Be(JsonValueKind.Object,
			because: "the wrapper the SDK binds the record from must be a JSON object");
		wrapped.GetProperty("environment-name").GetString().Should().Be("local",
			because: "the matched canonical field must survive the rewrite unchanged");
		wrapped.GetProperty("filter").GetString().Should().Be("some-filter",
			because: "every top-level key moves into the wrapper — cherry-picking only the matched keys would "
				+ "silently drop a co-present field");
	}

	[Test]
	[Category("Unit")]
	[Description("T2: an already-wrapped payload passes through the filter unchanged — no rewrite, no hint, no error (ENG-95885 R1).")]
	public async Task Normalization_ShouldLeavePayloadUntouched_WhenAlreadyWrapped() {
		// Arrange
		JsonElement originalWrapper = JsonSerializer.SerializeToElement(
			new Dictionary<string, string> { ["environment-name"] = "local" });
		RequestContext<CallToolRequestParams> context = CreateContext(
			"list-apps", new Dictionary<string, JsonElement> { ["args"] = originalWrapper });
		context.MatchedPrimitive = CreateRealTool();
		CallToolRequestParams? forwardedParams = null;
		// ENG-95262 Stage 4b: the matched dispatch site is FAIL-CLOSED without a routing authority, so a
		// context that carries a MatchedPrimitive and continues into the pipeline must carry the router
		// too — exactly as a real host does.
		context = WithRoutingAuthority(context);
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((forwardedContext, _) => {
				forwardedParams = forwardedContext.Params;
				return ValueTask.FromResult(new CallToolResult { IsError = false });
			});

		// Act
		await handler(context, CancellationToken.None);

		// Assert
		forwardedParams!.Arguments.Should().ContainSingle(because: "an already-wrapped payload keeps its single wrapper key")
			.Which.Key.Should().Be("args", because: "the wrapper key is unchanged");
		forwardedParams.Arguments!["args"].GetProperty("environment-name").GetString().Should().Be("local",
			because: "the working wrapped shape must stay byte-compatible — it must not be re-wrapped or rebuilt");
	}

	[Test]
	[Category("Unit")]
	[Description("T3: an unknown-only payload against an args record with no [JsonExtensionData] overflow bag is refused with the canonical field list, and never reaches the tool with a defaulted record (ENG-95885 R2).")]
	public async Task Normalization_ShouldRefuseUnknownOnlyPayload_WhenArgsRecordHasNoOverflowBucket() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateContext(
			"list-apps", new Dictionary<string, JsonElement> {
				["enviroment"] = JsonSerializer.SerializeToElement("local")
			});
		context.MatchedPrimitive = CreateRealTool();
		bool reachedTool = false;
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((_, _) => {
				reachedTool = true;
				return ValueTask.FromResult(new CallToolResult { IsError = false });
			});

		// Act
		CallToolResult result = await handler(context, CancellationToken.None);

		// Assert
		reachedTool.Should().BeFalse(
			because: "wrapping an unknown-only payload into a record with no overflow bag would materialize "
				+ "defaults and let the tool answer a validation mistake with a plausible list/default success");
		result.IsError.Should().BeTrue(because: "an unknown-only payload is a refusal, surfaced as an error result");
		string text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(b => b.Text));
		text.Should().Contain("enviroment", because: "the offending key must be named");
		text.Should().Contain("\"environment-name\"", because: "the canonical field list must be offered");
		text.Should().Contain("\"filter\"", because: "every valid field is listed, not only the nearest match");
	}

	[Test]
	[Category("Unit")]
	[Description("T3b: a PARTIAL-unknown payload (a canonical field beside a typo) against an args record with no [JsonExtensionData] overflow bag is refused with the canonical field list — the good field does not rescue the typo into a silently-dropped success (ENG-95885 R2, partial-unknown hole).")]
	public async Task Normalization_ShouldRefusePartialUnknownPayload_WhenArgsRecordHasNoOverflowBucket() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateContext(
			"list-apps", new Dictionary<string, JsonElement> {
				["environment-name"] = JsonSerializer.SerializeToElement("local"),
				["filer"] = JsonSerializer.SerializeToElement("x")
			});
		context.MatchedPrimitive = CreateRealTool();
		bool reachedTool = false;
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((_, _) => {
				reachedTool = true;
				return ValueTask.FromResult(new CallToolResult { IsError = false });
			});

		// Act
		CallToolResult result = await handler(context, CancellationToken.None);

		// Assert
		reachedTool.Should().BeFalse(
			because: "for a no-overflow-bag record the serializer would silently drop 'filer' at bind time, so a "
				+ "real field beside a typo must be refused rather than answered with a plausible success");
		result.IsError.Should().BeTrue(because: "a payload carrying any unknown key is a refusal");
		string text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(b => b.Text));
		text.Should().Contain("filer", because: "the offending typo key must be named so the agent can fix it");
		text.Should().Contain("\"environment-name\"", because: "the canonical field list must be offered");
	}

	[Test]
	[Category("Unit")]
	[Description("T4: a hybrid payload carrying both a wrapper object and a conflicting top-level key is refused as an ambiguous shape, with no silent precedence in either direction (ENG-95885 R4).")]
	public async Task Normalization_ShouldRefuseHybridPayload_WhenWrapperAndTopLevelKeyBothPresent() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateContext(
			"list-apps", new Dictionary<string, JsonElement> {
				["args"] = JsonSerializer.SerializeToElement(
					new Dictionary<string, string> { ["environment-name"] = "from-wrapper" }),
				["environment-name"] = JsonSerializer.SerializeToElement("from-top-level")
			});
		context.MatchedPrimitive = CreateRealTool();
		bool reachedTool = false;
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((_, _) => {
				reachedTool = true;
				return ValueTask.FromResult(new CallToolResult { IsError = false });
			});

		// Act
		CallToolResult result = await handler(context, CancellationToken.None);

		// Assert
		reachedTool.Should().BeFalse(because: "an ambiguous shape must be refused, not resolved by guessing");
		result.IsError.Should().BeTrue();
		string text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(b => b.Text));
		text.Should().Contain("ambiguous");
		text.Should().NotContain("from-wrapper", because: "neither candidate value may be silently chosen");
		text.Should().NotContain("from-top-level", because: "neither candidate value may be silently chosen");
	}

	[Test]
	[Category("Unit")]
	[Description("T5a: a multi-parameter tool (the clio-run shape: command plus args) is excluded from normalization, so ClioRunExecutor.RecoverWrappedCall keeps sole ownership of clio-run recovery (ENG-95885 R4).")]
	public async Task Normalization_ShouldNotFire_WhenToolHasMultipleBindableParameters() {
		// Arrange
		Dictionary<string, JsonElement> arguments = new() {
			["command"] = JsonSerializer.SerializeToElement("sync-schemas"),
			["environment-name"] = JsonSerializer.SerializeToElement("local")
		};
		RequestContext<CallToolRequestParams> context = CreateContext("clio-run", arguments);
		context.MatchedPrimitive = McpServerTool.Create(
			typeof(FakeMultiParameterTool).GetMethod(
				nameof(FakeMultiParameterTool.Execute), BindingFlags.Public | BindingFlags.Instance)!,
			new FakeMultiParameterTool());
		CallToolRequestParams? forwardedParams = null;
		// ENG-95262 Stage 4b: the matched dispatch site is FAIL-CLOSED without a routing authority, so a
		// context that carries a MatchedPrimitive and continues into the pipeline must carry the router
		// too — exactly as a real host does.
		context = WithRoutingAuthority(context);
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((forwardedContext, _) => {
				forwardedParams = forwardedContext.Params;
				return ValueTask.FromResult(new CallToolResult { IsError = false });
			});

		// Act
		await handler(context, CancellationToken.None);

		// Assert
		forwardedParams!.Arguments.Should().BeSameAs(arguments,
			because: "a multi-parameter tool binds top-level keys BY PARAMETER NAME, so its payload must "
				+ "never be rewritten — two mechanisms fighting over the same object is the failure mode here");
	}

	[Test]
	[Category("Unit")]
	[Description("T5b: a single-SCALAR-parameter tool is excluded from normalization, because its top-level key is already the parameter name (ENG-95885 R4).")]
	public async Task Normalization_ShouldNotFire_WhenSingleParameterIsScalar() {
		// Arrange
		Dictionary<string, JsonElement> arguments = new() {
			["value"] = JsonSerializer.SerializeToElement("plain")
		};
		RequestContext<CallToolRequestParams> context = CreateContext("scalar-tool", arguments);
		context.MatchedPrimitive = McpServerTool.Create(
			typeof(FakeToolWithStringArg).GetMethod(
				nameof(FakeToolWithStringArg.Execute), BindingFlags.Public | BindingFlags.Instance)!,
			new FakeToolWithStringArg());
		CallToolRequestParams? forwardedParams = null;
		// ENG-95262 Stage 4b: the matched dispatch site is FAIL-CLOSED without a routing authority, so a
		// context that carries a MatchedPrimitive and continues into the pipeline must carry the router
		// too — exactly as a real host does.
		context = WithRoutingAuthority(context);
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((forwardedContext, _) => {
				forwardedParams = forwardedContext.Params;
				return ValueTask.FromResult(new CallToolResult { IsError = false });
			});

		// Act
		await handler(context, CancellationToken.None);

		// Assert
		forwardedParams!.Arguments.Should().BeSameAs(arguments,
			because: "a scalar parameter is bound by name from the top level, so there is nothing to wrap");
	}

	[Test]
	[Category("Unit")]
	[Description("T6a: an empty payload is accepted for a tool that has declared no-arguments capability — the empty wrapper is synthesized so the SDK can bind the record (ENG-95885 R3).")]
	public async Task Normalization_ShouldSynthesizeEmptyWrapper_WhenToolDeclaresNoArgumentsCapability() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateContext(
			"fake-no-args-tool", new Dictionary<string, JsonElement>());
		context.MatchedPrimitive = McpServerTool.Create(
			typeof(FakeNoArgumentsTool).GetMethod(
				nameof(FakeNoArgumentsTool.Execute), BindingFlags.Public | BindingFlags.Instance)!,
			new FakeNoArgumentsTool());
		CallToolRequestParams? forwardedParams = null;
		// ENG-95262 Stage 4b: the matched dispatch site is FAIL-CLOSED without a routing authority, so a
		// context that carries a MatchedPrimitive and continues into the pipeline must carry the router
		// too — exactly as a real host does.
		context = WithRoutingAuthority(context);
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((forwardedContext, _) => {
				forwardedParams = forwardedContext.Params;
				return ValueTask.FromResult(new CallToolResult { IsError = false });
			});

		// Act
		await handler(context, CancellationToken.None);

		// Assert
		forwardedParams!.Arguments.Should().ContainSingle(because: "a synthesized no-arguments call has exactly the wrapper key")
			.Which.Key.Should().Be("args", because: "the synthesized key is the args-parameter name");
		forwardedParams.Arguments!["args"].ValueKind.Should().Be(JsonValueKind.Object,
			because: "an empty args object is what the SDK needs to bind the record for a no-arguments call");
	}

	[Test]
	[Category("Unit")]
	[Description("T6b: an empty payload is left exactly as it is for a tool that has NOT declared no-arguments capability, so today's missing-parameter error is preserved — the capability is fail-closed (ENG-95885 R3).")]
	public async Task Normalization_ShouldLeaveEmptyPayloadUntouched_WhenToolDidNotDeclareCapability() {
		// Arrange
		Dictionary<string, JsonElement> arguments = new();
		RequestContext<CallToolRequestParams> context = CreateContext("list-apps", arguments);
		context.MatchedPrimitive = CreateRealTool();
		CallToolRequestParams? forwardedParams = null;
		// ENG-95262 Stage 4b: the matched dispatch site is FAIL-CLOSED without a routing authority, so a
		// context that carries a MatchedPrimitive and continues into the pipeline must carry the router
		// too — exactly as a real host does.
		context = WithRoutingAuthority(context);
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((forwardedContext, _) => {
				forwardedParams = forwardedContext.Params;
				return ValueTask.FromResult(new CallToolResult { IsError = false });
			});

		// Act
		await handler(context, CancellationToken.None);

		// Assert
		forwardedParams!.Arguments.Should().BeSameAs(arguments,
			because: "capability is declared explicitly, never inferred — an undeclared tool keeps its current "
				+ "missing-parameter behavior");
		forwardedParams.Arguments.Should().BeEmpty(
			because: "the empty payload is passed through untouched, not turned into a synthesized wrapper");
	}

	[Test]
	[Category("Unit")]
	[Description("T7: a canonical flat payload carrying a wrong JSON value type still returns the precise per-argument deserialization error after normalization, rather than falling into the generic exception handler (ENG-95885 R6).")]
	public async Task Normalization_ShouldStillYieldPreciseDeserializationError_WhenFlatValueHasWrongType() {
		// Arrange — 'count' is a canonical property, but an int cannot bind from a string
		RequestContext<CallToolRequestParams> context = CreateContext(
			"typed-tool", new Dictionary<string, JsonElement> {
				["count"] = JsonSerializer.SerializeToElement("not-a-number")
			});
		context.MatchedPrimitive = McpServerTool.Create(
			typeof(FakeToolWithTypedArgs).GetMethod(
				nameof(FakeToolWithTypedArgs.Execute), BindingFlags.Public | BindingFlags.Instance)!,
			new FakeToolWithTypedArgs());
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors(
				(_, _) => ValueTask.FromResult(new CallToolResult { IsError = false }));

		// Act
		CallToolResult result = await handler(context, CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(because: "a wrong JSON value type is a per-argument binding failure");
		string text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(b => b.Text));
		text.Should().Contain("invalid-parameter-type",
			because: "the deserialization preflight must run over the REWRITTEN arguments, so the "
				+ "contracted per-argument diagnostic survives normalization");
		text.Should().Contain("'count'",
			because: "the preflight binds the rewritten wrapper, so it names the flat key that could not "
				+ "bind rather than reporting the wrapper generically");
		text.Should().NotContain("failed:",
			because: "a binding problem must not degrade into the generic tool-failure message");
	}

	[Test]
	[Category("Unit")]
	[Description("T8: normalization replaces Arguments on the SAME Params instance, so Params identity, _meta and the progress token survive — building a new CallToolRequestParams would break notifications/progress and the _meta.clioStageEvent stream (ENG-95885 R6).")]
	public async Task Normalization_ShouldPreserveParamsIdentityAndTransportMetadata_WhenRewritingArguments() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateContext(
			"list-apps", new Dictionary<string, JsonElement> {
				["environment-name"] = JsonSerializer.SerializeToElement("local")
			});
		context.MatchedPrimitive = CreateRealTool();
		CallToolRequestParams originalParams = context.Params!;
		// ProgressToken is a projection of _meta, so seeding _meta covers both: if normalization rebuilt
		// the params object, the token and the stage-event marker would both vanish.
		originalParams.Meta = new System.Text.Json.Nodes.JsonObject {
			["progressToken"] = "progress-123",
			["clioStageEvent"] = "stage-marker"
		};
		CallToolRequestParams? forwardedParams = null;
		// ENG-95262 Stage 4b: the matched dispatch site is FAIL-CLOSED without a routing authority, so a
		// context that carries a MatchedPrimitive and continues into the pipeline must carry the router
		// too — exactly as a real host does.
		context = WithRoutingAuthority(context);
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((forwardedContext, _) => {
				forwardedParams = forwardedContext.Params;
				return ValueTask.FromResult(new CallToolResult { IsError = false });
			});

		// Act
		await handler(context, CancellationToken.None);

		// Assert
		forwardedParams.Should().BeSameAs(originalParams,
			because: "Arguments is replaced on the existing instance; a fresh params object would drop transport metadata");
		forwardedParams!.ProgressToken.Should().NotBeNull(
			because: "a long-running tool still has to emit notifications/progress after normalization");
		forwardedParams.ProgressToken.ToString().Should().Contain("progress-123",
			because: "the caller's exact progress token, projected from _meta, must survive the rewrite");
		forwardedParams.Meta.Should().NotBeNull(because: "_meta carries the clioStageEvent stream ClioRing consumes");
		forwardedParams.Meta!["clioStageEvent"]!.GetValue<string>().Should().Be("stage-marker",
			because: "the stage-event marker must survive on the same params instance for ClioRing to read it");
	}

	[Test]
	[Category("Unit")]
	[Description("An unknown-only payload IS forwarded when the tool has explicitly declared that it recovers unknown arguments itself, so get-tool-contract's flat name-only call reaches its own recovery instead of being refused (ENG-95885 R2).")]
	public async Task Normalization_ShouldForwardUnknownOnlyPayload_WhenToolDeclaresRecovery() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateContext(
			"fake-recovering-tool", new Dictionary<string, JsonElement> {
				["some-alias"] = JsonSerializer.SerializeToElement("value")
			});
		context.MatchedPrimitive = McpServerTool.Create(
			typeof(FakeUnknownRecoveringTool).GetMethod(
				nameof(FakeUnknownRecoveringTool.Execute), BindingFlags.Public | BindingFlags.Instance)!,
			new FakeUnknownRecoveringTool());
		CallToolRequestParams? forwardedParams = null;
		// ENG-95262 Stage 4b: the matched dispatch site is FAIL-CLOSED without a routing authority, so a
		// context that carries a MatchedPrimitive and continues into the pipeline must carry the router
		// too — exactly as a real host does.
		context = WithRoutingAuthority(context);
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((forwardedContext, _) => {
				forwardedParams = forwardedContext.Params;
				return ValueTask.FromResult(new CallToolResult { IsError = false });
			});

		// Act
		CallToolResult result = await handler(context, CancellationToken.None);

		// Assert
		result.IsError.Should().BeFalse(
			because: "a declared unknown-recoverer receives the payload rather than a refusal");
		forwardedParams!.Arguments.Should().ContainSingle(because: "the forwarded payload collapses into the wrapper key")
			.Which.Key.Should().Be("args", because: "the wrapper key is the args-parameter name");
		forwardedParams.Arguments!["args"].GetProperty("some-alias").GetString().Should().Be("value",
			because: "the unknown key must travel INTO the tool's own overflow bag, where the tool diagnoses it");
	}

	[Test]
	[Category("Unit")]
	[Description("An argument the tool expects as a JSON object is refused with one precise shape-naming error when it arrives as a JSON string, replacing the raw 'BytePositionInLine' deserializer text (ENG-95885 R5).")]
	public async Task JsonEncodedObjectArgument_ShouldReturnPreciseShapeError_InsteadOfRawSerializerText() {
		// Arrange — the clio-run shape: args sent as a string containing JSON text
		RequestContext<CallToolRequestParams> context = CreateContext(
			"clio-run", new Dictionary<string, JsonElement> {
				["command"] = JsonSerializer.SerializeToElement("sync-schemas"),
				["args"] = JsonSerializer.SerializeToElement("{\"environment-name\":\"local\"}")
			});
		context.MatchedPrimitive = McpServerTool.Create(
			typeof(FakeMultiParameterTool).GetMethod(
				nameof(FakeMultiParameterTool.Execute), BindingFlags.Public | BindingFlags.Instance)!,
			new FakeMultiParameterTool());
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors(
				(_, _) => ValueTask.FromResult(new CallToolResult { IsError = false }));

		// Act
		CallToolResult result = await handler(context, CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(because: "a JSON-encoded object argument is refused, not decoded");
		string text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(b => b.Text));
		text.Should().Contain("must be a JSON object",
			because: "the error must name the required shape so the agent can fix the call in one attempt");
		text.Should().NotContain("BytePositionInLine",
			because: "the raw deserializer text tells an agent nothing about the required shape");
		text.Should().Contain("not parsed",
			because: "the value is deliberately refused rather than decoded — the accepted input set stays narrow");
	}

	private static McpServerTool CreateRetrySafeTool() =>
		McpServerTool.Create(
			typeof(FakeRetrySafeTool).GetMethod(
				nameof(FakeRetrySafeTool.Execute), BindingFlags.Public | BindingFlags.Instance)!,
			new FakeRetrySafeTool());

	[Test]
	[Category("Unit")]
	[Description("Reports a JSON object, not an array, when clio-run's dictionary-typed args parameter receives an array — the enumerable check classified every dictionary as an array.")]
	public async Task HandleCallToolErrors_Should_Report_Object_When_ClioRun_Args_Receives_An_Array() {
		// Arrange
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((_, _) => throw new AssertionException("tool body must not run"));
		MethodInfo method = typeof(ClioRunTool).GetMethod(nameof(ClioRunTool.Run),
			BindingFlags.Public | BindingFlags.Instance)!;
		RequestContext<CallToolRequestParams> context = CreateContext(
			"clio-run",
			new Dictionary<string, JsonElement> {
				["command"] = JsonDocument.Parse("\"sync-schemas\"").RootElement,
				["args"] = JsonDocument.Parse("[]").RootElement
			});
		context.MatchedPrimitive = McpServerTool.Create(method, new ClioRunTool(Substitute.For<IClioRunExecutor>()));

		// Act
		CallToolResult result = await handler(context, CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(
			because: "an array is not the documented shape for the clio-run arguments object");
		string text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
		text.Should().Contain("invalid-parameter-type",
			because: "the failure must use the error code advertised by get-tool-contract");
		text.Should().Contain("an object",
			because: "Dictionary<string, JsonElement> is carried on the wire as a JSON object");
		text.Should().NotContain("must be an array",
			because: "telling the caller to send an array repeats the very shape that just failed");
	}

	[Test]
	[Category("Unit")]
	[Description("Says the named property CONTAINS an incompatible value when the binding failed deeper inside it, instead of naming the outer property's own CLR type.")]
	public async Task HandleCallToolErrors_Should_Report_Containment_When_Binding_Fails_Below_The_Property() {
		// Arrange — 'rules' IS the array the contract asks for; the incompatible value is 'actions',
		// one level down, so the real binder produces a path of $.rules[0].actions.
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((_, _) => throw new AssertionException("tool body must not run"));
		RequestContext<CallToolRequestParams> context = CreateContext(
			"create-entity-business-rule",
			new Dictionary<string, JsonElement> {
				["args"] = JsonDocument.Parse("{\"rules\":[{\"actions\":\"not-an-array\"}]}").RootElement
			});
		context.MatchedPrimitive = McpServerTool.Create(
			typeof(FakeToolWithNestedArgs).GetMethod(nameof(FakeToolWithNestedArgs.Execute),
				BindingFlags.Public | BindingFlags.Instance)!,
			new FakeToolWithNestedArgs());

		// Act
		CallToolResult result = await handler(context, CancellationToken.None);

		// Assert
		string text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
		text.Should().Contain("rules",
			because: "the message must still name the property the caller can navigate from");
		text.Should().Contain("contains a value that does not match the documented shape",
			because: "the incompatible value is nested inside the array, not the array itself");
		text.Should().NotContain("must be an array",
			because: "the caller already supplied an array, so that advice recommends no valid correction");
	}


	[Test]
	[Category("Unit")]
	[Description("An explicit JSON null for a required composite argument is rejected here: JsonElement.Deserialize returns null for a reference type without throwing, so {\"args\":null} used to reach the tool and answer with a typed NRE-derived failure while the same call through clio-run reported a missing required argument.")]
	public void TryCreateArgumentDeserializationError_ShouldReject_JsonNull_ForRequiredArgument() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateContext(
			"fake-required-tool", new Dictionary<string, JsonElement> {
				["args"] = JsonSerializer.SerializeToElement((FakeCompositeArgs?)null)
			});
		context.MatchedPrimitive = McpServerTool.Create(
			typeof(FakeToolWithRequiredArgs).GetMethod(nameof(FakeToolWithRequiredArgs.Execute))!,
			new FakeToolWithRequiredArgs());

		// Act
		bool detected = McpToolErrorFilter.TryCreateArgumentDeserializationError(
			context, out CallToolResult? result);

		// Assert
		detected.Should().BeTrue(because: "a required argument sent as null cannot reach the tool");
		result!.IsError.Should().BeTrue(
			because: "both the direct and the clio-run path must surface the same IsError contract");
		((TextContentBlock)result.Content[0]).Text.Should()
			.Contain("invalid-parameter-type", because: "the stable error id is what callers key on").And
			.Contain("'args'", because: "the message must name the argument that was null");
	}

	[Test]
	[Category("Unit")]
	[Description("An optional argument may legitimately be null, so the null guard must not fire for it.")]
	public void TryCreateArgumentDeserializationError_ShouldAccept_JsonNull_ForOptionalArgument() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateContext(
			"fake-optional-tool", new Dictionary<string, JsonElement> {
				["args"] = JsonSerializer.SerializeToElement((FakeCompositeArgs?)null)
			});
		context.MatchedPrimitive = McpServerTool.Create(
			typeof(FakeToolWithOptionalArgs).GetMethod(nameof(FakeToolWithOptionalArgs.Execute))!,
			new FakeToolWithOptionalArgs());

		// Act
		bool detected = McpToolErrorFilter.TryCreateArgumentDeserializationError(
			context, out CallToolResult? result);

		// Assert
		detected.Should().BeFalse(because: "null is a valid value for an optional parameter");
		result.Should().BeNull();
	}

	[Test]
	[Category("Unit")]
	[Description("A property whose declared type IS IReadOnlyDictionary<,> is described as an object: Type.GetInterfaces() never returns the type itself, so the declared-interface case fell through to the IEnumerable branch and told the caller to send an array.")]
	public void TryCreateArgumentDeserializationError_ShouldSayObject_ForInterfaceTypedDictionary() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateContext(
			"fake-dictionary-tool", new Dictionary<string, JsonElement> {
				["args"] = JsonSerializer.SerializeToElement(new Dictionary<string, object> {
					["title-localizations"] = Array.Empty<string>()
				})
			});
		context.MatchedPrimitive = McpServerTool.Create(
			typeof(FakeToolWithDictionaryArgs).GetMethod(nameof(FakeToolWithDictionaryArgs.Execute))!,
			new FakeToolWithDictionaryArgs());

		// Act
		bool detected = McpToolErrorFilter.TryCreateArgumentDeserializationError(
			context, out CallToolResult? result);

		// Assert
		detected.Should().BeTrue(because: "an array is not a valid value for a dictionary-typed property");
		((TextContentBlock)result!.Content[0]).Text.Should()
			.Contain("must be an object",
				because: "the caller has to be told the shape that would work, not the one that just failed").And
			.NotContain("must be an array",
				because: "repeating the rejected shape is what made the message useless");
	}

	private static RequestContext<CallToolRequestParams> CreateContext(
		string toolName, IDictionary<string, JsonElement>? arguments = null) =>
		McpRequestContextTestFactory.CreateCallToolContext(toolName, arguments);

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

	// Two levels deep on purpose: a failure inside 'actions' must be reported against 'rules' as
	// containment, not as the CLR type of 'rules' itself.
	public sealed record FakeRuleAction(
		[property: JsonPropertyName("type")]
		string Type
	);

	public sealed record FakeRule(
		[property: JsonPropertyName("actions")]
		List<FakeRuleAction> Actions
	);

	public sealed record FakeNestedArgs(
		[property: JsonPropertyName("rules")]
		List<FakeRule> Rules
	);

	public sealed class FakeToolWithNestedArgs {
		public string Execute(FakeNestedArgs args) => "ok";
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

	// --- ENG-95885 fixtures ---

	// The clio-run shape: TWO bindable parameters, so top-level keys bind BY PARAMETER NAME and the
	// normalizer must never touch the payload.
	public sealed class FakeMultiParameterTool {
		public string Execute(string? command = null, Dictionary<string, JsonElement>? args = null) => "ok";
	}

	// Declares a natural no-arguments operation, so an empty {} payload is a legitimate call.
	public sealed class FakeNoArgumentsTool {
		[Clio.Command.McpServer.Tools.McpAcceptsEmptyArguments]
		public string Execute(FakeCompositeArgs args) => "ok";
	}

	// Declares that it validates/recovers unknown keys itself (the get-tool-contract pattern), so an
	// unknown-only payload is forwarded into its overflow bag instead of being refused by the filter.
	public sealed class FakeUnknownRecoveringTool {
		[Clio.Command.McpServer.Tools.McpRecoversUnknownArguments]
		public string Execute(FakeArgsWithNonContractProperties args) => "ok";
	}

	public sealed record FakeTypedArgs(
		[property: JsonPropertyName("count")]
		int Count = 0
	);

	public sealed class FakeToolWithTypedArgs {
		public string Execute(FakeTypedArgs args) => "ok";
	}

	// A retry-safe tool: ReadOnly + Idempotent + non-Destructive, so its SDK-built annotations satisfy
	// McpReadDeadlineGate.IsRetrySafe and the filter wraps it in the read-response deadline.
	public sealed class FakeRetrySafeTool {
		[McpServerTool(Name = "fake-read-tool", ReadOnly = true, Destructive = false, Idempotent = true)]
		[System.ComponentModel.Description("A retry-safe fake read tool for deadline-wrapper tests.")]
		public string Execute(FakeCompositeArgs args) => "ok";
	}

	private sealed class FakeToolWithoutMethodInfo : McpServerTool {
		public override Tool ProtocolTool { get; } = new() { Name = "fake-tool" };

		public override IReadOnlyList<object> Metadata { get; } = [];

		public override ValueTask<CallToolResult> InvokeAsync(
			RequestContext<CallToolRequestParams> request,
			CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(new CallToolResult());
	}

	public sealed record FakeDictionaryArgs(
		[property: JsonPropertyName("title-localizations")]
		IReadOnlyDictionary<string, string> TitleLocalizations
	);

	public sealed class FakeToolWithRequiredArgs {
		public string Execute([System.ComponentModel.DataAnnotations.Required] FakeCompositeArgs args) => "ok";
	}

	public sealed class FakeToolWithOptionalArgs {
		public string Execute(FakeCompositeArgs? args = null) => "ok";
	}

	public sealed class FakeToolWithDictionaryArgs {
		public string Execute([System.ComponentModel.DataAnnotations.Required] FakeDictionaryArgs args) => "ok";
	}
}
