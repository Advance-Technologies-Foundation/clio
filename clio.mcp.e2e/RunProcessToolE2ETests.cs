using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;

namespace Clio.Mcp.E2E;

// These need a live stand; configure Sandbox:EnvironmentName or they skip.
[TestFixture]
[AllureNUnit]
[AllureFeature(RunProcessTool.ToolName)]
[NonParallelizable]
public sealed class RunProcessToolE2ETests : McpContractFixtureBase {

	private const string ToolName = RunProcessTool.ToolName;

	// Pinned rather than read from Sandbox.ProcessCode: with a configurable process the fixture cannot know
	// what to expect back, so the launch test passes on any status - including one where nothing ran.
	private const string ProcessCode = "TrimHtml";

	private const string InputParameter = "InputText";
	private const string OutputParameter = "OutputText";
	private const string InputValue = "<b>hello</b> <i>world</i>";
	private const string ExpectedOutput = "hello world";

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
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to run run-process E2E.");
		}

		if (!await CanReachEnvironmentAsync(settings, environmentName!)) {
			Assert.Ignore($"run-process MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
		}

		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(5));

		// Act
		RunProcessEnvelope envelope = await RunProcessContractToolE2ETests.ActAsync(
			arrangeContext,
			ProcessCode,
			environmentName!,
			new Dictionary<string, object?> { ["ThisParameterDoesNotExist"] = "x" });

		// Assert
		envelope.Error.Should().Contain(InputParameter,
			because: "an unknown parameter code is a hard error — the platform would silently drop the value — "
				+ "and the rejection must ENUMERATE the codes the process really accepts, which is the half "
				+ "that lets a caller correct the call rather than guess again");
		envelope.Status.Should().BeNull(
			because: "the call was rejected before launch, so there is no run state to report");
		envelope.ProcessId.Should().BeNull(
			because: "validation runs before the server call, so nothing may have been launched");
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Launching the configured sandbox process reports one of the contract's statuses, and a real process id only ever arrives with a status from the platform's own scale.")]
	[AllureTag(ToolName)]
	[AllureName("Run process launches the configured sandbox process")]
	[AllureDescription("Starts the real clio MCP server, dispatches run-process through clio-run for the configured sandbox process, and verifies the envelope reports a known status, pairing a real process id only with a platform status.")]
	public async Task RunProcess_Should_Launch_The_Configured_Sandbox_Process() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run run-process end-to-end tests against a live sandbox.");
		}

		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to run run-process E2E.");
		}

		if (!await CanReachEnvironmentAsync(settings, environmentName!)) {
			Assert.Ignore($"run-process MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
		}

		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(5));

		// Act
		RunProcessEnvelope envelope = await RunProcessContractToolE2ETests.ActAsync(
			arrangeContext,
			ProcessCode,
			environmentName!,
			new Dictionary<string, object?> { [InputParameter] = InputValue },
			resultParameters: [OutputParameter]);

		// Assert
		envelope.Error.Should().BeNull(
			because: $"a side-effect-free process must launch cleanly. Error: {envelope.Error}");
		envelope.Status.Should().Be("completed",
			because: "this process runs straight through with nothing to suspend on, so any other status "
				+ "means the launch did not do what the contract promises");
		envelope.ProcessId.Should().NotBeNullOrWhiteSpace(
			because: "a completed run always carries the instance id, which is also its SysProcessLog key");
		envelope.ResultParameterValues.Should().ContainKey(OutputParameter,
			because: "the Output parameter was requested, so its value must come back");
		envelope.ResultParameterValues![OutputParameter].ToString().Should().Be(ExpectedOutput,
			because: "the input value reached the process verbatim and its output came back intact — an "
				+ "exact round trip is what proves the run really happened, which a status string alone "
				+ "cannot show");
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["ping-app", "-e", environmentName]);
		return result.ExitCode == 0;
	}
}
