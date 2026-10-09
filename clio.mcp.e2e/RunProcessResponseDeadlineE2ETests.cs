using System.Diagnostics;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// ENG-102333 round 3: run-process answers at the shared MCP response deadline, so a client that gives up at 60 s and
/// then restarts the MCP server (Claude Code desktop) gets its answer first. A deadline that comes before the launch
/// request was sent is answered not-started, and the request is then never sent.
/// </summary>
/// <remarks>
/// The launch is held back by the wedge stub's LOGIN delay, the first request the call makes. The deadline is lowered
/// to ten seconds - enough for a cold server to dispatch the call and reach that login, far below the 30 s hold - so
/// the scenario is fast; what it proves - the answer does not wait for the preparation, and it says nothing was
/// launched - does not depend on the value. That the request is never sent AFTER that answer, and the still-running
/// answer, need a stub that serves a process model, so the unit tests cover them
/// (<c>RunProcess_Should_Not_Send_The_Launch_After_Answering_NotStarted</c>).
/// </remarks>
[TestFixture]
[AllureNUnit]
[AllureFeature(RunProcessTool.ToolName)]
public sealed class RunProcessResponseDeadlineE2ETests {

	private const string EnvironmentName = "tc102333-run-process-stub";

	private const string ProcessCode = "UsrTc102333Process";

	private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

	private static readonly TimeSpan LoginHold = TimeSpan.FromSeconds(30);

	private static readonly TimeSpan ScenarioBudget = TimeSpan.FromMinutes(3);

	[Test]
	[Category("McpE2E.NoEnvironment")]
	[AllureTag(RunProcessTool.ToolName)]
	[AllureName("run-process answers not-started at the response deadline when its launch request was not sent")]
	[AllureDescription("Starts the real clio MCP server with a ten-second response deadline against a Creatio stub that holds the login for 30 s, and dispatches run-process through clio-run. The answer must arrive long before the login returns, with status not-started and an error saying nothing was launched; by then the stub must have received the login and no RunProcess request.")]
	[Description("ENG-102333 round 3: a run-process call whose preparation outlasts the MCP response deadline answers not-started at the deadline instead of holding the client until the environment answers.")]
	public async Task RunProcess_Should_AnswerNotStarted_AtTheDeadline_WhenItsLaunchWasNotSent() {
		// Arrange
		await using CreatioWedgeStubServer stub = CreatioWedgeStubServer.Start();
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-e2e-102333p-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		try {
			McpE2ESettings settings = TestConfiguration.Load();
			settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			settings.ProcessEnvironmentVariables[OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME"] = tempHome;
			settings.ProcessEnvironmentVariables["CLIO_HOME"] = tempHome;
			settings.ProcessEnvironmentVariables[McpProgressHeartbeat.ResponseDeadlineOverrideEnvVar] =
				Deadline.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
			using TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
				StubSettings(stub), settings.ClioProcessPath, settings.ProcessEnvironmentVariables);
			using CancellationTokenSource scenario = new(ScenarioBudget);
			await using McpServerSession session = await McpServerSession.StartAsync(settings, scenario.Token);
			stub.ResetCounters();
			stub.SetLoginDelay(LoginHold);

			// Act
			Stopwatch waited = Stopwatch.StartNew();
			CallToolResult answered = await AllureApi.Step("run-process while its login hangs",
				async () => await session.CallToolAsync(ClioRunTool.ToolName,
					new Dictionary<string, object?> {
						["command"] = RunProcessTool.ToolName,
						["args"] = new Dictionary<string, object?> {
							["process-name"] = ProcessCode,
							["environment-name"] = EnvironmentName
						}
					}, scenario.Token));
			waited.Stop();
			RunProcessEnvelope envelope = EntitySchemaStructuredResultParser.Extract<RunProcessEnvelope>(answered);

			// Assert
			waited.Elapsed.Should().BeLessThan(LoginHold,
				because: "the answer must not wait for a preparation that hangs; it came after {0}: {1}",
				waited.Elapsed, envelope.Error);
			envelope.Status.Should().Be(RunProcessTool.NotStartedStatus,
				because: "the launch request had not been sent when the deadline answered. Stub: {0}", stub.DescribeState());
			envelope.Error.Should().Contain("was not launched", because: "the answer is a refusal to launch, not a verdict");
			envelope.Error.Should().Contain("nothing ran",
				because: "the caller must know no run started, so calling again cannot duplicate one");
			envelope.Error.Should().Contain("the launch request was never sent",
				because: "the guidance tells this answer from a platform refusal by exactly this phrase");
			stub.LoginCount.Should().BeGreaterThan(0,
				because: "the call must have reached the environment - its login is what hangs. Stub: {0}",
				stub.DescribeState());
			stub.RunProcessCount.Should().Be(0,
				because: "no launch request had reached the environment when the answer said so. Stub: {0}",
				stub.DescribeState());
			stub.UnexpectedHandlerFailures.Should().BeEmpty(
				because: "a broken stub would make every assertion above measure the instrument instead of clio");
		}
		finally {
			TryDeleteDirectory(tempHome);
		}
	}

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
