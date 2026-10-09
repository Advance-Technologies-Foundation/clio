using System.Diagnostics;
using System.Text.RegularExpressions;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// ENG-102333: the restart request itself runs inside the MCP response-deadline race, so a request that hangs - a
/// fresh worker's login, an application reloading after a compile - cannot hold the answer past a client's ceiling.
/// </summary>
/// <remarks>
/// The request is held open by the wedge stub's LOGIN delay, the first request the restart makes. The deadline is
/// lowered to two seconds so the scenario is fast; what it proves - the answer does not wait for the request, and the
/// operation it names is already tracked - does not depend on the value.
/// </remarks>
[TestFixture]
[AllureNUnit]
[AllureFeature("restart-web-app")]
public sealed class RestartRequestDeadlineE2ETests {

	private const string EnvironmentName = "tc102333-restart-stub";

	private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);

	private static readonly TimeSpan LoginHold = TimeSpan.FromSeconds(30);

	private static readonly TimeSpan ScenarioBudget = TimeSpan.FromMinutes(3);

	// The notice names the operation as "operation-id '<id>'"; System.Text.Json may write the apostrophes as \u0027.
	private static readonly Regex OperationId = new(@"operation-id (?:'|\\u0027)([0-9a-fA-F-]+)(?:'|\\u0027)",
		RegexOptions.CultureInvariant);

	[Test]
	[Category("McpE2E.NoEnvironment")]
	[AllureTag(RestartTool.RestartByEnvironmentNameToolName)]
	[AllureTag(RestartStatusTool.RestartStatusToolName)]
	[AllureName("restart-by-environment-name answers before its restart request returns")]
	[AllureDescription("Starts the real clio MCP server with a two-second response deadline against a Creatio stub that holds the restart's login for 30 s, and calls restart-by-environment-name. The answer must arrive long before the login returns, say the request is not yet confirmed and name an operation-id; restart-status must then report that operation running.")]
	[Description("ENG-102333: a restart request that has not returned when the response deadline passes answers in-progress at once, saying the request is not yet confirmed, and restart-status already tracks the operation it names.")]
	public async Task RestartInstanceByName_Should_AnswerBeforeItsRequestReturns_WhenTheRequestHangs() {
		// Arrange
		await using CreatioWedgeStubServer stub = CreatioWedgeStubServer.Start();
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-e2e-102333r-{Guid.NewGuid():N}");
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
			CallToolResult answered = await AllureApi.Step("restart-by-environment-name while its login hangs",
				async () => await session.CallToolAsync(RestartTool.RestartByEnvironmentNameToolName,
					new Dictionary<string, object?> {
						["environmentName"] = EnvironmentName,
						["waitReady"] = true,
						["waitTimeoutSeconds"] = 60
					}, scenario.Token));
			waited.Stop();
			RestartStatusResponse status = await AllureApi.Step("restart-status for that restart",
				async () => EntitySchemaStructuredResultParser.Extract<RestartStatusResponse>(
					await session.CallToolAsync(RestartStatusTool.RestartStatusToolName,
						new Dictionary<string, object?> {
							["args"] = new Dictionary<string, object?> { ["environment-name"] = EnvironmentName }
						}, scenario.Token)));

			// Assert
			string notice = string.Join(" | ", answered.Content.OfType<TextContentBlock>().Select(block => block.Text));
			waited.Elapsed.Should().BeLessThan(LoginHold,
				because: "the answer must not wait for a restart request that hangs; it came after {0}: {1}",
				waited.Elapsed, notice);
			notice.Should().Contain("has not been answered yet",
				because: "the notice must not claim the restart was accepted before its request returned: {0}", notice);
			Match operation = OperationId.Match(notice);
			operation.Success.Should().BeTrue(because: "the notice names the operation restart-status reports: {0}", notice);
			status.Status.Should().Be("running",
				because: "the operation is begun before the request, so restart-status already tracks it");
			status.OperationId.Should().Be(operation.Groups[1].Value,
				because: "restart-status must describe the operation the notice named");
			stub.LoginCount.Should().BeGreaterThan(0,
				because: "the restart request must have reached the environment - its login is what hangs. Stub: {0}",
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
