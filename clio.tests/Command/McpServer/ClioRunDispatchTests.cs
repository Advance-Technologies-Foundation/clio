using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// clio-run / clio-run-destructive now dispatch by MCP tool NAME against the invoker registry (not by
/// CLI [Verb]): unknown tool → structured miss, destructiveness gate from the tool annotation, and a
/// known tool is invoked through the SDK and its CallToolResult is returned unchanged.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ClioRunDispatchTests {

	private IMcpToolInvokerRegistry _registry;
	private ClioRunExecutor _sut;

	[SetUp]
	public void SetUp() {
		_registry = Substitute.For<IMcpToolInvokerRegistry>();
		_sut = new ClioRunExecutor(_registry);
	}

	// A real SDK-built tool over a static echo method, so InvokeAsync executes without a live server.
	[McpServerToolType]
	private static class EchoToolType {
		[McpServerTool(Name = "echo-tool", Destructive = false)]
		[System.ComponentModel.Description("Echoes its input back.")]
		public static string Echo([System.ComponentModel.Description("payload")] string value) => $"echo:{value}";
	}

	private static McpServerTool BuildEchoTool() =>
		McpServerTool.Create(
			typeof(EchoToolType).GetMethod(nameof(EchoToolType.Echo))!,
			target: null,
			new McpServerToolCreateOptions { SerializerOptions = JsonSerializerOptions.Default });

	// A real SDK-built tool whose method throws, so InvokeAsync surfaces an exception (which the SDK
	// wraps) for the error-masking guard test.
	[McpServerToolType]
	private static class ThrowingToolType {
		internal const string FailureMessage = "boom: underlying tool blew up";

		[McpServerTool(Name = "throwing-tool", Destructive = false)]
		[System.ComponentModel.Description("Always throws.")]
		public static string Throw([System.ComponentModel.Description("payload")] string value) =>
			throw new InvalidOperationException(FailureMessage);
	}

	private static McpServerTool BuildThrowingTool() =>
		McpServerTool.Create(
			typeof(ThrowingToolType).GetMethod(nameof(ThrowingToolType.Throw))!,
			target: null,
			new McpServerToolCreateOptions { SerializerOptions = JsonSerializerOptions.Default });

	// RequestContext's constructor rejects a null server, so build an uninitialized instance (the
	// executor reuses this context and only sets Params/MatchedPrimitive before InvokeAsync).
	private static RequestContext<CallToolRequestParams> CallContext() =>
		(RequestContext<CallToolRequestParams>)System.Runtime.CompilerServices.RuntimeHelpers
			.GetUninitializedObject(typeof(RequestContext<CallToolRequestParams>));

	private void RegisterTool(string name, McpServerTool tool, bool destructive) {
		_registry.TryGetTool(name, out Arg.Any<McpServerTool>())
			.Returns(call => {
				call[1] = tool;
				return true;
			});
		_registry.IsDestructive(name).Returns(destructive);
	}

	private static string ErrorText(CallToolResult result) =>
		string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

	[Test]
	[Category("Unit")]
	[Description("Returns a structured 'unknown tool' result (not an exception) when the tool name is not registered.")]
	public async Task RunAsync_ShouldReturnUnknownToolResult_WhenToolIsNotRegistered() {
		// Arrange
		_registry.TryGetTool("nope", out Arg.Any<McpServerTool>()).Returns(false);

		// Act
		CallToolResult result = await _sut.RunAsync("nope", null, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(because: "an unknown tool is a failure result");
		ErrorText(result).Should().Contain("unknown tool 'nope'",
			because: "the failure must be a structured unknown-tool message");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects dispatching clio-run to itself, preventing unbounded recursive self-dispatch (DoS).")]
	public async Task RunAsync_ShouldRejectSelfDispatch_WhenTargetIsClioRun() {
		// Arrange

		// Act
		CallToolResult result = await _sut.RunAsync(ClioRunTool.ToolName, null, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(because: "clio-run must not be able to target itself");
		ErrorText(result).Should().Contain("self/cross-dispatch is not allowed",
			because: "the guard must explain that the executors cannot be dispatch targets");
		_registry.DidNotReceive().TryGetTool(ClioRunTool.ToolName, out Arg.Any<McpServerTool>());
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects clio-run-destructive cross-dispatching to clio-run (or vice versa), closing the recursion path.")]
	public async Task RunAsync_ShouldRejectCrossDispatch_WhenTargetIsTheOtherExecutor() {
		// Arrange

		// Act
		CallToolResult result = await _sut.RunAsync(ClioRunTool.ToolName, null, destructiveSurface: true, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(because: "one executor must not be able to target the other");
		ErrorText(result).Should().Contain("self/cross-dispatch is not allowed",
			because: "cross-dispatch between the executors is the same recursion hazard as self-dispatch");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a different-cased executor name (e.g. 'CLIO-RUN'), since the registry resolves names case-insensitively — a case mismatch must not slip past the recursion guard.")]
	public async Task RunAsync_ShouldRejectSelfDispatch_WhenTargetIsDifferentlyCasedExecutorName() {
		// Arrange
		string mixedCaseName = ClioRunTool.ToolName.ToUpperInvariant();

		// Act
		CallToolResult result = await _sut.RunAsync(mixedCaseName, null, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(
			because: "the registry matches names with OrdinalIgnoreCase, so a different-cased executor name is still self-dispatch and must be refused");
		ErrorText(result).Should().Contain("self/cross-dispatch is not allowed",
			because: "the guard must reject the case-variant alias for the same recursion reason");
		_registry.DidNotReceive().TryGetTool(mixedCaseName, out Arg.Any<McpServerTool>());
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured error when 'command' is null or whitespace.")]
	public async Task RunAsync_ShouldReturnError_WhenCommandIsBlank() {
		// Arrange

		// Act
		CallToolResult result = await _sut.RunAsync("   ", null, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(because: "a blank command cannot be dispatched");
		ErrorText(result).Should().Contain("'command' is required",
			because: "the error must explain the missing command");
	}

	// The destructive-vs-safe REFUSAL was removed: field testing showed capable models loop forever on
	// the "use the other executor" redirect and never act. Both surfaces now run ANY tool directly;
	// host-level Destructive=true is the safety boundary. These tests pin that there is no refusal.
	[Test]
	[Category("Unit")]
	[Description("clio-run runs a destructive tool directly (no 'use the other executor' refusal) — eliminates the model-looping footgun.")]
	public async Task RunAsync_ShouldRunDestructiveTool_WhenInvokedOnSafeSurface() {
		// Arrange
		RegisterTool("echo-tool", BuildEchoTool(), destructive: true);
		JsonElement args = JsonDocument.Parse("{\"value\":\"hi\"}").RootElement;

		// Act
		CallToolResult result = await _sut.RunAsync("echo-tool", args, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().NotBe(true, because: "a destructive tool must execute, not be refused with a redirect");
		result.Content.OfType<TextContentBlock>().Should().Contain(
			block => block.Text.Contains("echo:hi", StringComparison.Ordinal),
			because: "the executor must reach the real tool regardless of the surface");
	}

	[Test]
	[Category("Unit")]
	[Description("clio-run-destructive runs a non-destructive tool directly (no refusal) — either executor works for any tool.")]
	public async Task RunAsync_ShouldRunNonDestructiveTool_WhenInvokedOnDestructiveSurface() {
		// Arrange
		RegisterTool("echo-tool", BuildEchoTool(), destructive: false);
		JsonElement args = JsonDocument.Parse("{\"value\":\"hi\"}").RootElement;

		// Act
		CallToolResult result = await _sut.RunAsync("echo-tool", args, destructiveSurface: true, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().NotBe(true, because: "either executor must run any tool without a cross-redirect");
		result.Content.OfType<TextContentBlock>().Should().Contain(
			block => block.Text.Contains("echo:hi", StringComparison.Ordinal),
			because: "the executor must reach the real tool regardless of the surface");
	}

	[Test]
	[Category("Unit")]
	[Description("A known safe tool is invoked through the SDK and its CallToolResult is returned to the caller.")]
	public async Task RunAsync_ShouldInvokeToolAndReturnResult_WhenSafeToolIsValid() {
		// Arrange
		RegisterTool("echo-tool", BuildEchoTool(), destructive: false);
		JsonElement args = JsonDocument.Parse("{\"value\":\"hello\"}").RootElement;

		// Act
		CallToolResult result = await _sut.RunAsync("echo-tool", args, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().NotBe(true, because: "a valid invocation must not be an error");
		result.Content.OfType<TextContentBlock>().Should().Contain(
			block => block.Text.Contains("echo:hello", StringComparison.Ordinal),
			because: "clio-run must reach the real tool method and return its output");
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces the real failure message in a structured Error result (not a thrown exception or a generic message) when the dispatched tool fails (field-test defect #3).")]
	public async Task RunAsync_ShouldReturnStructuredErrorWithRealMessage_WhenDispatchedToolThrows() {
		// Arrange
		RegisterTool("throwing-tool", BuildThrowingTool(), destructive: false);
		JsonElement args = JsonDocument.Parse("{\"value\":\"hello\"}").RootElement;

		// Act
		CallToolResult result = await _sut.RunAsync("throwing-tool", args, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(
			because: "a failing dispatched tool must yield a structured error result, not escape to the generic outer filter");
		ErrorText(result).Should().Contain(ThrowingToolType.FailureMessage,
			because: "the real (inner-most) failure message must be surfaced so the agent can self-correct instead of seeing a generic 'An error occurred' message");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a non-object 'args' value with a structured error before dispatch.")]
	public async Task RunAsync_ShouldReturnError_WhenArgsIsNotObject() {
		// Arrange
		RegisterTool("echo-tool", BuildEchoTool(), destructive: false);
		JsonElement args = JsonDocument.Parse("\"not-an-object\"").RootElement;

		// Act
		CallToolResult result = await _sut.RunAsync("echo-tool", args, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(because: "args must be a JSON object");
		ErrorText(result).Should().Contain("must be a JSON object",
			because: "the error must explain the expected args shape");
	}

	[Test]
	[Category("Unit")]
	[Description("ClioRunTool delegates to the executor with destructiveSurface=false.")]
	public async Task ClioRunTool_ShouldDelegateToExecutor_WithSafeSurface() {
		// Arrange
		IClioRunExecutor executor = Substitute.For<IClioRunExecutor>();
		CallToolResult expected = new() { Content = [] };
		executor.RunAsync("get-thing", Arg.Any<JsonElement?>(), false, Arg.Any<RequestContext<CallToolRequestParams>>(), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<CallToolResult>(expected));
		ClioRunTool tool = new(executor);

		// Act
		CallToolResult result = await tool.Run(CallContext(), "get-thing");

		// Assert
		result.Should().BeSameAs(expected, because: "the tool returns the executor result unchanged");
		await executor.Received(1).RunAsync("get-thing", Arg.Any<JsonElement?>(), false, Arg.Any<RequestContext<CallToolRequestParams>>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Category("Unit")]
	[Description("ClioRunDestructiveTool delegates to the executor with destructiveSurface=true.")]
	public async Task ClioRunDestructiveTool_ShouldDelegateToExecutor_WithDestructiveSurface() {
		// Arrange
		IClioRunExecutor executor = Substitute.For<IClioRunExecutor>();
		CallToolResult expected = new() { Content = [] };
		executor.RunAsync("delete-thing", Arg.Any<JsonElement?>(), true, Arg.Any<RequestContext<CallToolRequestParams>>(), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<CallToolResult>(expected));
		ClioRunDestructiveTool tool = new(executor);

		// Act
		CallToolResult result = await tool.Run(CallContext(), "delete-thing");

		// Assert
		result.Should().BeSameAs(expected, because: "the tool returns the executor result unchanged");
		await executor.Received(1).RunAsync("delete-thing", Arg.Any<JsonElement?>(), true, Arg.Any<RequestContext<CallToolRequestParams>>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Category("Unit")]
	[Description("clio-run advertises Destructive=true (host-gated) and never ReadOnly, since it can run write/destructive tools.")]
	public void ClioRunTool_ShouldExposeDestructiveNonReadOnlyMetadata() {
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ClioRunTool)
			.GetMethod(nameof(ClioRunTool.Run))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Act

		// Assert
		attribute.Name.Should().Be(ClioRunTool.ToolName, because: "the tool uses its stable name constant");
		attribute.ReadOnly.Should().BeFalse(because: "clio-run must never be ReadOnly/auto-approved");
		attribute.Destructive.Should().BeTrue(
			because: "clio-run can dispatch destructive tools, so the host must gate it (host-level safety replaces the removed executor split)");
	}

	[Test]
	[Category("Unit")]
	[Description("clio-run-destructive advertises a destructive MCP tool so hosts can prompt for confirmation.")]
	public void ClioRunDestructiveTool_ShouldExposeDestructiveMetadata() {
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ClioRunDestructiveTool)
			.GetMethod(nameof(ClioRunDestructiveTool.Run))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Act

		// Assert
		attribute.Name.Should().Be(ClioRunDestructiveTool.ToolName, because: "the tool uses its stable name constant");
		attribute.Destructive.Should().BeTrue(because: "the destructive surface must flag Destructive=true");
	}
}
