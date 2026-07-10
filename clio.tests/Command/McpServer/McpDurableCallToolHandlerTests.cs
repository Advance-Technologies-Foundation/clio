using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using CommandLine;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// The durable (forgiving) unmatched-name handler restores the pre-lazy invocation contract: a
/// non-destructive tool named directly executes via the native dispatch path with an advisory note, a
/// destructive tool returns a structured confirmation-required retry shape without executing, aliases
/// resolve through the compatibility catalog, and unresolvable names return machine-readable outcomes
/// (feature-disabled / cli-verb / unknown with did-you-mean) — every outcome carrying a correlation id.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpDurableCallToolHandlerTests {

	private IMcpToolInvokerRegistry _registry;
	private IMcpToolCompatibilityCatalog _catalog;
	private IClioRunExecutor _executor;
	private McpDurableCallToolHandler _sut;

	[SetUp]
	public void SetUp() {
		_registry = Substitute.For<IMcpToolInvokerRegistry>();
		_catalog = Substitute.For<IMcpToolCompatibilityCatalog>();
		_executor = Substitute.For<IClioRunExecutor>();
		_sut = new McpDurableCallToolHandler(_registry, _catalog, _executor);
	}

	[TearDown]
	public void TearDown() {
		_registry.ClearReceivedCalls();
		_catalog.ClearReceivedCalls();
		_executor.ClearReceivedCalls();
	}

	// RequestContext's constructor rejects a null server, so build an uninitialized instance (the
	// dispatch path only reads/writes Params and MatchedPrimitive on it).
	private static RequestContext<CallToolRequestParams> CallContext(string toolName,
		Dictionary<string, JsonElement> arguments = null) {
		RequestContext<CallToolRequestParams> context =
			(RequestContext<CallToolRequestParams>)System.Runtime.CompilerServices.RuntimeHelpers
				.GetUninitializedObject(typeof(RequestContext<CallToolRequestParams>));
		context.Params = new CallToolRequestParams { Name = toolName, Arguments = arguments };
		return context;
	}

	// A real SDK-built tool over a static echo method, so a resolved McpServerTool instance exists
	// without a live server.
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

	private static JsonElement StructuredOf(CallToolResult result) =>
		result.StructuredContent ?? throw new InvalidOperationException("expected StructuredContent");

	[Test]
	[Category("Unit")]
	[Description("Executes a resolved non-destructive tool through the native dispatch path and appends the model-visible advisory to Content plus the durable-invocation audit to Meta.")]
	public async Task HandleAsync_ShouldExecuteAndAttachAdvisory_WhenToolIsNonDestructive() {
		// Arrange
		McpServerTool tool = BuildEchoTool();
		_registry.TryGetTool("echo-tool", out Arg.Any<McpServerTool>())
			.Returns(callInfo => { callInfo[1] = tool; return true; });
		_registry.IsDestructive("echo-tool").Returns(false);
		RequestContext<CallToolRequestParams> context = CallContext("echo-tool");
		CallToolResult toolResult = new() { Content = [new TextContentBlock { Text = "payload" }] };
		_executor.InvokeResolvedAsync(tool, "echo-tool", context, Arg.Any<CancellationToken>())
			.Returns(toolResult);

		// Act
		CallToolResult result = await _sut.HandleAsync(context, CancellationToken.None);

		// Assert
		await _executor.Received(1).InvokeResolvedAsync(tool, "echo-tool", context, Arg.Any<CancellationToken>());
		result.Content.OfType<TextContentBlock>().Select(block => block.Text)
			.Should().Contain(text => text.Contains("[clio] Executed 'echo-tool'"),
				because: "a forgiving execution must carry the model-visible advisory in Content");
		result.Meta!["durable-invocation"]!["dispatched-tool"]!.GetValue<string>()
			.Should().Be("echo-tool", because: "the audit block records the dispatched canonical tool");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns confirmation-required with a ready-to-retry clio-run-destructive shape and does NOT execute, when the resolved tool is destructive.")]
	public async Task HandleAsync_ShouldReturnConfirmationRequired_WhenToolIsDestructive() {
		// Arrange
		McpServerTool tool = BuildEchoTool();
		_registry.TryGetTool("restart-by-environment-name", out Arg.Any<McpServerTool>())
			.Returns(callInfo => { callInfo[1] = tool; return true; });
		_registry.IsDestructive("restart-by-environment-name").Returns(true);
		Dictionary<string, JsonElement> arguments = new() {
			["environmentName"] = JsonSerializer.SerializeToElement("dev04")
		};
		RequestContext<CallToolRequestParams> context = CallContext("restart-by-environment-name", arguments);

		// Act
		CallToolResult result = await _sut.HandleAsync(context, CancellationToken.None);

		// Assert
		await _executor.DidNotReceiveWithAnyArgs()
			.InvokeResolvedAsync(default, default, default, default);
		result.IsError.Should().BeTrue(because: "a destructive tool is never silently executed");
		JsonElement payload = StructuredOf(result);
		payload.GetProperty("code").GetString().Should().Be("confirmation-required",
			because: "the outcome must be machine-readable");
		payload.GetProperty("retry").GetProperty("tool").GetString().Should().Be("clio-run-destructive",
			because: "the retry shape routes through the advertised, host-gated executor");
		payload.GetProperty("retry").GetProperty("arguments").GetProperty("command").GetString()
			.Should().Be("restart-by-environment-name",
				because: "the retry command is the canonical tool name");
		payload.GetProperty("correlation-id").GetString().Should().NotBeNullOrWhiteSpace(
			because: "every handler outcome carries a correlation id");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves a deprecated alias through the compatibility catalog to its canonical tool and executes it, noting the deprecation in the advisory.")]
	public async Task HandleAsync_ShouldResolveAliasAndExecute_WhenNameIsDeprecatedAlias() {
		// Arrange
		McpToolCompatibilityEntry entry = new(
			CanonicalName: "new-name",
			Aliases: ["old-name"],
			Kind: McpToolCompatibilityKind.DeprecatedAlias,
			DeprecatedSince: null,
			Replacement: null,
			Owner: McpToolSurfaceOwner.Clio);
		_catalog.TryResolveAlias("old-name", out Arg.Any<string>(), out Arg.Any<McpToolCompatibilityEntry>())
			.Returns(callInfo => { callInfo[1] = "new-name"; callInfo[2] = entry; return true; });
		McpServerTool tool = BuildEchoTool();
		_registry.TryGetTool("new-name", out Arg.Any<McpServerTool>())
			.Returns(callInfo => { callInfo[1] = tool; return true; });
		_registry.IsDestructive("new-name").Returns(false);
		RequestContext<CallToolRequestParams> context = CallContext("old-name");
		_executor.InvokeResolvedAsync(tool, "new-name", context, Arg.Any<CancellationToken>())
			.Returns(new CallToolResult { Content = [] });

		// Act
		CallToolResult result = await _sut.HandleAsync(context, CancellationToken.None);

		// Assert
		await _executor.Received(1).InvokeResolvedAsync(tool, "new-name", context, Arg.Any<CancellationToken>());
		result.Content.OfType<TextContentBlock>().Select(block => block.Text)
			.Should().Contain(text => text.Contains("deprecated alias"),
				because: "the advisory must teach the canonical name");
		result.Meta!["durable-invocation"]!["via-alias"]!.GetValue<bool>()
			.Should().BeTrue(because: "the audit records that resolution went through the catalog");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured unknown-tool outcome with did-you-mean candidates, a discovery hint, and a correlation id for a genuinely unknown name.")]
	public async Task HandleAsync_ShouldReturnUnknownTool_WhenNameResolvesNowhere() {
		// Arrange
		_registry.ToolNames.Returns(["sync-schemas", "list-apps"]);
		RequestContext<CallToolRequestParams> context = CallContext("zzz-definitely-not-a-tool");

		// Act
		CallToolResult result = await _sut.HandleAsync(context, CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(because: "an unknown tool cannot be executed");
		JsonElement payload = StructuredOf(result);
		payload.GetProperty("code").GetString().Should().Be("unknown-tool",
			because: "the outcome must be machine-readable");
		payload.GetProperty("candidates").GetArrayLength().Should().BeGreaterThan(0,
			because: "did-you-mean candidates help the agent self-correct");
		payload.GetProperty("correlation-id").GetString().Should().NotBeNullOrWhiteSpace(
			because: "every handler outcome carries a correlation id");
		result.Content.OfType<TextContentBlock>().Single().Text
			.Should().Contain("get-tool-contract", because: "the discovery hint routes the agent to the catalog");
	}

	[Test]
	[Category("Unit")]
	[Description("Classifies a tool that exists in the full reflection catalog but not the feature-filtered registry as feature-disabled, not unknown.")]
	public async Task HandleAsync_ShouldReturnFeatureDisabled_WhenToolExistsButFeatureIsOff() {
		// Arrange — sync-schemas is a REAL tool in the reflection catalog; the registry substitute
		// reports a miss, which is exactly the feature-gated-off signature.
		RequestContext<CallToolRequestParams> context = CallContext("sync-schemas");

		// Act
		CallToolResult result = await _sut.HandleAsync(context, CancellationToken.None);

		// Assert
		JsonElement payload = StructuredOf(result);
		payload.GetProperty("code").GetString().Should().Be("feature-disabled",
			because: "a real-but-gated tool must be distinguishable from an unknown name");
		result.Content.OfType<TextContentBlock>().Single().Text
			.Should().Contain("clio experimental", because: "the outcome teaches how to enable the feature");
	}

	[Test]
	[Category("Unit")]
	[Description("Classifies a name that is a clio CLI verb (but not an MCP tool) as cli-verb-not-mcp-tool with terminal guidance.")]
	public async Task HandleAsync_ShouldReturnCliVerbOutcome_WhenNameIsCliOnlyVerb() {
		// Arrange — pick a real CLI verb that has NO MCP tool, derived from the live assembly so the
		// test stays valid as the surfaces evolve.
		HashSet<string> mcpNames = new(McpToolSchemaCatalog.RegisteredToolNames, StringComparer.OrdinalIgnoreCase);
		string cliOnlyVerb = typeof(Clio.Program).Assembly.GetTypes()
			.Select(type => type.GetCustomAttribute<VerbAttribute>())
			.Where(verb => verb is not null && !string.IsNullOrWhiteSpace(verb.Name))
			.Select(verb => verb.Name)
			.FirstOrDefault(name => !mcpNames.Contains(name));
		cliOnlyVerb.Should().NotBeNull(because: "the CLI surface has verbs with no MCP tool counterpart");
		RequestContext<CallToolRequestParams> context = CallContext(cliOnlyVerb);

		// Act
		CallToolResult result = await _sut.HandleAsync(context, CancellationToken.None);

		// Assert
		JsonElement payload = StructuredOf(result);
		payload.GetProperty("code").GetString().Should().Be("cli-verb-not-mcp-tool",
			because: "a CLI-only verb is actionable guidance, not an unknown name");
		result.Content.OfType<TextContentBlock>().Single().Text
			.Should().Contain($"clio {cliOnlyVerb}", because: "the outcome shows the terminal command to run");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns deprecated-tool-alias when a declared alias resolves to a canonical that is not invokable (a removed tool).")]
	public async Task HandleAsync_ShouldReturnDeprecatedAlias_WhenCanonicalIsGone() {
		// Arrange
		McpToolCompatibilityEntry entry = new(
			CanonicalName: "gone-tool",
			Aliases: ["old-gone-tool"],
			Kind: McpToolCompatibilityKind.Removed,
			DeprecatedSince: null,
			Replacement: "replacement-tool",
			Owner: McpToolSurfaceOwner.Clio);
		_catalog.TryResolveAlias("old-gone-tool", out Arg.Any<string>(), out Arg.Any<McpToolCompatibilityEntry>())
			.Returns(callInfo => { callInfo[1] = "gone-tool"; callInfo[2] = entry; return true; });
		_registry.ToolNames.Returns(["replacement-tool"]);
		RequestContext<CallToolRequestParams> context = CallContext("old-gone-tool");

		// Act
		CallToolResult result = await _sut.HandleAsync(context, CancellationToken.None);

		// Assert
		JsonElement payload = StructuredOf(result);
		payload.GetProperty("code").GetString().Should().Be("deprecated-tool-alias",
			because: "an alias whose canonical is gone must name the replacement, not report unknown");
		payload.GetProperty("replacement").GetString().Should().Be("replacement-tool",
			because: "the entry's explicit replacement wins over the canonical");
	}
}
