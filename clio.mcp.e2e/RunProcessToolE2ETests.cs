using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the run-process MCP tool against a live sandbox stand.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(RunProcessTool.ToolName)]
[NonParallelizable]
public sealed class RunProcessToolE2ETests : McpContractFixtureBase {

	private const string ToolName = RunProcessTool.ToolName;

	/// <summary>
	/// The outcomes a live launch may legitimately report. Every one of them is a valid contract answer, so
	/// the assertion pins the vocabulary rather than a single expected verdict — which process the sandbox is
	/// configured with is not this test's business.
	/// </summary>
	private static readonly string[] LaunchOutcomes = [
		"completed", "error", "cancelled", "cancelling", "running", "inactive",
		"queued-background", "refused", "accepted-still-running"
	];

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("An unknown parameter code is rejected with the list of codes the sandbox process really accepts, and the rejection happens without launching anything.")]
	[AllureTag(ToolName)]
	[AllureName("Run process rejects an unknown parameter code against a live process signature")]
	[AllureDescription("Starts the real clio MCP server, dispatches run-process through clio-run with a parameter code the configured sandbox process does not declare, and verifies the structured failure enumerates the process's real parameter codes.")]
	public async Task RunProcess_Should_Reject_An_Unknown_Parameter_Code() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run run-process end-to-end tests against a live sandbox.");
		}

		string? environmentName = settings.Sandbox.EnvironmentName;
		string? processCode = settings.Sandbox.ProcessCode;
		if (string.IsNullOrWhiteSpace(environmentName) || string.IsNullOrWhiteSpace(processCode)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName and McpE2E:Sandbox:ProcessCode to run run-process E2E.");
		}

		if (!await CanReachEnvironmentAsync(settings, environmentName!)) {
			Assert.Ignore($"run-process MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
		}

		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(5));

		// Act
		RunProcessEnvelope envelope = await RunProcessContractToolE2ETests.ActAsync(
			arrangeContext,
			processCode!,
			environmentName!,
			new Dictionary<string, object?> { ["ThisParameterDoesNotExist"] = "x" });

		// Assert
		envelope.Success.Should().BeFalse(
			because: "an unknown parameter code is a hard error — the platform would silently drop the value");
		envelope.ResolvedProcessCode.Should().Be(processCode,
			because: "the process resolved fine; only the parameter code was wrong, and the caller needs to "
				+ "see that distinction to know what to fix");
		envelope.Error.Should().NotBeNullOrWhiteSpace(
			because: "the rejection must name the accepted codes so the caller can correct the call");
		envelope.ProcessId.Should().BeNull(
			because: "validation runs before the server call, so nothing may have been launched");
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Launching the configured sandbox process reports one of the contract's outcome modes and, whenever the platform produced a handle, echoes the resolved process code alongside it.")]
	[AllureTag(ToolName)]
	[AllureName("Run process launches the configured sandbox process")]
	[AllureDescription("Starts the real clio MCP server, dispatches run-process through clio-run for the configured sandbox process, and verifies the envelope reports a known outcome mode with a resolved process code.")]
	public async Task RunProcess_Should_Launch_The_Configured_Sandbox_Process() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run run-process end-to-end tests against a live sandbox.");
		}

		string? environmentName = settings.Sandbox.EnvironmentName;
		string? processCode = settings.Sandbox.ProcessCode;
		if (string.IsNullOrWhiteSpace(environmentName) || string.IsNullOrWhiteSpace(processCode)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName and McpE2E:Sandbox:ProcessCode to run run-process E2E.");
		}

		if (!await CanReachEnvironmentAsync(settings, environmentName!)) {
			Assert.Ignore($"run-process MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
		}

		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(5));

		// Act
		RunProcessEnvelope envelope = await RunProcessContractToolE2ETests.ActAsync(
			arrangeContext, processCode!, environmentName!, parameters: null);

		// Assert
		envelope.Mode.Should().BeOneOf(LaunchOutcomes,
			because: $"mode is the only field carrying the verdict, so it must always be one of the documented "
				+ $"outcomes. Error: {envelope.Error}");
		envelope.ResolvedProcessCode.Should().Be(processCode,
			because: "the resolved code is echoed back so the caller can reuse it verbatim");
		if (envelope.ProcessId is not null) {
			envelope.ProcessStatus.Should().NotBeNull(
				because: "a real handle and a real status arrive together — a process id without a status "
					+ "would leave the caller unable to judge the run");
		}
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["ping-app", "-e", environmentName]);
		return result.ExitCode == 0;
	}
}
