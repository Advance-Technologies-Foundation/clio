using System.Text.Json.Nodes;
using System.Text.Json;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Progress;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>Destructive opt-in proof of real dbHub lifecycle warning behavior.</summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[Category("LocalOnly")]
[Category("McpE2E.Manual")]
[Explicit("Developer-local validation only: installs a disposable Creatio instance from a local archive and must never run in TeamCity.")]
[AllureFeature("dbHub lifecycle")]
[NonParallelizable]
public sealed class DbHubLifecycleWarningE2ETests {
	private const string DeployToolName = "deploy-creatio";

	[Test]
	[Description("Deploys and uninstalls one disposable Creatio instance through MCP while isolated dbHub is offline, proving both lifecycle hooks complete with warnings.")]
	[AllureName("Offline dbHub produces non-fatal deploy and uninstall MCP warnings")]
	[AllureDescription("Uses an explicitly configured disposable archive, IIS port, local database, Redis, and isolated CLIO_HOME. The environment must have dbHub synchronization enabled while its dbHub server is stopped.")]
	public async Task Lifecycle_ShouldCompleteWithWarnings_WhenDbHubIsOffline() {
		// Arrange
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("The disposable IIS deployment lifecycle is Windows-only.");
		}
		if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEAMCITY_VERSION"))) {
			Assert.Ignore("This archive-backed Creatio installation test is forbidden in TeamCity.");
		}
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests || !settings.Sandbox.RequireDbHubWarning) {
			Assert.Ignore("Enable destructive MCP tests and Sandbox:RequireDbHubWarning for this disposable proof.");
		}
		settings.Sandbox.EnvironmentName.Should().NotBeNullOrWhiteSpace(
			because: "the destructive fixture must name one disposable environment");
		settings.Sandbox.DeploymentArchivePath.Should().NotBeNullOrWhiteSpace(
			because: "the destructive fixture must use an explicitly selected Creatio archive");
		File.Exists(settings.Sandbox.DeploymentArchivePath).Should().BeTrue(
			because: "the selected disposable deployment archive must exist");
		settings.Sandbox.DeploymentSitePort.Should().BeInRange(1, 65535,
			because: "the disposable IIS deployment requires an explicit valid port");
		settings.Sandbox.DeploymentDbServerName.Should().NotBeNullOrWhiteSpace(
			because: "the destructive fixture must target an explicitly configured local database server");
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();

		using CancellationTokenSource cts = new(TimeSpan.FromMinutes(20));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cts.Token);
		session.StartCapturingProgressNotifications();
		bool deployed = false;
		try {
			// Act: deploy with configured-but-offline dbHub.
			ProgressToken deployToken = new($"dbhub-deploy-warning-{Guid.NewGuid():N}");
			CallToolResult deployResult = await session.CallToolWithRawProgressAsync(
				DeployToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["siteName"] = settings.Sandbox.EnvironmentName,
						["zipFile"] = settings.Sandbox.DeploymentArchivePath,
						["sitePort"] = settings.Sandbox.DeploymentSitePort,
						["dbServerName"] = settings.Sandbox.DeploymentDbServerName,
						["redisServerName"] = settings.Sandbox.DeploymentRedisServerName
					}
				}, deployToken, cts.Token);
			deployed = true;
			IReadOnlyList<JsonNode> deployProgress = await session.WaitForCapturedProgressAsync(
				deployToken, nodes => HasWarning(nodes, StageIds.SyncDbHubSource), TimeSpan.FromMinutes(2), cts.Token);
			AssertWarningCompletion(deployResult, deployProgress, StageIds.SyncDbHubSource,
				settings.Sandbox.SecretSentinel);
			// Act: uninstall the same environment while dbHub remains offline.
			ProgressToken uninstallToken = new($"dbhub-uninstall-warning-{Guid.NewGuid():N}");
			CallToolResult uninstallResult = await session.CallToolWithRawProgressAsync(
				UninstallCreatioTool.UninstallCreatioToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = settings.Sandbox.EnvironmentName
					}
				}, uninstallToken, cts.Token);
			IReadOnlyList<JsonNode> uninstallProgress = await session.WaitForCapturedProgressAsync(
				uninstallToken, nodes => HasWarning(nodes, StageIds.RemoveDbHubSource),
				TimeSpan.FromMinutes(2), cts.Token);
			AssertWarningCompletion(uninstallResult, uninstallProgress, StageIds.RemoveDbHubSource,
				settings.Sandbox.SecretSentinel);
			deployed = false;
		}
		finally {
			if (deployed) {
				try {
					using CancellationTokenSource cleanupCts = new(TimeSpan.FromMinutes(5));
					CallToolResult cleanupResult = await session.CallToolAsync(
						UninstallCreatioTool.UninstallCreatioToolName,
						new Dictionary<string, object?> {
							["args"] = new Dictionary<string, object?> {
								["environment-name"] = settings.Sandbox.EnvironmentName
							}
						}, cleanupCts.Token);
					deployed = McpCommandExecutionParser.Extract(cleanupResult).ExitCode != 0;
				}
				catch (Exception exception) when (exception is InvalidOperationException or IOException
					or OperationCanceledException) {
					TestContext.Error.WriteLine($"Best-effort disposable uninstall failed: {exception.GetType().Name}");
				}
				if (deployed) {
					TestContext.Error.WriteLine(
						$"Disposable environment '{settings.Sandbox.EnvironmentName}' may remain after bounded cleanup.");
				}
			}
		}
	}

	private static void AssertWarningCompletion(CallToolResult result, IReadOnlyList<JsonNode> progress,
		string stageId, string? secretSentinel) {
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(result);
		execution.ExitCode.Should().Be(0, because: "dbHub is a best-effort lifecycle integration");
		result.IsError.Should().NotBeTrue(because: "an offline dbHub warning must not become an MCP error");
		IReadOnlyList<ClioStageEvent> events = ExtractEvents(progress);
		events.Any(stageEvent => stageEvent.Stage?.StageId == stageId
			&& stageEvent.Stage.Status == ClioStageEventContract.StageStatuses.Warning).Should().BeTrue(
			because: "the exact dbHub lifecycle stage must expose its non-fatal warning");
		events[^1].RunCompleted!.Outcome.Should().Be(ClioStageEventContract.RunOutcomes.SuccessWithWarnings,
			because: "clients must distinguish clean success from best-effort integration warnings");
		if (!string.IsNullOrWhiteSpace(secretSentinel)) {
			string.Join("\n", (execution.Output ?? []).Select(message => message.Value ?? string.Empty))
				.Should().NotContain(secretSentinel, because: "MCP command output must not expose database credentials");
			string.Join("\n", progress.Select(node => node.ToJsonString()))
				.Should().NotContain(secretSentinel, because: "MCP progress must not expose database credentials");
		}
	}

	private static bool HasWarning(IReadOnlyList<JsonNode> nodes, string stageId) {
		IReadOnlyList<ClioStageEvent> events = ExtractEvents(nodes);
		return events.Any(stageEvent => stageEvent.Stage?.StageId == stageId
				&& stageEvent.Stage.Status == ClioStageEventContract.StageStatuses.Warning)
			&& events.Any(stageEvent => stageEvent.RunCompleted?.Outcome ==
				ClioStageEventContract.RunOutcomes.SuccessWithWarnings);
	}

	private static IReadOnlyList<ClioStageEvent> ExtractEvents(IReadOnlyList<JsonNode> nodes) =>
		[.. nodes.Select(node => node["_meta"]?["clioStageEvent"]?.Deserialize<ClioStageEvent>(
				ClioStageEventContract.SerializerOptions))
			.Where(stageEvent => stageEvent is not null)
			.Cast<ClioStageEvent>()
			.OrderBy(stageEvent => stageEvent.Sequence)];
}
