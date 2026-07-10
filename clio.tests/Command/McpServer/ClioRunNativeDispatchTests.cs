using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// The native dispatch path (<see cref="IClioRunExecutor.InvokeResolvedAsync"/>) forwards a direct
/// <c>tools/call</c>'s SDK-bound arguments VERBATIM — so a single-complex-parameter tool is never
/// re-wrapped into <c>{"args":{"args":{…}}}</c> — and always restores the request context's original
/// <c>Params</c>/<c>MatchedPrimitive</c> after the call.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ClioRunNativeDispatchTests {

	private ClioRunExecutor _sut;

	[SetUp]
	public void SetUp() {
		_sut = new ClioRunExecutor(
			Substitute.For<IMcpToolInvokerRegistry>(),
			Substitute.For<IMcpToolCompatibilityCatalog>());
	}

	private static RequestContext<CallToolRequestParams> CallContext(
		string toolName,
		Dictionary<string, JsonElement> arguments) {
		RequestContext<CallToolRequestParams> context =
			(RequestContext<CallToolRequestParams>)System.Runtime.CompilerServices.RuntimeHelpers
				.GetUninitializedObject(typeof(RequestContext<CallToolRequestParams>));
		context.Params = new CallToolRequestParams { Name = toolName, Arguments = arguments };
		return context;
	}

	// A real SDK-built scalar-parameter tool.
	[McpServerToolType]
	private static class EchoToolType {
		[McpServerTool(Name = "echo-tool", Destructive = false)]
		[System.ComponentModel.Description("Echoes its input back.")]
		public static string Echo([System.ComponentModel.Description("payload")] string value) => $"echo:{value}";
	}

	// A real SDK-built tool with the common clio shape: ONE complex args-record parameter named `args`.
	public sealed record ComplexToolArgs(string Name, int Count);

	[McpServerToolType]
	private static class ComplexToolType {
		[McpServerTool(Name = "complex-tool", Destructive = false)]
		[System.ComponentModel.Description("Consumes a single complex args record.")]
		public static string Run([System.ComponentModel.Description("args record")] ComplexToolArgs args) =>
			$"{args.Name}:{args.Count}";
	}

	// Built with the PRODUCTION MCP serializer options, so argument binding (property naming policy,
	// case handling) behaves exactly as on the live server.
	private static McpServerTool BuildTool(Type toolType, string methodName) =>
		McpServerTool.Create(
			toolType.GetMethod(methodName)!,
			target: null,
			new McpServerToolCreateOptions { SerializerOptions = Clio.BindingsModule.CreateMcpSerializerOptions() });

	private static string SingleText(CallToolResult result) =>
		result.Content.OfType<TextContentBlock>().Single().Text;

	[Test]
	[Category("Unit")]
	[Description("Dispatches a native scalar-parameter call verbatim: the SDK binds the parameter from the caller's own arguments dictionary.")]
	public async Task InvokeResolvedAsync_ShouldBindScalarParameter_WhenArgumentsAreNativeShape() {
		// Arrange
		McpServerTool tool = BuildTool(typeof(EchoToolType), nameof(EchoToolType.Echo));
		Dictionary<string, JsonElement> arguments = new() {
			["value"] = JsonSerializer.SerializeToElement("hi")
		};
		RequestContext<CallToolRequestParams> context = CallContext("echo-tool", arguments);

		// Act
		CallToolResult result = await _sut.InvokeResolvedAsync(tool, "echo-tool", context, CancellationToken.None);

		// Assert
		SingleText(result).Should().Contain("echo:hi",
			because: "the native arguments bind directly onto the tool's parameter");
	}

	[Test]
	[Category("Unit")]
	[Description("Dispatches a native single-complex-parameter call WITHOUT re-wrapping: arguments already carry the record under the parameter name, so no {\"args\":{\"args\":{…}}} double-wrap occurs (the codex-review B2 regression).")]
	public async Task InvokeResolvedAsync_ShouldNotDoubleWrap_WhenToolHasSingleComplexParameter() {
		// Arrange — the native call shape for complex-tool is {"args": {"name":..., "count":...}}.
		McpServerTool tool = BuildTool(typeof(ComplexToolType), nameof(ComplexToolType.Run));
		Dictionary<string, JsonElement> arguments = new() {
			["args"] = JsonSerializer.SerializeToElement(new { name = "x", count = 2 })
		};
		RequestContext<CallToolRequestParams> context = CallContext("complex-tool", arguments);

		// Act
		CallToolResult result = await _sut.InvokeResolvedAsync(tool, "complex-tool", context, CancellationToken.None);

		// Assert
		result.IsError.Should().NotBe(true,
			because: "a correctly-bound record must deserialize without a double-wrap failure");
		SingleText(result).Should().Contain("x:2",
			because: "the record's fields must reach the tool exactly as the caller sent them");
	}

	[Test]
	[Category("Unit")]
	[Description("Restores the request context's original Params and MatchedPrimitive after dispatch, so the outer pipeline observes the caller's own request.")]
	public async Task InvokeResolvedAsync_ShouldRestoreContext_WhenDispatchCompletes() {
		// Arrange
		McpServerTool tool = BuildTool(typeof(EchoToolType), nameof(EchoToolType.Echo));
		Dictionary<string, JsonElement> arguments = new() {
			["value"] = JsonSerializer.SerializeToElement("hi")
		};
		RequestContext<CallToolRequestParams> context = CallContext("echo-tool", arguments);
		CallToolRequestParams originalParams = context.Params;

		// Act
		await _sut.InvokeResolvedAsync(tool, "echo-tool", context, CancellationToken.None);

		// Assert
		context.Params.Should().BeSameAs(originalParams,
			because: "the original request params must be restored after the retargeted dispatch");
		context.MatchedPrimitive.Should().BeNull(
			because: "the original (unmatched) MatchedPrimitive must be restored after dispatch");
	}


	// A real SDK-built tool whose method throws, so the exception-path restore can be asserted.
	[McpServerToolType]
	private static class NativeThrowingToolType {
		[McpServerTool(Name = "native-throwing-tool", Destructive = false)]
		[System.ComponentModel.Description("Always throws a recoverable exception.")]
		public static string Throw([System.ComponentModel.Description("payload")] string value) =>
			throw new InvalidOperationException("native boom");
	}

	// A real SDK-built tool that honours cooperative cancellation, so the cancellation-path restore can
	// be asserted.
	[McpServerToolType]
	private static class NativeCancellingToolType {
		[McpServerTool(Name = "native-cancelling-tool", Destructive = false)]
		[System.ComponentModel.Description("Throws OperationCanceledException for its token.")]
		public static string Cancel(CancellationToken cancellationToken) =>
			throw new OperationCanceledException(cancellationToken);
	}

	[Test]
	[Category("Unit")]
	[Description("Restores the request context after a RECOVERABLE tool exception: the failure is returned as an error result and the original Params/MatchedPrimitive come back (TC-U-20 exception variant).")]
	public async Task InvokeResolvedAsync_ShouldRestoreContextAndReturnError_WhenToolThrows() {
		// Arrange
		McpServerTool tool = BuildTool(typeof(NativeThrowingToolType), nameof(NativeThrowingToolType.Throw));
		Dictionary<string, JsonElement> arguments = new() {
			["value"] = JsonSerializer.SerializeToElement("hi")
		};
		RequestContext<CallToolRequestParams> context = CallContext("native-throwing-tool", arguments);
		CallToolRequestParams originalParams = context.Params;

		// Act
		CallToolResult result = await _sut.InvokeResolvedAsync(tool, "native-throwing-tool", context, CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(
			because: "a recoverable tool exception surfaces as a structured error result, not a throw");
		context.Params.Should().BeSameAs(originalParams,
			because: "the original request params must be restored even when the tool throws");
		context.MatchedPrimitive.Should().BeNull(
			because: "the original (unmatched) MatchedPrimitive must be restored even when the tool throws");
	}

	[Test]
	[Category("Unit")]
	[Description("Restores the request context on COOPERATIVE CANCELLATION: the OperationCanceledException propagates (the host sees a cancellation) and the original Params/MatchedPrimitive come back (TC-U-20 cancellation variant).")]
	public async Task InvokeResolvedAsync_ShouldRestoreContext_WhenDispatchIsCancelled() {
		// Arrange
		McpServerTool tool = BuildTool(typeof(NativeCancellingToolType), nameof(NativeCancellingToolType.Cancel));
		RequestContext<CallToolRequestParams> context = CallContext("native-cancelling-tool", []);
		CallToolRequestParams originalParams = context.Params;
		using CancellationTokenSource cts = new();
		cts.Cancel();

		// Act
		Func<Task> act = async () =>
			await _sut.InvokeResolvedAsync(tool, "native-cancelling-tool", context, cts.Token);

		// Assert
		await act.Should().ThrowAsync<OperationCanceledException>(
			because: "cooperative cancellation must propagate so the host observes a cancellation, not a masked error");
		context.Params.Should().BeSameAs(originalParams,
			because: "the original request params must be restored even on cancellation");
		context.MatchedPrimitive.Should().BeNull(
			because: "the original (unmatched) MatchedPrimitive must be restored even on cancellation");
	}
}
