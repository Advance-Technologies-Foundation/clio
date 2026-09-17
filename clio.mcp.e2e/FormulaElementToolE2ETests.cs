using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Command.ProcessModel;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the Formula element (ENG-92712) over the real MCP path. NOT in CI — run manually,
/// gated on the <c>process-designer</c> feature and a reachable environment carrying CrtProcessBuilder 1.6.3.4
/// or later.
/// <para>The element is checked through what a caller can SEE: the build token it round-trips to, the
/// expression after the server expanded its parameter names, and the target resolved back to the name a build
/// would take. The target is deliberately exercised in BOTH forms — a process parameter and another element's
/// parameter — because they are stored as different map paths and only the element form can address a
/// parameter that does not exist yet when the descriptor is written.</para>
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class FormulaElementToolE2ETests {

	private const string ToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;

	#region Methods: Tests

	[Test]
	[Description("Over the real MCP path, create-business-process builds a formula element writing to a process parameter, and describe reads it back: the element resolves to the formulaTask build token, the body comes back with its parameter name expanded into the platform meta path, and the target is reported as the parameter NAME a build would take.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process builds a formula element and describe reads the block back")]
	public async Task CreateBusinessProcess_Should_BuildFormulaElement_AndReadItBack() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string processName = $"UsrClioBpFormulaE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildProcessParameterTargetDescriptor(processName)
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a formula element with a body and a process-parameter target must build without a transport error");
		JsonSerializer.Serialize(callResult).Should().Contain("created (UId:",
			because: "only a genuinely successful build logs the created-schema line");

		DescribeProcessResult graph = ParseDescribeGraph(await DescribeAsync(context, processName));
		DescribedElement formula = graph.Elements.Single(element => element.Name == "Formula1");
		formula.BuildType.Should().Be("formulatask",
			because: "the element round-trips to its own build token rather than to the script task it derives from");
		string described = JsonSerializer.Serialize(formula);
		described.Should().Contain("[Parameter:",
			because: "the body is stored EXPANDED: a name the caller wrote is turned into the platform meta path, "
				+ "which is the only form the formula engine evaluates");
		described.Should().NotContain("[#Amount#]",
			because: "an unexpanded name reaches the platform as 'Expression expected (at index 0)'");
		described.Should().Contain("Doubled",
			because: "the target is reported as the parameter NAME, so a describe read-back feeds back into a build");
	}

	[Test]
	[Description("A formula element can write into ANOTHER element's parameter, which is stored as a two-part map path rather than as a bare parameter UId, and describe reports both halves by name.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process builds a formula targeting another element's parameter")]
	public async Task CreateBusinessProcess_Should_BuildFormulaTargetingAnElementParameter() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string processName = $"UsrClioBpFormulaElementTargetE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildElementParameterTargetDescriptor(processName)
		});

		// Assert
		JsonSerializer.Serialize(callResult).Should().Contain("created (UId:",
			because: "an element-parameter target is resolved after every element exists, so declaring the target "
				+ "element AFTER the formula must still build");

		DescribeProcessResult graph = ParseDescribeGraph(await DescribeAsync(context, processName));
		string described = JsonSerializer.Serialize(graph.Elements.Single(element => element.Name == "Formula1"));
		described.Should().Contain("Task1",
			because: "describe reports the target element by name");
		described.Should().Contain("Recommendation",
			because: "and the parameter of that element by name, which together are what a build takes back");
	}

	[Test]
	[Description("A formula element with no target is refused by the server rather than saved as an element that computes a value and writes it nowhere.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process refuses a formula element with no target")]
	public async Task CreateBusinessProcess_Should_RefuseFormulaWithoutATarget() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string processName = $"UsrClioBpFormulaNoTargetE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildNoTargetDescriptor(processName)
		});

		// Assert
		string resultJson = JsonSerializer.Serialize(callResult);
		resultJson.Should().NotContain("created (UId:",
			because: "an element that writes nowhere must abort the build, not ship as a silently useless element");
		resultJson.Should().Contain("Formula1",
			because: "the refusal names the element, which is what a caller with several of them can act on");
	}

	#endregion

	#region Methods: Private

	private static string BuildProcessParameterTargetDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Formula E2E",
		  "packageName": "Custom",
		  "parameters": [
		    { "name": "Amount", "type": "Integer", "direction": "In", "value": "7" },
		    { "name": "Doubled", "type": "Integer", "direction": "Variable", "value": "0" }
		  ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "Formula1", "type": "formulaTask", "caption": "Double the amount",
		      "formula": { "body": "[#Amount#] * 2", "resultProcessParameter": "Doubled" } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "Formula1" },
		    { "source": "Formula1", "target": "EndEvent1" }
		  ]
		}
		""";

	private static string BuildElementParameterTargetDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Formula Element Target E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "Formula1", "type": "formulaTask", "caption": "Build the recommendation",
		      "formula": { "body": "\"handled\"", "elementName": "Task1", "elementParameter": "Recommendation" } },
		    { "name": "Task1", "type": "performTask", "caption": "Do the work" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "Formula1" },
		    { "source": "Formula1", "target": "Task1" },
		    { "source": "Task1", "target": "EndEvent1" }
		  ]
		}
		""";

	private static string BuildNoTargetDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Formula No Target E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "Formula1", "type": "formulaTask", "caption": "Computes nothing anybody reads",
		      "formula": { "body": "1 + 1" } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "Formula1" },
		    { "source": "Formula1", "target": "EndEvent1" }
		  ]
		}
		""";

	private static DescribeProcessResult ParseDescribeGraph(CallToolResult describeResult) {
		CommandExecutionEnvelope envelope = McpCommandExecutionParser.Extract(describeResult);
		string graphJson = envelope.Output!
			.Select(message => message.Value)
			.First(value => !string.IsNullOrWhiteSpace(value)
				&& value!.TrimStart().StartsWith("{", StringComparison.Ordinal))!;
		return JsonSerializer.Deserialize<DescribeProcessResult>(graphJson,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
	}

	private static async Task<CallToolResult> DescribeAsync(ArrangeContext context, string processCode) =>
		await context.Session.CallToolAsync(
			DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["process-name"] = processCode
				}
			},
			context.CancellationTokenSource.Token);

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext context,
			Dictionary<string, object?> args) {
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		toolNames.Should().Contain(ToolName,
			because: "the create-business-process tool must be discoverable before the end-to-end call");
		return await context.Session.CallToolAsync(
			ToolName, new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
	}

	// No 'requireReachableEnvironment' switch: every fixture here calls the server, so the parameter was passed
	// true at all four call sites and the false branch could never run. A dead branch in an arrange helper reads
	// as coverage that exists; reinstate it only alongside a fixture that actually needs it.
	private static async Task<ArrangeContext> ArrangeAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore(
				"Configure McpE2E:Sandbox:EnvironmentName (with a CrtProcessBuilder 1.6.3.4 or later, which "
				+ "builds the formula element) to run the Approval MCP E2E tests.");
		}

		if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName!)) {
			Assert.Ignore(
				$"Formula MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
		}

		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource, environmentName);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string? EnvironmentName) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

	#endregion

}
