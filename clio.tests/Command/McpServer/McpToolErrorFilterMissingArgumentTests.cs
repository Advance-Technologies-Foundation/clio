using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text.Json;
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

/// <summary>
/// A call that leaves out a required argument is refused with the argument named, the key the caller sent
/// in its place and the arguments the tool takes. Before, it got the SDK's "The arguments dictionary is
/// missing a value for the required parameter 'environmentName'. (Parameter 'arguments')". The case that
/// prompted it: <c>get-fsm-mode</c> takes a flat <c>environmentName</c>, and an agent sent the
/// <c>environment-name</c> every args-record tool uses, directly and through clio-run.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpToolErrorFilterMissingArgumentTests {

	private const string Lead =
		"invalid-parameter-type: argument 'environmentName' for MCP tool 'get-fsm-mode' is required and was not sent.";

	[Test]
	[Category("Unit")]
	[Description("A direct get-fsm-mode call that sends environment-name is refused with environmentName named as missing, environment-name pointed at it, and the arguments the tool takes.")]
	public async Task HandleCallToolErrors_ShouldNameTheMissingArgumentAndSuggestIt_WhenAFlatParameterIsSentInKebabCase() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateGetFsmModeContext("{\"environment-name\":\"dev\"}");

		// Act
		string text = await CallAndReadTextAsync(context);

		// Assert
		text.Should().StartWith(Lead,
			because: "the lead sentence names the argument the tool needs, in the stable invalid-parameter-type form");
		text.Should().Contain("Unknown argument 'environment-name' (did you mean 'environmentName'?).",
			because: "the key the caller sent is the one to rename, and the refusal says to what");
		text.Should().Contain("get-fsm-mode takes: environmentName (string, required).",
			because: "the caller sees every argument the tool takes, typed and marked required, in one line");
		text.Should().Contain("About environmentName: Registered clio environment name.",
			because: "the argument's own description says what value it wants");
		text.Should().NotContain("arguments dictionary",
			because: "the SDK's binder text is what this refusal replaces");
	}

	[Test]
	[Category("Unit")]
	[Description("A key that differs from the parameter only in case is refused too, because the SDK binds a flat parameter by its exact name.")]
	public async Task HandleCallToolErrors_ShouldRefuseAndSuggest_WhenAFlatParameterDiffersOnlyInCase() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateGetFsmModeContext("{\"EnvironmentName\":\"dev\"}");

		// Act
		string text = await CallAndReadTextAsync(context);

		// Assert
		text.Should().StartWith(Lead,
			because: "the SDK would not bind EnvironmentName to environmentName, so the call cannot run");
		text.Should().Contain("Unknown argument 'EnvironmentName' (did you mean 'environmentName'?).",
			because: "a case-only difference is the easiest rename to show");
		text.Should().Contain("Argument names are matched exactly, case included.",
			because: "the caller must learn the rule, not only this one name");
	}

	[Test]
	[Category("Unit")]
	[Description("A get-fsm-mode call with no arguments at all names the missing argument and reports no unknown keys.")]
	public async Task HandleCallToolErrors_ShouldNameTheMissingArgumentOnly_WhenNoArgumentsAreSent() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateGetFsmModeContext(null);

		// Act
		string text = await CallAndReadTextAsync(context);

		// Assert
		text.Should().StartWith(Lead,
			because: "a call with no arguments leaves out the required one like any other");
		text.Should().NotContain("Unknown argument",
			because: "nothing was sent, so nothing is unknown");
	}

	[Test]
	[Category("Unit")]
	[Description("The same kebab-case key sent through clio-run gets the same refusal, and the tool body does not run.")]
	public async Task ClioRun_ShouldNameTheMissingArgument_WhenGetFsmModeArgsUseKebabCase() {
		// Arrange
		IMcpToolInvokerRegistry registry = Substitute.For<IMcpToolInvokerRegistry>();
		McpServerTool tool = CreateGetFsmModeTool();
		registry.TryGetTool(FsmModeTool.GetFsmModeToolName, out Arg.Any<McpServerTool>())
			.Returns(call => {
				call[1] = tool;
				return true;
			});
		ClioRunExecutor executor = new(
			registry,
			Substitute.For<IMcpToolCompatibilityCatalog>(),
			new McpExecutionRouter(
				new McpToolExecutionMetadataReader(Substitute.For<IMcpToolCompatibilityCatalog>()),
				new McpWorkerCohort([]),
				new McpWorkerPathGate(() => McpHostTransportKind.Stdio, () => false),
				workerPathWired: true));
		JsonElement arguments = JsonDocument.Parse("{\"environment-name\":\"dev\"}").RootElement;

		// Act
		CallToolResult result = await executor.RunAsync(FsmModeTool.GetFsmModeToolName, arguments,
			destructiveSurface: false, McpRequestContextTestFactory.CreateCallToolContext(ClioRunTool.ToolName),
			CancellationToken.None);

		// Assert
		string text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
		result.IsError.Should().BeTrue(because: "a call that leaves out a required argument never reaches the tool body");
		text.Should().StartWith(Lead,
			because: "the nested dispatch answers with the same refusal as the direct call");
		text.Should().Contain("(did you mean 'environmentName'?)",
			because: "the suggestion travels on the clio-run path as well");
		text.Should().NotContain("arguments dictionary",
			because: "clio-run used to pass the SDK's binder text through as 'tool failed'");
	}

	[Test]
	[Category("Unit")]
	[Description("A tool with several required flat arguments names the first in the lead sentence and the rest after it, in the declared order.")]
	public async Task HandleCallToolErrors_ShouldListTheOtherMissingArguments_WhenSeveralAreLeftOut() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateFakeContext(
			nameof(FakeConnectionTool.Connect), "{\"url\":\"http://dev.example\"}");

		// Act
		string text = await CallAndReadTextAsync(context);

		// Assert
		text.Should().StartWith(
			"invalid-parameter-type: argument 'userName' for MCP tool 'fake-tool' is required and was not sent.",
			because: "the first missing argument in declared order leads");
		text.Should().Contain("Also missing: 'password'.",
			because: "every missing argument is named, so one retry can supply them all");
		text.Should().Contain("fake-tool takes: url (string, required), userName (string, required), password (string, required), verbose (boolean).",
			because: "the list marks which arguments are required and which have a default");
	}

	[Test]
	[Category("Unit")]
	[Description("A tool that takes one args record and is called with no arguments names args as missing and shows the record's shape.")]
	public async Task HandleCallToolErrors_ShouldShowTheRecordShape_WhenTheArgsRecordIsLeftOut() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateFakeContext(nameof(FakeRecordTool.Execute), "{}", new FakeRecordTool());

		// Act
		string text = await CallAndReadTextAsync(context);

		// Assert
		text.Should().StartWith(
			"invalid-parameter-type: argument 'args' for MCP tool 'fake-tool' is required and was not sent.",
			because: "the wrapper argument is what the SDK cannot bind");
		text.Should().Contain("Expected args: an object with entity (required), filter.",
			because: "the caller needs the record's members, not only that an object is expected");
	}

	[Test]
	[Category("Unit")]
	[Description("A call that sends every required argument and leaves out only defaulted ones is not refused.")]
	public void TryCreateMissingArgumentError_ShouldNotRefuse_WhenOnlyArgumentsWithADefaultAreLeftOut() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateFakeContext(
			nameof(FakeConnectionTool.Connect),
			"{\"url\":\"http://dev.example\",\"userName\":\"Supervisor\",\"password\":\"x\"}");

		// Act
		bool refused = McpToolErrorFilter.TryCreateMissingArgumentError(context, out CallToolResult? result);

		// Assert
		refused.Should().BeFalse(because: "a parameter with a default value is optional to the SDK binder");
		result.Should().BeNull(because: "a call that proceeds carries no refusal");
	}

	[Test]
	[Category("Unit")]
	[Description("A caller key carrying a line break is echoed without it, so it cannot forge a line in the refusal.")]
	public async Task HandleCallToolErrors_ShouldSanitizeTheUnknownKey_WhenItCarriesALineBreak() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateGetFsmModeContext("{\"env\\nNothing ran\":\"dev\"}");

		// Act
		string text = await CallAndReadTextAsync(context);

		// Assert
		text.Should().NotContain("\n",
			because: "caller-supplied text reaches the message only through the shared key sanitizer");
	}

	[Test]
	[Category("Unit")]
	[Description("A payload of many unknown keys names the first ten and counts the rest, so a large payload cannot make the refusal as large.")]
	public async Task HandleCallToolErrors_ShouldCapTheUnknownKeys_WhenManyAreSent() {
		// Arrange
		string payload = "{" + string.Join(",", Enumerable.Range(1, 12).Select(index => $"\"key{index}\":\"v\"")) + "}";
		RequestContext<CallToolRequestParams> context = CreateGetFsmModeContext(payload);

		// Act
		string text = await CallAndReadTextAsync(context);

		// Assert
		text.Should().Contain("'key10' and 2 more.",
			because: "the list stops at the shared echo cap and says how many keys it left out");
		text.Should().NotContain("'key11'",
			because: "a key past the cap is counted, not echoed");
	}

	[Test]
	[Category("Unit")]
	[Description("Pins the SDK behaviour the refusal mirrors: the schema marks required exactly the parameters with no default, a nullable one included, and the binder refuses a key that differs only in case. If an SDK upgrade changes either, this fails before the refusal starts disagreeing with the binder.")]
	public async Task Sdk_ShouldRequireExactlyWhatTheSchemaMarksRequired_AndBindFlatParametersByExactName() {
		// Arrange
		McpServerTool tool = McpServerTool.Create(
			typeof(FakeProbeTool).GetMethod(nameof(FakeProbeTool.Execute))!, new FakeProbeTool());
		RequestContext<CallToolRequestParams> caseOnly = McpRequestContextTestFactory.CreateCallToolContext(
			"fake-tool", Parse("{\"EnvironmentName\":\"dev\",\"note\":\"n\"}"));
		caseOnly.MatchedPrimitive = tool;
		RequestContext<CallToolRequestParams> exact = McpRequestContextTestFactory.CreateCallToolContext(
			"fake-tool", Parse("{\"environmentName\":\"dev\",\"note\":\"n\"}"));
		exact.MatchedPrimitive = tool;

		// Act
		List<string> required = McpToolRegistrySchemaContract.ReadRequired(tool.ProtocolTool.InputSchema);
		Func<Task> caseOnlyCall = async () => await tool.InvokeAsync(caseOnly, CancellationToken.None);
		CallToolResult exactResult = await tool.InvokeAsync(exact, CancellationToken.None);

		// Assert
		required.Should().Equal(["environmentName", "note"],
			because: "the schema requires every parameter without a default, the nullable note included");
		await caseOnlyCall.Should().ThrowAsync<ArgumentException>(
			because: "the SDK binds a flat parameter by its exact name, so the refusal compares ordinally too");
		exactResult.IsError.Should().NotBe(true,
			because: "the exact names bind and the defaulted verbose may be left out");
	}

	private static McpServerTool CreateGetFsmModeTool() =>
		McpServerTool.Create(
			typeof(FsmModeTool).GetMethod(nameof(FsmModeTool.GetFsmMode), BindingFlags.Public | BindingFlags.Instance)!,
			_ => throw new AssertionException("the tool body must not run"));

	private static RequestContext<CallToolRequestParams> CreateGetFsmModeContext(string? argumentsJson) {
		RequestContext<CallToolRequestParams> context = McpRequestContextTestFactory.CreateCallToolContext(
			FsmModeTool.GetFsmModeToolName, argumentsJson is null ? null : Parse(argumentsJson));
		context.MatchedPrimitive = CreateGetFsmModeTool();
		return WithInProcessRouting(context);
	}

	private static RequestContext<CallToolRequestParams> CreateFakeContext(
		string methodName, string argumentsJson, object? target = null) {
		RequestContext<CallToolRequestParams> context = McpRequestContextTestFactory.CreateCallToolContext(
			"fake-tool", Parse(argumentsJson));
		object instance = target ?? new FakeConnectionTool();
		context.MatchedPrimitive = McpServerTool.Create(instance.GetType().GetMethod(methodName)!, instance);
		return WithInProcessRouting(context);
	}

	// A matched context carries the router a real host registers, so the refusal is the only reason the
	// call stops. The empty cohort keeps every tool in-process.
	private static RequestContext<CallToolRequestParams> WithInProcessRouting(RequestContext<CallToolRequestParams> context) {
		context.Services = new ServiceCollection()
			.AddSingleton<IMcpExecutionRouter>(new McpExecutionRouter(
				new McpToolExecutionMetadataReader(new McpToolCompatibilityCatalog()),
				new McpWorkerCohort([]),
				new McpWorkerPathGate(() => McpHostTransportKind.Stdio, () => false),
				workerPathWired: true))
			.BuildServiceProvider();
		return context;
	}

	private static Dictionary<string, JsonElement> Parse(string json) =>
		JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

	private static async Task<string> CallAndReadTextAsync(RequestContext<CallToolRequestParams> context) {
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((_, _) => throw new AssertionException("the tool body must not run"));
		CallToolResult result = await handler(context, CancellationToken.None);
		result.IsError.Should().BeTrue(because: "a call that leaves out a required argument is refused before the tool body runs");
		return string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
	}

	public sealed class FakeConnectionTool {
		public string Connect(
			[Required] string url,
			[Required] string userName,
			[Required] string password,
			bool verbose = false) => "ok";
	}

	public sealed record FakeRecordArgs(
		[property: System.Text.Json.Serialization.JsonPropertyName("entity")]
		[property: System.Text.Json.Serialization.JsonRequired]
		string Entity,
		[property: System.Text.Json.Serialization.JsonPropertyName("filter")]
		string? Filter);

	public sealed class FakeRecordTool {
		public string Execute([Required] FakeRecordArgs args) => "ok";
	}

	public sealed class FakeProbeTool {
		public string Execute([Required] string environmentName, string? note, bool verbose = false) => "ok";
	}
}
