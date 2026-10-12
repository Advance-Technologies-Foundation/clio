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
		envelope.VersionName.Should().Be(ProcessCode,
			because: "the answer names the version it launched (ENG-101880 TC-06)");
		envelope.IsActiveVersion.Should().BeTrue(
			because: "a single-version process is its own active version, so the standing must not read as unknown (TC-06)");
		envelope.Warnings.Should().BeNullOrEmpty(
			because: "running the active version of a process without versions needs no caveat (TC-06)");
	}

	// ENG-103024: a process with a Perform task parks on it after creating an activity. Present on the product
	// stands the sandbox runs on; a stand without it skips the test.
	private const string ActivityProcessCode = "CreateActivityProcess";

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("A process that parks on a Perform task is waited for, names the element it waits on, reports the activity it created in a watched entity, and with cleanup cancels the run and deletes exactly that activity.")]
	[AllureTag(ToolName)]
	[AllureName("Run process reports and cleans up the records a parked run created")]
	[AllureDescription("Starts the real clio MCP server, launches CreateActivityProcess through clio-run with watch [Activity] and cleanup true, and verifies waitingOn, created and cleanup, then reads the activity back to prove it is gone.")]
	public async Task RunProcess_Should_Report_And_Clean_Up_What_A_Parked_Run_Created() {
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
			arrangeContext, ActivityProcessCode, environmentName!, parameters: null,
			extraArgs: new Dictionary<string, object?> {
				["watch"] = new[] { "Activity" },
				["wait-timeout"] = 3,
				["cleanup"] = true
			});
		if (envelope.Status is null && envelope.Error is not null) {
			Assert.Ignore($"'{ActivityProcessCode}' is not available on '{environmentName}': {envelope.Error}");
		}

		// Assert
		envelope.Error.Should().BeNull(because: $"a parked run is not a failure. Error: {envelope.Error}");
		envelope.Created.Should().ContainSingle(record => record.Entity == "Activity",
			because: "the Perform task created exactly one activity, and the watch must report it");
		envelope.Cleanup.Should().NotBeNull(because: "cleanup was requested");
		envelope.Cleanup!.Failed.Should().BeEmpty(
			because: "cleanup cancels the parked run first, so the activity it uses can be deleted");
		envelope.Cleanup.Deleted.Select(record => record.Id).Should().BeEquivalentTo(
			envelope.Created!.Select(record => record.Id),
			because: "cleanup deletes exactly what created lists, nothing else");
		envelope.Status.Should().Be("cancelled",
			because: "the run parks on the Perform task until cleanup cancels it");
		envelope.Warnings.Should().Contain(warning => warning.Contains("ProcessSchemaUserTask"),
			because: "the answer names the user task the run was parked on before cleanup cancelled it");
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("A straight-through run with a watch on an entity it does not write reports an empty created list, not a missing one, and cleanup deletes nothing.")]
	[AllureTag(ToolName)]
	[AllureName("Run process reports an empty created list for a run that creates nothing")]
	[AllureDescription("Launches TrimHtml through clio-run with watch [Activity] and cleanup true and verifies created and cleanup are empty.")]
	public async Task RunProcess_Should_Report_Nothing_Created_For_A_Side_Effect_Free_Run() {
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
			arrangeContext, ProcessCode, environmentName!,
			new Dictionary<string, object?> { [InputParameter] = InputValue },
			extraArgs: new Dictionary<string, object?> { ["watch"] = new[] { "Activity" }, ["cleanup"] = true });

		// Assert
		envelope.Status.Should().Be("completed", because: $"TrimHtml runs straight through. Error: {envelope.Error}");
		envelope.Created.Should().NotBeNull(because: "a watched run always answers created, empty when nothing appeared");
		envelope.Created.Should().BeEmpty(because: "TrimHtml writes no records");
		envelope.Cleanup!.Deleted.Should().BeEmpty(because: "cleanup touches only what the run created");
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) =>
		await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName);
}
