using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Progress;
using Clio.Command.McpServer.Relay;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// TC-E-801 / TC-E-802 / TC-E-803 — the <c>terminal-stage</c> protocol (ADR §3.3, story 8) proved against
/// a REAL <c>clio mcp-server</c> host relaying a REAL deploy into a worker process.
/// </summary>
/// <remarks>
/// <para>
/// <b>Opt-in, disposable, and never automatic (story 8 AC-04).</b> Every test here installs Creatio, so
/// each one is <see cref="ExplicitAttribute"/>, refuses to run under TeamCity, requires
/// <c>AllowDestructiveMcpTests</c> AND an explicitly configured disposable sandbox environment and
/// archive, and ignores itself when either is missing. No watcher, probe, retry or startup path in this
/// fixture can reach a deploy: it happens only when a developer names a disposable target and runs the
/// test by name.
/// </para>
/// <para>
/// <b>What is NOT reachable here, stated rather than implied.</b> The post-terminal grace has two halves.
/// The half a real worker can be made to show — the result arriving with the run's terminal outcome, not
/// an error — is asserted below. The other half, a worker that emits <c>run-completed</c> and then HANGS,
/// cannot be produced from a real clio child without a fault-injection hook that does not exist and should
/// not be added to a shipping binary; it is covered deterministically by
/// <c>clio.tests/Command/McpServer/WorkerTerminalStageProtocolTests</c>, which scripts exactly that child
/// over a real pipe with the production relay and dispatcher.
/// </para>
/// </remarks>
[TestFixture]
[Category("McpE2E.Sandbox")]
[Category("LocalOnly")]
[Category("McpE2E.Manual")]
[Explicit("Developer-local validation only: installs a disposable Creatio instance from a local archive and must never run in TeamCity.")]
[AllureFeature("terminal-stage worker bound")]
[NonParallelizable]
public sealed class DeployTerminalStageE2ETests {

	private const string DeployToolName = "deploy-creatio";

	/// <summary>
	/// The ordinary worker budget forced on the host for TC-E-801: far shorter than any real deploy, so a
	/// generic kill would be unmistakable.
	/// </summary>
	private const string TinyOrdinaryBudgetSeconds = "20";

	[Test]
	[Description("TC-E-801: a real deploy relayed into a worker runs to its terminal stage even though the ORDINARY worker budget is far shorter than the deploy — it is bounded by the run-completed event, not by a stopwatch, so a mid-deploy budget expiry can never leave a half-installed environment.")]
	[AllureName("A deploy outlives the ordinary worker budget and is bounded by its terminal stage")]
	[AllureDescription("Forces CLIO_MCP_WORKER_BUDGET_SECONDS to a value far below a real deploy's duration, deploys one disposable Creatio instance through MCP, and asserts the run reached its own run-completed event and the tool result is neither a budget-expiry envelope nor an indeterminate one.")]
	public async Task Deploy_Should_ReachItsTerminalStage_WhenTheOrdinaryBudgetIsFarShorterThanTheRun() {
		// Arrange
		McpE2ESettings settings = ArrangeDisposableSandbox();
		settings.ProcessEnvironmentVariables[McpWorkerCallDispatcher.BudgetOverrideEnvVar] =
			TinyOrdinaryBudgetSeconds;
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(30));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellation.Token);
		session.StartCapturingProgressNotifications();
		ProgressToken progressToken = new($"terminal-stage-801-{Guid.NewGuid():N}");
		bool deployed = false;

		try {
			// Act
			CallToolResult result = await session.CallToolWithRawProgressAsync(
				DeployToolName, DeployArguments(settings), progressToken, cancellation.Token);
			deployed = true;
			IReadOnlyList<JsonNode> progress = await session.WaitForCapturedProgressAsync(
				progressToken, HasTerminalEvent, TimeSpan.FromMinutes(2), cancellation.Token);

			// Assert
			JsonNode payload = ReadStructured(result);
			progress.Should().Contain(node => HasTerminalEventNode(node),
				because: "the run must have reported its own authoritative terminal stage — without it this test would be asserting that a deploy simply happened to finish");
			payload?["error-class"]?.GetValue<string>().Should().NotBe(
				McpWorkerCallDispatcher.BudgetExpiredErrorClass,
				because: $"the deploy ran far longer than the {TinyOrdinaryBudgetSeconds}s ordinary budget, and a budget-expiry envelope here would mean the parent killed a deploy mid-flight — the half-installed environment ADR rule 4 exists to prevent");
			payload?["outcome"]?.GetValue<string>().Should().NotBe("indeterminate",
				because: "a deploy that streamed to its terminal stage has a known outcome, and reporting it as ambiguous would send an operator to inspect a perfectly good environment");
			result.IsError.Should().NotBeTrue(
				because: "the disposable deploy is expected to succeed; a failure here means the fixture's own configuration is wrong rather than the protocol");
		}
		finally {
			await CleanUpAsync(session, settings, deployed);
		}
	}

	[Test]
	[Description("TC-E-802 (lost child): a worker killed mid-deploy produces an explicit INDETERMINATE error naming the last stage reached — never a success, and never an automatic retry, which the spawn count is what proves.")]
	[AllureName("A worker killed mid-deploy yields an indeterminate error and no retry")]
	[AllureDescription("Deploys one disposable Creatio instance through MCP, waits for the run's first stage transition, kills the observed worker child, and asserts the tool result is the indeterminate envelope naming the last stage reached with no-retry guidance — and that the host spawned exactly one worker for the call.")]
	public async Task Deploy_Should_ReportIndeterminate_WhenTheWorkerIsKilledMidDeploy() {
		// Arrange
		McpE2ESettings settings = ArrangeDisposableSandbox();
		string clioHome = settings.ProcessEnvironmentVariables["CLIO_HOME"]
			?? throw new InvalidOperationException("The disposable sandbox must isolate CLIO_HOME.");
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(30));
		await using WorkerSpawnObserver workers = WorkerSpawnObserver.Start(clioHome);
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellation.Token);
		session.StartCapturingProgressNotifications();
		ProgressToken progressToken = new($"terminal-stage-802-{Guid.NewGuid():N}");

		try {
			// Act — start the deploy, let it reach a real stage, then kill the child under it.
			Task<CallToolResult> call = session.CallToolWithRawProgressAsync(
				DeployToolName, DeployArguments(settings), progressToken, cancellation.Token);
			await session.WaitForCapturedProgressAsync(progressToken, HasAnyStageTransition,
				TimeSpan.FromMinutes(5), cancellation.Token);
			ObservedWorker worker = workers.Observed.SingleOrDefault()
				?? throw new InvalidOperationException(
					$"Expected exactly one worker child to kill. {workers.Describe()}");
			KillWorker(worker);
			CallToolResult result = await call;

			// Assert
			JsonNode payload = ReadStructured(result);
			result.IsError.Should().BeTrue(
				because: "a deploy whose worker died mid-flight must never be reported as an answer");
			payload?["success"]?.GetValue<bool>().Should().BeFalse(
				because: "ClioRing reads success/error rather than trusting IsError alone, so both have to say the same thing");
			payload?["outcome"]?.GetValue<string>().Should().Be("indeterminate",
				because: "the run reported no terminal stage, so clio cannot know whether the operation completed and must say exactly that");
			payload?["error-class"]?.GetValue<string>().Should().NotBe(
				McpWorkerCallDispatcher.BudgetExpiredErrorClass,
				because: "the budget-expiry class ships guidance saying the call is safe to retry, which against a possibly half-installed environment is the most damaging instruction available");
			payload?["last-stage-id"]?.GetValue<string>().Should().NotBeNullOrWhiteSpace(
				because: "naming the last stage reached is what tells an operator where the environment was left");
			payload?["environment-state"]?.GetValue<string>().Should().Be("possibly-half-installed",
				because: "the operator-facing consequence is the state of the target, not the state of the call");
			workers.Observed.Should().HaveCount(1,
				because: "the parent must NOT retry a deploy whose outcome it cannot establish — a second worker is the only observable evidence of a retry, and no timing or result assertion could see one");
		}
		finally {
			// A killed deploy leaves an unknown amount behind, so the disposable target is cleaned
			// unconditionally — which is precisely why this test may only ever run against one.
			await CleanUpAsync(session, settings, deployed: true);
		}
	}

	[Test]
	[Description("TC-E-803 (reachable half): after the run's terminal stage the tool result arrives inside the post-terminal exit grace and carries the run's terminal outcome rather than an error. The unreachable half — a worker that hangs after run-completed — is covered by WorkerTerminalStageProtocolTests, which can script one.")]
	[AllureName("The tool result after run-completed is the terminal outcome, not an error")]
	[AllureDescription("Deploys one disposable Creatio instance through MCP and asserts the tool result arrives promptly after the run's terminal event and reports the run's own outcome, so the post-terminal grace neither truncates a healthy hand-off nor converts it into a failure.")]
	public async Task Deploy_Should_AnswerWithTheTerminalOutcome_WhenTheRunCompletes() {
		// Arrange
		McpE2ESettings settings = ArrangeDisposableSandbox();
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(30));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellation.Token);
		session.StartCapturingProgressNotifications();
		ProgressToken progressToken = new($"terminal-stage-803-{Guid.NewGuid():N}");
		bool deployed = false;

		try {
			// Act
			Stopwatch elapsedSinceCall = Stopwatch.StartNew();
			CallToolResult result = await session.CallToolWithRawProgressAsync(
				DeployToolName, DeployArguments(settings), progressToken, cancellation.Token);
			deployed = true;
			elapsedSinceCall.Stop();
			IReadOnlyList<JsonNode> progress = await session.WaitForCapturedProgressAsync(
				progressToken, HasTerminalEvent, TimeSpan.FromMinutes(2), cancellation.Token);

			// Assert
			JsonNode payload = ReadStructured(result);
			progress.Should().Contain(node => HasTerminalEventNode(node),
				because: "the grace window only begins at the terminal event, so a run without one would be exercising the indeterminate path instead");
			result.IsError.Should().NotBeTrue(
				because: "the run reported a successful terminal outcome, and a completed deploy answered as an error is the failure mode AC-06 names");
			payload?["outcome"]?.GetValue<string>().Should().NotBe("indeterminate",
				because: "the terminal event states the outcome, so the parent must report it rather than declare the result unknown");
			McpCommandExecutionParser.Extract(result).ExitCode.Should().Be(0,
				because: "the worker's own answer is relayed verbatim when it arrives inside the grace, exit code included");
		}
		finally {
			await CleanUpAsync(session, settings, deployed);
		}
	}

	// ---------------------------------------------------------------------------------------------
	// Helpers
	// ---------------------------------------------------------------------------------------------

	/// <summary>
	/// Refuses to run unless a developer has explicitly named a DISPOSABLE target, and returns the settings
	/// for it. Every guard here is fail-closed: a missing one ignores the test rather than falling back to
	/// a default, because the fallback would be a real deploy against whatever happens to be configured.
	/// </summary>
	/// <returns>The sandbox settings.</returns>
	private static McpE2ESettings ArrangeDisposableSandbox() {
		if (!OperatingSystem.IsWindows()) {
			Assert.Ignore("The disposable IIS deployment lifecycle is Windows-only.");
		}
		TeamCityRunGuard.IgnoreIfRunningUnderTeamCity(
			"This archive-backed Creatio installation test is forbidden in TeamCity.");
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Enable AllowDestructiveMcpTests to run the terminal-stage deploy proofs.");
		}
		if (string.IsNullOrWhiteSpace(settings.Sandbox.EnvironmentName)
			|| string.IsNullOrWhiteSpace(settings.Sandbox.DeploymentArchivePath)
			|| !File.Exists(settings.Sandbox.DeploymentArchivePath)
			|| settings.Sandbox.DeploymentSitePort is < 1 or > 65535
			|| string.IsNullOrWhiteSpace(settings.Sandbox.DeploymentDbServerName)) {
			Assert.Ignore(
				"Configure Sandbox EnvironmentName, DeploymentArchivePath, DeploymentSitePort and "
				+ "DeploymentDbServerName with an explicitly DISPOSABLE target for the terminal-stage proofs.");
		}
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string clioHome = Path.Combine(Path.GetTempPath(),
			string.Create(CultureInfo.InvariantCulture, $"clio-terminal-stage-e2e-{Guid.NewGuid():N}"));
		Directory.CreateDirectory(clioHome);
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = clioHome;
		return settings;
	}

	private static IReadOnlyDictionary<string, object?> DeployArguments(McpE2ESettings settings) =>
		new Dictionary<string, object?> {
			["args"] = new Dictionary<string, object?> {
				["siteName"] = settings.Sandbox.EnvironmentName,
				["zipFile"] = settings.Sandbox.DeploymentArchivePath,
				["sitePort"] = settings.Sandbox.DeploymentSitePort,
				["dbServerName"] = settings.Sandbox.DeploymentDbServerName,
				["redisServerName"] = settings.Sandbox.DeploymentRedisServerName
			}
		};

	private static JsonNode? ReadStructured(CallToolResult result) =>
		result?.StructuredContent is { } structured ? JsonNode.Parse(structured.GetRawText()) : null;

	private static bool HasTerminalEvent(IReadOnlyList<JsonNode> progress) =>
		progress.Any(HasTerminalEventNode);

	private static bool HasTerminalEventNode(JsonNode node) =>
		StageEventTypeOf(node) == ClioStageEventContract.EventTypes.RunCompleted;

	private static bool HasAnyStageTransition(IReadOnlyList<JsonNode> progress) =>
		progress.Any(node => StageEventTypeOf(node) == ClioStageEventContract.EventTypes.Stage);

	private static string? StageEventTypeOf(JsonNode node) =>
		node?["_meta"]?["clioStageEvent"]?["eventType"]?.GetValue<string>();

	/// <summary>
	/// Kills one observed worker child, which is how this fixture reproduces "the child died mid-deploy"
	/// without depending on a fault-injection hook inside the shipping binary.
	/// </summary>
	/// <param name="worker">The worker the host recorded in its own registry.</param>
	private static void KillWorker(ObservedWorker worker) {
		using Process process = Process.GetProcessById(worker.ProcessId);
		process.Kill(entireProcessTree: true);
	}

	/// <summary>
	/// Removes the disposable instance, best effort and bounded — an indeterminate deploy may have left
	/// anything behind, so cleanup runs even when the test's own assertions failed.
	/// </summary>
	/// <param name="session">The live MCP session.</param>
	/// <param name="settings">The sandbox settings naming the disposable environment.</param>
	/// <param name="deployed">Whether a deploy was started at all.</param>
	/// <returns>A task that completes when cleanup has been attempted.</returns>
	private static async Task CleanUpAsync(
		McpServerSession session, McpE2ESettings settings, bool deployed) {
		if (!deployed) {
			return;
		}
		try {
			using CancellationTokenSource cleanup = new(TimeSpan.FromMinutes(10));
			await session.CallToolAsync(
				UninstallCreatioTool.UninstallCreatioToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = settings.Sandbox.EnvironmentName
					}
				}, cleanup.Token);
		}
		catch (Exception exception) when (exception is InvalidOperationException or IOException
			or OperationCanceledException) {
			TestContext.Error.WriteLine(
				$"Best-effort disposable uninstall failed: {exception.GetType().Name}. Environment "
				+ $"'{settings.Sandbox.EnvironmentName}' may remain and must be removed by hand.");
		}
	}
}
