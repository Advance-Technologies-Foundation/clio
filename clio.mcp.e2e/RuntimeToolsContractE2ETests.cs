using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>Real-process contract coverage without deployment side effects.</summary>
[TestFixture(false), TestFixture(true), Category("McpE2E.NoEnvironment"), AllureNUnit, NonParallelizable]
[AllureFeature("Runtime host tools")]
public class RuntimeToolsContractE2ETests(bool runtimeHost) : McpContractFixtureBase {
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		settings.RuntimeHost = runtimeHost;
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = CreateIsolatedClioHome("""{"Autoupdate":false,"Features":{"runtime":true},"Environments":{}}""", GetType().Name);
	}
	[TestCase(RuntimeTools.ImagesName, false)]
	[TestCase(RuntimeTools.ListName, false)]
	[TestCase(RuntimeTools.StatusName, false)]
	[TestCase(RuntimeTools.CreateName, true)]
	[TestCase(RuntimeTools.BuildName, true)]
	[TestCase(RuntimeTools.AttachName, true)]
	[TestCase(RuntimeTools.DetachName, true)]
	[TestCase(InstallOperatorTool.ToolName, true)]
	[Description("Every runtime tool is discoverable behind the feature and refuses execution without explicit host opt-in.")]
	[AllureName("Runtime host opt-in is required for every tool")]
	[AllureDescription("Calls all runtime tools through the real MCP process and checks rejection before Kubernetes or workspace changes.")]
	public async Task Tools_ShouldRejectWithoutHostOptIn(string name, bool destructive) {
		// Arrange
		await using var context = Arrange();
		var args = new Dictionary<string, object?>();
		if (name != RuntimeTools.DetachName && name != InstallOperatorTool.ToolName) args["context"] = "not-a-real-context";
		if (name is RuntimeTools.StatusName or RuntimeTools.CreateName or RuntimeTools.AttachName) args["name"] = "probe";
		if (name == RuntimeTools.CreateName) args["image"] = "creatio-dev:probe";
		if (name == RuntimeTools.BuildName) args["source"] = "missing.zip";
		if (name is RuntimeTools.AttachName or RuntimeTools.DetachName) args["workspace"] = "missing-workspace";
		if (name == RuntimeTools.AttachName) args["sshAlias"] = "missing-alias";
		if (name == InstallOperatorTool.ToolName) args["target"] = "unsupported-test-target";
		// Act
		var result = destructive ? await context.Session.CallDestructiveAsync(name, args, context.CancellationTokenSource.Token)
			: await context.Session.CallToolAsync(name, args, context.CancellationTokenSource.Token);
		var execution = McpCommandExecutionParser.Extract(result);
		// Assert
		result.IsError.Should().NotBeTrue(because: "valid MCP arguments return a structured command failure envelope");
		execution.ExitCode.Should().Be(1, because: "host opt-out or invalid targets must fail before mutations");
		execution.Output.Should().Contain(m => m.MessageType == LogDecoratorType.Error && (!runtimeHost ? m.Value!.Contains("--runtime-host") : !m.Value!.Contains("require a developer-host server")), because: "a clear host opt-in refusal proves command execution was not reached");
	}
	[TestCase("rancher-desktop"), TestCase("omen")]
	[Description("Explicitly opted-in live validation reads the operator image catalogue through MCP.")]
	[AllureName("Runtime images returns the live catalogue")]
	[AllureDescription("Read-only opt-in validation against configured clusters; no deployment or build is performed.")]
	public async Task Images_ShouldReadLiveCatalogue(string cluster) {
		// Arrange
		if (!runtimeHost || Environment.GetEnvironmentVariable("CLIO_RUNTIME_LIVE_TEST") != "1") Assert.Ignore("Enable CLIO_RUNTIME_LIVE_TEST=1 on a configured developer host.");
		await using var context = Arrange();
		// Act
		var result = await context.Session.CallToolAsync(RuntimeTools.ImagesName, new Dictionary<string, object?> { ["context"] = cluster }, context.CancellationTokenSource.Token);
		var execution = McpCommandExecutionParser.Extract(result);
		// Assert
		Allure.Net.Commons.AllureApi.Step("Verify successful result", () => {
			result.IsError.Should().NotBeTrue(because: "the live catalogue read should succeed");
			execution.ExitCode.Should().Be(0, because: "the selected operator is reachable");
			execution.Output.Should().Contain(m => m.MessageType == LogDecoratorType.Info, because: "success is explicitly reported");
		});
		Allure.Net.Commons.AllureApi.Step("Verify returned distributions", () => {
			var json = execution.Output!.First(m => m.Value?.TrimStart().StartsWith("[") == true).Value!;
			using var catalogue = System.Text.Json.JsonDocument.Parse(json);
			catalogue.RootElement.GetArrayLength().Should().BeGreaterThan(0, because: "these test clusters have built distributions");
			catalogue.RootElement[0].GetProperty("images").GetArrayLength().Should().BeGreaterThan(0, because: "agents need exact deployment references");
		});
	}}

/// <summary>Disabled runtime features must not appear in the lazy MCP catalogue.</summary>
[TestFixture, Category("McpE2E.NoEnvironment"), AllureNUnit, NonParallelizable]
[AllureFeature("Runtime host tools")]
public class RuntimeToolsDisabledE2ETests : McpContractFixtureBase {
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		settings.RuntimeHost = true;
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = CreateIsolatedClioHome("""{"Autoupdate":false,"Features":{"runtime":false},"Environments":{}}""", GetType().Name);
	}
	[Test, Description("Host opt-in cannot bypass the runtime feature flag.")]
	[AllureName("Disabled runtime feature hides all runtime tools")]
	[AllureDescription("Checks the actual lazy discovery catalogue from an isolated MCP server process.")]
	public async Task DisabledFeature_ShouldHideAllRuntimeTools() {
		// Arrange
		await using var context = Arrange();
		string[] names = [RuntimeTools.ImagesName, RuntimeTools.ListName, RuntimeTools.StatusName, RuntimeTools.CreateName,
			RuntimeTools.BuildName, RuntimeTools.AttachName, RuntimeTools.DetachName, InstallOperatorTool.ToolName];
		// Act
		var index = await context.Session.GetToolContractIndexAsync(context.CancellationTokenSource.Token);
		// Assert
		Allure.Net.Commons.AllureApi.Step("Verify runtime tools are absent", () =>
			index.Should().NotContain(item => names.Contains(item.Name), because: "runtime is disabled despite host opt-in"));
	}
}

/// <summary>A pod must never treat itself as the developer's machine.</summary>
[TestFixture, Category("McpE2E.NoEnvironment"), AllureNUnit, NonParallelizable]
[AllureFeature("Runtime host tools")]
public class RuntimeToolsContainerE2ETests : McpContractFixtureBase {
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		settings.RuntimeHost = true;
		settings.ProcessEnvironmentVariables["KUBERNETES_SERVICE_HOST"] = "container-test";
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = CreateIsolatedClioHome("""{"Autoupdate":false,"Features":{"runtime":true},"Environments":{}}""", GetType().Name);
	}
	[Test, Description("Container detection refuses host operations even with both opt-ins.")]
	[AllureName("Container-hosted runtime operations are refused")]
	[AllureDescription("Starts a real MCP process with Kubernetes container environment and checks an early refusal.")]
	public async Task Container_ShouldRejectRuntimeOperations() {
		// Arrange
		await using var context = Arrange();
		// Act
		var result = await context.Session.CallToolAsync(RuntimeTools.ImagesName, new Dictionary<string, object?> { ["context"] = "not-a-real-context" }, context.CancellationTokenSource.Token);
		var execution = McpCommandExecutionParser.Extract(result);
		// Assert
		Allure.Net.Commons.AllureApi.Step("Verify container refusal before catalogue access", () => {
			result.IsError.Should().NotBeTrue(because: "the refusal uses the command envelope");
			execution.ExitCode.Should().Be(1, because: "containers cannot opt in to developer-host operations");
			execution.Output.Should().Contain(m => m.MessageType == LogDecoratorType.Error && m.Value!.Contains("Container-hosted"), because: "the reason should be actionable");
		});
	}
}
