using System.Diagnostics;
using System.Text.Json;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol;
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
	/// "timeout" below and than the status poll that follows it, far shorter than clio's 150 s deadline.
	/// </summary>
	private static readonly TimeSpan CompileHold = TimeSpan.FromSeconds(30);

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
	[Description("ENG-102333 TC-01/TC-03: after the client cancels compile-creatio, compile-status answers running and then the final status with exit code and message tail, and a second compile-creatio for the target is refused as already in progress.")]
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
				""",
				settings.ClioProcessPath,
				settings.ProcessEnvironmentVariables);
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
		JsonRpcRequest request = new() {
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
		using CancellationTokenSource client = CancellationTokenSource.CreateLinkedTokenSource(scenario);
		Task<JsonRpcResponse> call = session.Client.SendRequestAsync(request, client.Token);
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
}
