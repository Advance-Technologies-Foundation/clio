using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Stand-free end-to-end contract tests for the run-process MCP tool: discoverability, the destructive
/// flag it is advertised with, and the failure envelope. None of these reach a Creatio instance.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(RunProcessTool.ToolName)]
[Category("McpE2E.NoEnvironment")]
[Parallelizable(ParallelScope.Self)]
public sealed class RunProcessContractToolE2ETests : McpContractFixtureBase {

	private const string ToolName = RunProcessTool.ToolName;

	[Test]
	[Description("run-process is discoverable through the get-tool-contract compact index even though it is not advertised in tools/list, so an agent can find it on the lazy surface.")]
	[AllureTag(ToolName)]
	[AllureName("Run process is discoverable on the lazy surface")]
	public async Task RunProcess_Should_Be_Discoverable_On_The_Lazy_Surface() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(5));

		// Act
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "a long-tail tool that the index does not list is unreachable in practice — an agent has "
				+ "no other way to learn the name exists");
	}

	[Test]
	[Description("The discovery index reports run-process as destructive, which is what makes a host prompt before a launch. A long-tail tool carries no tools/list annotation, so the index flag is the only signal a caller has.")]
	[AllureTag(ToolName)]
	[AllureName("Run process is advertised as destructive in the discovery index")]
	public async Task RunProcess_Should_Be_Advertised_As_Destructive_In_The_Index() {
		// Arrange
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(5));

		// Act
		IReadOnlyList<ToolContractIndexEntry> index =
			await arrangeContext.Session.GetToolContractIndexAsync(arrangeContext.CancellationTokenSource.Token);
		ToolContractIndexEntry? entry = index.FirstOrDefault(
			item => string.Equals(item.Name, ToolName, StringComparison.OrdinalIgnoreCase));

		// Assert
		entry.Should().NotBeNull(because: "the tool must appear in the index to be discoverable at all");
		entry!.Destructive.Should().BeTrue(
			because: "launching a process changes data, and for a non-resident tool this index flag is the "
				+ "only thing that tells a host to confirm before dispatching it");
	}

	[Test]
	[Description("Dispatched through clio-run against an unknown environment, run-process returns a structured failure naming the unresolved environment rather than an unstructured transport error.")]
	[AllureTag(ToolName)]
	[AllureName("Run process reports an unknown environment as a structured failure")]
	public async Task RunProcess_Should_Report_Invalid_Environment_As_Structured_Failure() {
		// Arrange
		string invalidEnvironmentName = $"missing-run-process-env-{Guid.NewGuid():N}";
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(5));

		// Act
		RunProcessEnvelope envelope = await ActAsync(
			arrangeContext, "UsrMissingProcess", invalidEnvironmentName, parameters: null);

		// Assert
		envelope.Error.Should().NotBeNullOrWhiteSpace(
			because: "an unknown environment cannot launch anything, and error is the failure signal");
		envelope.Status.Should().BeNull(
			because: "the call was rejected before launch, so there is no run state to report");
	}

	/// <summary>
	/// Dispatches run-process through <c>clio-run</c>. The tool is not advertised in <c>tools/list</c> (it is
	/// long tail), so an executor is how a caller reaches it.
	/// </summary>
	internal static async Task<RunProcessEnvelope> ActAsync(
		ArrangeContext arrangeContext,
		string processName,
		string environmentName,
		Dictionary<string, object?>? parameters,
		IReadOnlyList<string>? resultParameters = null) {
		Dictionary<string, object?> args = new() {
			["process-name"] = processName,
			["environment-name"] = environmentName
		};
		if (parameters is not null) {
			args["parameters"] = parameters;
		}
		if (resultParameters is not null) {
			args["result-parameters"] = resultParameters;
		}

		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ClioRunTool.ToolName,
			new Dictionary<string, object?> {
				["command"] = RunProcessTool.ToolName,
				["args"] = args
			},
			arrangeContext.CancellationTokenSource.Token);

		return EntitySchemaStructuredResultParser.Extract<RunProcessEnvelope>(callResult);
	}
}
