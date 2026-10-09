using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Relay;
using Clio.Command.McpServer.Tools;
using Clio.Common.McpWorker;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// ENG-102333: an MCP client that stops waiting for <c>compile-creatio</c> before clio's own in-progress answer
/// must not lose the compile it started.
/// </summary>
/// <remarks>
/// <para>
/// The client's own request timeout is reproduced by its wire sequence: the <c>tools/call</c> request, then
/// <c>notifications/cancelled</c> naming that request's id, sent to the real <c>clio mcp-server</c> (see
/// <see cref="CallCompileAndGiveUpAsync"/> for why the SDK client's own cancellation is not used). That parent
/// relays the call to a sticky worker, and the worker is the only process holding the compile's record — so the
/// question every assertion below asks is whether the parent kept that worker.
/// </para>
/// <para>
/// <b>No stand is needed, and none would be deterministic.</b> The compile is held open by the wedge stub's
/// LOGIN delay: the worker records the operation, hands the work to the detached heartbeat and then blocks on
/// its first request to the "environment", which is the stub's login. The caller cancels only once that login
/// has ARRIVED at the stub, so the request had certainly reached the worker — cancelling earlier would be the
/// handshake case, where releasing the worker is correct. Once the login answers, the process compile asks
/// the stub for installed packages, finds no CrtProcessBuilder and fails — a real terminal state that
/// compile-status must then report with its exit code and message tail.
/// </para>
/// </remarks>
[TestFixture]
[AllureNUnit]
[AllureFeature("compile-configuration")]
public sealed class CompileCreatioClientTimeoutE2ETests {

	private const string EnvironmentName = "tc102333-stub";
	private const string ProcessName = "UsrTc102333Probe";

	/// <summary>
	/// How long the stub holds the worker's login, and with it the compile: far longer than the client's
	/// "timeout" below and than the status poll that follows it, shorter than clio's response deadline (45 s by
	/// default, and the 150 s the suite pins in <see cref="TestConfiguration.Load"/>).
	/// </summary>
	private static readonly TimeSpan CompileHold = TimeSpan.FromSeconds(30);

	/// <summary>
	/// The per-call ceiling of the client the QA run used: Claude Code desktop 2.1.293 gave up on a tool call after
	/// 60 s and then restarted the MCP server, which ends every operation the server tracked.
	/// </summary>
	private static readonly TimeSpan ClientCeiling = TimeSpan.FromSeconds(60);

	/// <summary>How long the whole scenario may take before the run is abandoned.</summary>
	private static readonly TimeSpan ScenarioBudget = TimeSpan.FromMinutes(5);

	/// <summary>How long the compile may take to reach a terminal state once its login has answered.</summary>
	private static readonly TimeSpan TerminalStateBound = TimeSpan.FromMinutes(2);

	[Test]
	[Category("McpE2E.NoEnvironment")]
	[AllureTag(CompileCreatioTool.CompileCreatioToolName)]
	[AllureTag(CompileStatusTool.CompileStatusToolName)]
	[AllureName("A client that stops waiting for compile-creatio does not lose the compile")]
	[AllureDescription("Starts the real clio MCP server against a Creatio stub that holds the compile's login open, calls compile-creatio with process-name and cancels the call once the request has reached the worker - what an MCP client's own request timeout does. Then: compile-status must answer running (not not-found), a second compile-creatio must be refused as already in progress, and once the held compile ends compile-status must report its final status with the exit code and the message tail.")]
	[Description("ENG-102333 (the Jira issue's TC-01 and TC-03): after the client cancels compile-creatio, compile-status answers running and then the final status with exit code and message tail, and a second compile-creatio for the target is refused as already in progress.")]
	public async Task CompileStatus_Should_ReportTheCompile_WhenTheClientStoppedWaitingForCompileCreatio() {
		// Arrange
		await using CreatioWedgeStubServer stub = CreatioWedgeStubServer.Start();
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-e2e-102333-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		try {
			McpE2ESettings settings = TestConfiguration.Load();
			settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			settings.ProcessEnvironmentVariables[OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME"] = tempHome;
			// Overrides the assembly-shared CLIO_HOME so the settings replacement below stays in this fixture's own
			// home instead of rewriting the file every other fixture reads.
			settings.ProcessEnvironmentVariables["CLIO_HOME"] = tempHome;
			// IsNetCore=false pins the .NET Framework routes the stub matches by substring; registering inline
			// keeps reg-web-app's runtime detection - and its logins - out of the counter this test waits on.
			using TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
				StubSettings(stub), settings.ClioProcessPath, settings.ProcessEnvironmentVariables);
			settingsOverride.AppSettingsPath.Should().StartWith(tempHome,
				because: "the replaced settings file must live in this fixture's own clio home");
			using CancellationTokenSource scenario = new(ScenarioBudget);
			await using McpServerSession session = await McpServerSession.StartAsync(settings, scenario.Token);
			stub.ResetCounters();
			stub.SetLoginDelay(CompileHold);

			// Act
			Exception abandonedCall = await AllureApi.Step(
				"The client stops waiting for compile-creatio after the request reached the worker",
				async () => await CallCompileAndGiveUpAsync(session, stub, scenario.Token));
			CompileStatusResponse whileRunning = await AllureApi.Step("compile-status right after the client gave up",
				async () => await GetStatusAsync(session, scenario.Token));
			CallToolResult secondCompile = await AllureApi.Step("a second compile-creatio while the first one runs",
				async () => await session.CallToolAsync(CompileCreatioTool.CompileCreatioToolName,
					CompileArguments(), scenario.Token));
			CompileStatusResponse finished = await AllureApi.Step("compile-status once the held compile has ended",
				async () => await WaitForTerminalStatusAsync(session, scenario.Token));

			// Assert
			abandonedCall.Should().BeAssignableTo<OperationCanceledException>(
				because: "the arrangement is a client that gave up: it told clio with notifications/cancelled and stopped waiting, so its own call ends as cancelled and is never answered");
			whileRunning.Status.Should().Be("running",
				because: "the compile was recorded in the sticky worker before the client gave up; a not-found here means the worker holding that record was killed - the reported defect");
			whileRunning.ProcessName.Should().Be(ProcessName,
				because: "the poll must describe the compile the abandoned call started");
			string.IsNullOrWhiteSpace(whileRunning.OperationId).Should().BeFalse(
				because: "a tracked operation carries its operation-id");
			secondCompile.IsError.Should().BeTrue(
				because: "the first compile still runs on the target, so starting another one must be refused: {0}",
				DescribeResult(secondCompile));
			// A literal, not the dispatcher's constant: the error class is a token agents and guidance key on, so
			// a rename must fail here rather than compare equal to itself.
			ReadStructuredString(secondCompile, "error-class").Should().Be("clio-configuration-build-in-progress",
				because: "the refusal must say the configuration build is already in progress, which tells the agent to poll rather than retry");
			finished.Status.Should().BeOneOf(new[] { "failed", "succeeded" },
				because: "compile-status must report the compile's final status once it ends, not lose it");
			finished.OperationId.Should().Be(whileRunning.OperationId,
				because: "the final status must belong to the same compile the earlier poll reported as running");
			finished.ExitCode.Should().NotBeNull(because: "a finished compile reports its exit code");
			finished.MessageTail.Should().NotBeNullOrEmpty(
				because: "a finished compile reports the tail of its output, which is where compiler errors would be");
			stub.UnexpectedHandlerFailures.Should().BeEmpty(
				because: "a broken stub would make every assertion above measure the instrument instead of clio");
		}
		finally {
			TryDeleteDirectory(tempHome);
		}
	}

	[Test]
	[Category("McpE2E.NoEnvironment")]
	[AllureTag(CompileCreatioTool.CompileCreatioToolName)]
	[AllureName("A worker signals completion for a compile call cancelled right behind its request")]
	[AllureDescription("Talks to a real clio mcp-server --worker directly, the way the parent does, and writes notifications/cancelled immediately behind the compile-creatio request - the tightest race a parent can produce. The parent keeps such a worker and relies on it to send the private completion signal when the work ends; if the MCP SDK refused a request already cancelled before clio's call-tool filter ran, no signal would ever come and the kept worker would hold the target's configuration-build reservation until its 65-minute lifetime bound.")]
	[Description("ENG-102333: a worker whose compile-creatio call is cancelled right behind its request still sends the private completion signal - at once when the cancellation won, when the compile ends when it did not - so a parent that keeps the worker is always released. An uncancelled control call on the same worker proves the setup reaches the environment.")]
	public async Task Worker_Should_SendTheCompletionSignal_WhenItsCompileCallIsCancelledRightBehindTheRequest() {
		// Arrange
		await using CreatioWedgeStubServer stub = CreatioWedgeStubServer.Start();
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-e2e-102333w-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		try {
			McpE2ESettings settings = TestConfiguration.Load();
			settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			settings.ProcessEnvironmentVariables[OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME"] = tempHome;
			settings.ProcessEnvironmentVariables["CLIO_HOME"] = tempHome;
			using TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
				StubSettings(stub), settings.ClioProcessPath, settings.ProcessEnvironmentVariables);
			settingsOverride.AppSettingsPath.Should().StartWith(tempHome,
				because: "the replaced settings file must live in this fixture's own clio home");
			using CancellationTokenSource scenario = new(ScenarioBudget);
			await using DirectWorker worker = await DirectWorker.StartAsync(settings, tempHome, scenario.Token);
			TaskCompletionSource<JsonRpcNotification> signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
			await using IAsyncDisposable signalHandler = worker.Client.RegisterNotificationHandler(
				WorkerOperationSignalContract.NotificationMethod, (notification, _) => {
					signal.TrySetResult(notification);
					return default;
				});
			stub.ResetCounters();
			stub.SetLoginDelay(TimeSpan.FromSeconds(5));
			RequestId requestId = new($"eng-102333-worker-{Guid.NewGuid():N}");

			// Act - the request and its cancellation leave back to back, from one thread. Which of the two the
			// worker acts on first is a race, and both outcomes are correct: cancelled before the tool started,
			// the call ends with nothing running and signals at once (measured: no login reached the stub);
			// started first, the compile runs detached and signals when it ends. What may never happen is no
			// signal at all.
			using CancellationTokenSource local = CancellationTokenSource.CreateLinkedTokenSource(scenario.Token);
			Task<JsonRpcResponse> call = worker.Client.SendRequestAsync(CompileRequest(requestId), local.Token);
			await worker.Client.SendNotificationAsync(NotificationMethods.CancelledNotification,
				new CancelledNotificationParams { RequestId = requestId, Reason = "Request timed out" },
				cancellationToken: scenario.Token);
			await local.CancelAsync();
			_ = call.ContinueWith(static abandoned => abandoned.Exception, CancellationToken.None,
				TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
			Task finished = await Task.WhenAny(signal.Task, Task.Delay(TerminalStateBound, scenario.Token));
			int loginsWhenSignalled = stub.LoginCount;
			// Control: the same worker and setup, NOT cancelled, must reach the environment. Without it a fast signal
			// proves nothing - a call that fails at once for any other reason (a broken child environment, say)
			// sends exactly the same signal. Whichever branch the race above took, its call has ended by now, so
			// nothing in the worker refuses this one.
			using CancellationTokenSource control = CancellationTokenSource.CreateLinkedTokenSource(scenario.Token);
			control.CancelAfter(TerminalStateBound);
			await worker.Client.SendRequestAsync(CompileRequest(new RequestId($"eng-102333-control-{Guid.NewGuid():N}")),
				control.Token);

			// Assert
			finished.Should().BeSameAs(signal.Task,
				because: "the worker must send its completion signal for a call cancelled right behind its request; without it a parent that kept the worker holds the target's reservation for the whole lifetime bound. Stub: {0}. Worker: {1}",
				stub.DescribeState(), worker.Describe());
			WorkerOperationSignalContract.TryRead(await signal.Task, out McpToolOperationFamily family, out int? _)
				.Should().BeTrue(because: "the signal must be the parent's private completion contract");
			family.Should().Be(McpToolOperationFamily.ConfigurationBuild,
				because: "the signal belongs to the compile's operation family");
			stub.LoginCount.Should().BeGreaterThan(loginsWhenSignalled,
				because: "the uncancelled control call must reach the environment, or the signal above could have come from a setup that cannot compile at all. Logins when signalled: {0}. Worker: {1}",
				loginsWhenSignalled, worker.Describe());
		}
		finally {
			TryDeleteDirectory(tempHome);
		}
	}

	[Test]
	[Category("McpE2E.NoEnvironment")]
	[AllureTag(CompileCreatioTool.CompileCreatioToolName)]
	[AllureTag(CompileStatusTool.CompileStatusToolName)]
	[AllureName("compile-creatio answers before a 60-second client ceiling at the default deadline")]
	[AllureDescription("Starts the real clio MCP server with no response-deadline override against a Creatio stub that holds the compile's login for longer than 60 s, and calls compile-creatio with process-name. The answer must be the in-progress note with an operation-id, and it must arrive before 60 s - the per-call ceiling after which Claude Code desktop gives up and restarts the MCP server. compile-status must then report that operation running.")]
	[Description("ENG-102333 (QA on Claude Code desktop 2.1.293): at the default response deadline, compile-creatio answers a compile that outlasts 60 s with its in-progress note and operation-id before 60 s have passed, and compile-status then reports that operation running.")]
	public async Task CompileCreatio_Should_AnswerInProgressBeforeSixtySeconds_WhenTheDefaultDeadlineApplies() {
		// Arrange
		await using CreatioWedgeStubServer stub = CreatioWedgeStubServer.Start();
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-e2e-102333d-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		try {
			McpE2ESettings settings = IsolatedSettings(tempHome);
			// Clears the suite's 150 s pin and whatever the runner's own environment carries. An empty value is
			// "not set" to clio (McpProgressHeartbeat.ResolveResponseDeadline), so the built-in default applies, and
			// the parent hands a sticky worker nothing for it either.
			settings.ProcessEnvironmentVariables[McpProgressHeartbeat.ResponseDeadlineOverrideEnvVar] = string.Empty;
			using TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
				StubSettings(stub), settings.ClioProcessPath, settings.ProcessEnvironmentVariables);
			using CancellationTokenSource scenario = new(ScenarioBudget);
			await using McpServerSession session = await McpServerSession.StartAsync(settings, scenario.Token);
			stub.ResetCounters();
			stub.SetLoginDelay(ClientCeiling + TimeSpan.FromSeconds(30));

			// Act
			Stopwatch waited = Stopwatch.StartNew();
			CallToolResult answered = await AllureApi.Step("compile-creatio with a compile that outlasts 60 s",
				async () => await session.CallToolAsync(CompileCreatioTool.CompileCreatioToolName, CompileArguments(),
					scenario.Token));
			waited.Stop();
			CompileStatusResponse status = await AllureApi.Step("compile-status for that compile",
				async () => await GetStatusAsync(session, scenario.Token));

			// Assert
			string answer = DescribeResult(answered);
			waited.Elapsed.Should().BeLessThan(ClientCeiling,
				because: "a client that gives up at 60 s restarts the MCP server, and the restart takes the operation's record with it; answered after {0}: {1}",
				waited.Elapsed, answer);
			waited.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(30),
				because: "the stub holds the compile far past the deadline, so an earlier answer is not the deadline's and the timing above would prove nothing: {0}",
				answer);
			answered.IsError.Should().NotBe(true, because: "the in-progress note is not a failure: {0}", answer);
			Match operation = InProgressOperationId.Match(answer);
			operation.Success.Should().BeTrue(
				because: "the in-progress note must carry the operation-id compile-status is polled with: {0}", answer);
			status.Status.Should().Be("running", because: "the compile is still held by the stub");
			status.OperationId.Should().Be(operation.Groups[1].Value,
				because: "compile-status must describe the operation the in-progress note named");
			stub.UnexpectedHandlerFailures.Should().BeEmpty(
				because: "a broken stub would make every assertion above measure the instrument instead of clio");
		}
		finally {
			TryDeleteDirectory(tempHome);
		}
	}

	[Test]
	[Category("McpE2E.NoEnvironment")]
	[AllureTag(CompileStatusTool.CompileStatusToolName)]
	[AllureName("compile-status lists the environment's compilation history when the server holds no record")]
	[AllureDescription("Starts a fresh clio MCP server - what a client that restarts its server after giving up is left with - against a Creatio stub that serves CompilationHistory through DataService in a UTC+3 session and reports that offset through GetApplicationInfo, and calls compile-status. The not-found answer must list the stub's rows with their finish times converted to UTC and with fenced errors. With the stub serving no history, the answer must say the history could not be read and fall back to last-compilation-log.")]
	[Description("ENG-102333 (QA): when the MCP server holds no record, compile-status reads the environment's compilation history and lists its rows with UTC finish times - converted from the session's zone with the session's offset - and fenced errors; when the history cannot be read it says so in clio's own words and falls back to last-compilation-log.")]
	public async Task CompileStatus_Should_ListTheCompilationHistory_WhenTheServerHoldsNoRecord() {
		// Arrange
		await using CreatioWedgeStubServer stub = CreatioWedgeStubServer.Start();
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-e2e-102333h-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		try {
			McpE2ESettings settings = IsolatedSettings(tempHome);
			using TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
				StubSettings(stub), settings.ClioProcessPath, settings.ProcessEnvironmentVariables);
			using CancellationTokenSource scenario = new(ScenarioBudget);
			await using McpServerSession session = await McpServerSession.StartAsync(settings, scenario.Token);
			DateTime failedAt = DateTime.UtcNow.AddSeconds(-90);
			stub.SetCompilationHistory(CompilationHistoryRows(failedAt, SessionOffset), (int)SessionOffset.TotalMinutes);

			// Act
			CompileStatusResponse withHistory = await AllureApi.Step("compile-status while the history answers",
				async () => await GetStatusAsync(session, scenario.Token));
			stub.SetCompilationHistory(null);
			CompileStatusResponse withoutHistory = await AllureApi.Step("compile-status while it does not",
				async () => await GetStatusAsync(session, scenario.Token));

			// Assert
			withHistory.Status.Should().Be("not-found", because: "a fresh server holds no compile record");
			withHistory.CompilationHistoryError.Should().BeNull(because: "the history answered: {0}", withHistory.Note);
			withHistory.CompilationHistory.Should().HaveCount(2, because: "the stub serves two rows");
			withHistory.OlderFailures.Should().BeEmpty(because: "no row is older than the listed ones");
			CompileHistoryEntry failed = withHistory.CompilationHistory![0];
			failed.FinishedUtc.Should().BeCloseTo(failedAt, TimeSpan.FromMilliseconds(1),
				because: "DataService wrote the time in the session's UTC+3 zone, and the session's own offset must turn it back into UTC");
			failed.FinishedSecondsAgo.Should().BeInRange(60, 600,
				because: "the row was written about 90 s before compile-status read it");
			failed.Succeeded.Should().BeFalse(because: "the newest row is a failed build");
			failed.ErrorCount.Should().Be(1, because: "the row carries one error and one warning");
			failed.Errors.Should().ContainSingle(because: "warnings are not errors")
				.Which.Should().Contain("CS0103").And.Contain("UsrProc.cs").And.NotContain("inetpub")
				.And.StartWith("[untrusted-source-text begin]",
					because: "the compiler's text is fenced as text the environment authored, with the file cut to its name");
			withHistory.CheckedUtc.Should().NotBeNull(because: "the ages are counted back from it");
			withoutHistory.Status.Should().Be("not-found", because: "the server still holds no record");
			withoutHistory.CompilationHistory.Should().BeNull(because: "the stub served no history and no session offset");
			withoutHistory.CompilationHistoryError.Should().Be(CompileStatusTool.HistoryUnreadableError,
				because: "an answer without history says, in clio's own words, that it could not read it");
			withoutHistory.Note.Should().Contain(LastCompilationLogTool.ToolName,
				because: "without the history, last-compilation-log is the fallback");
			stub.CompilationHistoryReadCount.Should().Be(1,
				because: "the rows were read once, while the stub served them; without a session offset the read stops before asking for rows");
			stub.UnexpectedHandlerFailures.Should().BeEmpty(
				because: "a broken stub would make every assertion above measure the instrument instead of clio");
		}
		finally {
			TryDeleteDirectory(tempHome);
		}
	}

	// The in-progress notice names the operation as "(operation-id '<id>')"; System.Text.Json writes the
	// apostrophes as \u0027, so both spellings are accepted.
	private static readonly Regex InProgressOperationId = new(@"operation-id (?:'|\\u0027)([0-9a-fA-F-]+)(?:'|\\u0027)",
		RegexOptions.CultureInvariant);

	private static McpE2ESettings IsolatedSettings(string tempHome) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		settings.ProcessEnvironmentVariables[OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME"] = tempHome;
		// Overrides the assembly-shared CLIO_HOME so the settings replacement stays in this fixture's own home.
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = tempHome;
		return settings;
	}

	/// <summary>The session zone the stub's history is written in: UTC+3, as on the stands the QA ran on.</summary>
	private static readonly TimeSpan SessionOffset = TimeSpan.FromHours(3);

	// DataService writes CreatedOn in the session's zone, with no offset: this is the shape clio has to convert.
	private static string SessionTime(DateTime utc, TimeSpan sessionOffset) =>
		(utc + sessionOffset).ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);

	private static JsonArray CompilationHistoryRows(DateTime failedAt, TimeSpan sessionOffset) =>
		new(
			new JsonObject {
				["CreatedOn"] = SessionTime(failedAt, sessionOffset),
				["ProjectName"] = "Terrasoft.Configuration.Dev.csproj",
				["Result"] = false,
				["DurationInSeconds"] = 141,
				["ErrorsWarnings"] = """
					[{"errorNumber":"CS0103","errorText":"The name 'qaUndefinedVar' does not exist in the current context","fileName":"C:\\inetpub\\Creatio\\UsrProc.cs","line":34,"column":40,"isWarning":false},{"errorNumber":"CS0114","errorText":"hides an inherited member","isWarning":true}]
					"""
			},
			new JsonObject {
				["CreatedOn"] = SessionTime(failedAt.AddHours(-3), sessionOffset),
				["ProjectName"] = "Terrasoft.Configuration.Dev.csproj",
				["Result"] = true,
				["DurationInSeconds"] = 90,
				["ErrorsWarnings"] = "[]"
			});

	private static string StubSettings(CreatioWedgeStubServer stub) =>
		$$"""
		{
		  "ActiveEnvironmentKey": "{{EnvironmentName}}",
		  "Environments": {
		    "{{EnvironmentName}}": {
		      "Uri": "{{stub.BaseUrl}}",
		      "Login": "Supervisor",
		      "Password": "Supervisor",
		      "IsNetCore": false
		    }
		  }
		}
		""";

	private static JsonRpcRequest CompileRequest(RequestId requestId) =>
		new() {
			Id = requestId,
			Method = RequestMethods.ToolsCall,
			Params = JsonSerializer.SerializeToNode(new CallToolRequestParams {
				Name = ClioRunTool.ToolName,
				Arguments = new Dictionary<string, JsonElement> {
					["command"] = JsonSerializer.SerializeToElement(CompileCreatioTool.CompileCreatioToolName),
					["args"] = JsonSerializer.SerializeToElement(new Dictionary<string, string> {
						["environment-name"] = EnvironmentName,
						["process-name"] = ProcessName
					})
				}
			}, McpJsonUtilities.DefaultOptions)
		};

	private static Dictionary<string, object?> CompileArguments() =>
		new() {
			["args"] = new Dictionary<string, object?> {
				["environment-name"] = EnvironmentName,
				["process-name"] = ProcessName
			}
		};

	/// <summary>
	/// Calls compile-creatio and gives up on it once the worker's login has reached the stub - the moment the
	/// compile is recorded and running inside the worker.
	/// </summary>
	/// <remarks>
	/// <b>The wire sequence is written out rather than left to the SDK client's own cancellation.</b> What a
	/// client that times out sends is the request and then <c>notifications/cancelled</c> carrying that
	/// request's id - measured with Claude Code against the real server on a stand, where the worker died at
	/// the client's 60-second timeout. Cancelling the SDK client's call alone did NOT reach the parent in this
	/// harness: the unfixed server then kept its worker and this test passed for the wrong reason. So the
	/// request goes out under an id chosen here, and the cancellation names that id explicitly.
	/// </remarks>
	private static async Task<Exception> CallCompileAndGiveUpAsync(McpServerSession session,
		CreatioWedgeStubServer stub, CancellationToken scenario) {
		RequestId requestId = new($"eng-102333-{Guid.NewGuid():N}");
		using CancellationTokenSource client = CancellationTokenSource.CreateLinkedTokenSource(scenario);
		Task<JsonRpcResponse> call = session.Client.SendRequestAsync(CompileRequest(requestId), client.Token);
		Stopwatch waited = Stopwatch.StartNew();
		while (stub.LoginCount == 0 && !call.IsCompleted && waited.Elapsed < CompileHold) {
			await Task.Delay(100, scenario);
		}
		stub.LoginCount.Should().BeGreaterThan(0,
			because: "the worker must have started the compile's first request before the client gives up; cancelling earlier is the handshake case, where releasing the worker is correct. Stub state: {0}",
			stub.DescribeState());
		await session.Client.SendNotificationAsync(NotificationMethods.CancelledNotification,
			new CancelledNotificationParams { RequestId = requestId, Reason = "Request timed out" },
			cancellationToken: scenario);
		// Releases the local awaiter: a cancelled request is never answered.
		await client.CancelAsync();
		try {
			JsonRpcResponse unexpected = await call;
			return new InvalidOperationException(
				$"compile-creatio answered instead of being cancelled: {unexpected.Result?.ToJsonString()}");
		}
		catch (Exception exception) {
			return exception;
		}
	}

	private static async Task<CompileStatusResponse> GetStatusAsync(McpServerSession session,
		CancellationToken cancellationToken) {
		CallToolResult result = await session.CallToolAsync(CompileStatusTool.CompileStatusToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["environment-name"] = EnvironmentName }
			},
			cancellationToken);
		return EntitySchemaStructuredResultParser.Extract<CompileStatusResponse>(result);
	}

	private static async Task<CompileStatusResponse> WaitForTerminalStatusAsync(McpServerSession session,
		CancellationToken cancellationToken) {
		Stopwatch waited = Stopwatch.StartNew();
		CompileStatusResponse status = await GetStatusAsync(session, cancellationToken);
		while (status.Status == "running" && waited.Elapsed < TerminalStateBound) {
			await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
			status = await GetStatusAsync(session, cancellationToken);
		}
		return status;
	}

	private static string? ReadStructuredString(CallToolResult result, string propertyName) =>
		result.StructuredContent is { ValueKind: JsonValueKind.Object } structured
			&& structured.TryGetProperty(propertyName, out JsonElement value)
			&& value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;

	private static string DescribeResult(CallToolResult result) =>
		string.Join(" | ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

	private static void TryDeleteDirectory(string path) {
		try {
			if (Directory.Exists(path)) {
				Directory.Delete(path, recursive: true);
			}
		}
		catch (IOException) {
			// Best-effort cleanup of the isolated home; a leaked temp directory is not what this test proves.
		}
		catch (UnauthorizedAccessException) {
			// Same reasoning.
		}
	}

	/// <summary>
	/// One real <c>clio mcp-server --worker</c> child, started and spoken to the way the parent's supervisor
	/// and relay do: a cleared environment carrying the supervisor's allowlist and the sticky composition, and
	/// MCP over the child's own streams.
	/// </summary>
	/// <remarks>
	/// A smaller copy of <c>McpWorkerModeE2ETests.WorkerProcess</c>, which is private to that fixture; this
	/// one only needs to start the child, drain its standard error and expose the client.
	/// </remarks>
	private sealed class DirectWorker : IAsyncDisposable {

		private readonly Process _process;
		private readonly System.Text.StringBuilder _standardError = new();
		private Task? _standardErrorPump;

		private DirectWorker(Process process) => _process = process;

		public McpClient Client { get; private set; } = null!;

		public static async Task<DirectWorker> StartAsync(McpE2ESettings settings, string home,
			CancellationToken cancellationToken) {
			ClioProcessDescriptor descriptor =
				ClioExecutableResolver.Resolve(settings, "mcp-server", McpWorkerEnvironment.WorkerFlag);
			ProcessStartInfo startInfo = new() {
				FileName = descriptor.Command,
				WorkingDirectory = descriptor.WorkingDirectory,
				UseShellExecute = false,
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			foreach (string argument in descriptor.Arguments) {
				startInfo.ArgumentList.Add(argument);
			}
			startInfo.Environment.Clear();
			foreach (string name in WorkerProcessSupervisor.DefaultInheritedEnvironmentVariableAllowlist) {
				string? value = Environment.GetEnvironmentVariable(name);
				if (value is not null) {
					startInfo.Environment[name] = value;
				}
			}
			IReadOnlyDictionary<string, string> composed = McpWorkerEnvironment.ComposeChildEnvironment(
				new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase), McpWorkerLifetime.Sticky);
			foreach (KeyValuePair<string, string> pair in composed) {
				startInfo.Environment[pair.Key] = pair.Value;
			}
			startInfo.Environment["CLIO_HOME"] = home;
			startInfo.Environment[OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME"] = home;
			startInfo.Environment["CLIO_NO_UPDATE_CHECK"] = "true";
			Process process = Process.Start(startInfo)
				?? throw new InvalidOperationException("Unable to start the clio MCP worker child process.");
			DirectWorker worker = new(process);
			try {
				await worker.ConnectAsync(cancellationToken);
			}
			catch {
				await worker.DisposeAsync();
				throw;
			}
			return worker;
		}

		public string Describe() {
			string standardError;
			lock (_standardError) {
				standardError = _standardError.ToString();
			}
			string shortened = standardError.Length <= 600 ? standardError : standardError[..600];
			return $"pid={_process.Id}, exited={_process.HasExited}, "
				+ $"stderr=[{shortened.Replace('\r', ' ').Replace('\n', ' ')}]";
		}

		public async ValueTask DisposeAsync() {
			if (Client is not null) {
				try {
					await Client.DisposeAsync();
				}
				catch (Exception) {
					// Teardown must not hide an assertion failure.
				}
			}
			try {
				if (!_process.HasExited) {
					_process.Kill(entireProcessTree: true);
					using CancellationTokenSource exitWait = new(TimeSpan.FromSeconds(10));
					await _process.WaitForExitAsync(exitWait.Token);
				}
			}
			catch (Exception) {
				// Best-effort teardown of a child this fixture owns.
			}
			if (_standardErrorPump is not null) {
				await Task.WhenAny(_standardErrorPump, Task.Delay(TimeSpan.FromSeconds(2)));
			}
			_process.Dispose();
		}

		private async Task ConnectAsync(CancellationToken cancellationToken) {
			// Drained continuously: an undrained pipe eventually blocks the child, which would surface as an
			// unexplained hang rather than as a failed assertion.
			_standardErrorPump = Task.Run(async () => {
				string? line;
				while ((line = await _process.StandardError.ReadLineAsync()) is not null) {
					lock (_standardError) {
						_standardError.AppendLine(line);
					}
				}
			});
			StreamClientTransport transport = new(_process.StandardInput.BaseStream,
				_process.StandardOutput.BaseStream, NullLoggerFactory.Instance);
			Client = await McpClient.CreateAsync(transport,
				new McpClientOptions {
					ClientInfo = new Implementation { Name = "clio.mcp.e2e.eng-102333-parent", Version = "1.0.0" }
				},
				NullLoggerFactory.Instance, cancellationToken);
		}
	}
}
