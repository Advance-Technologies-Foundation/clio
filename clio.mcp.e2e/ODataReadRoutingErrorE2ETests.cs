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
/// End-to-end coverage for ENG-93088: odata-read must report a Web API routing error
/// (<c>{Message, MessageDetail}</c>, e.g. a 404 for an unregistered/uncompiled OData controller
/// returned with HTTP 200) as a structured failure instead of wrapping the error body as data.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(ODataReadTool.ToolName)]
[NonParallelizable]
public sealed class ODataReadRoutingErrorE2ETests {
	private const string RegisterToolName = "reg-web-app";
	private const string UnregisteredEntity = "UsrCustomerStatus";

	[Test]
	[AllureTag(ODataReadTool.ToolName)]
	[AllureName("odata-read reports a Web API routing error as a structured failure")]
	[AllureDescription("Registers an environment against a stub that returns the {Message, MessageDetail} routing body with HTTP 200 for an unregistered controller, then verifies odata-read returns success:false with the unregistered-entity hint.")]
	[Description("odata-read against an unregistered OData controller (a {Message, MessageDetail} 404 body served with HTTP 200) returns success:false with a clear message, not a masked single-entity success.")]
	public async Task ODataRead_Should_Report_Routing_Error_As_Failure() {
		// Arrange
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-odata-routing-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		try {
		string envVarName = OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME";
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		settings.ProcessEnvironmentVariables[envVarName] = tempHome;
		using TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
			"""
			{
			  "ActiveEnvironmentKey": null,
			  "Environments": {}
			}
			""",
			settings.ClioProcessPath,
			settings.ProcessEnvironmentVariables);
		await using RuntimeDetectionStubServer stubServer = RuntimeDetectionStubServer.Start(
			new RuntimeDetectionStubServerConfiguration(
				NetCoreHealthEnabled: true,
				NetFrameworkHealthEnabled: true,
				NetCoreServiceEnabled: false,
				NetFrameworkServiceEnabled: true,
				NetCoreUiMarkerEnabled: false,
				NetFrameworkUiMarkerEnabled: true,
				ODataRoutingErrorEntity: UnregisteredEntity));
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		string environmentName = $"odata-routing-{Guid.NewGuid():N}";
		await RegisterEnvironmentAsync(session, environmentName, stubServer.BaseUrl, cancellationTokenSource.Token);

		// Act
		CallToolResult callResult = await session.CallToolAsync(
			ODataReadTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["entity"] = UnregisteredEntity,
					["select"] = new[] { "Id", "Name" },
					["top"] = 10
				}
			},
			cancellationTokenSource.Token);
		ODataReadResponse response = EntitySchemaStructuredResultParser.Extract<ODataReadResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a bindable odata-read payload should return a structured tool response, not a protocol error");
		response.Success.Should().BeFalse(
			because: "a {Message, MessageDetail} routing body must be surfaced as a failure, not wrapped as a single-entity success");
		response.Error.Should().Contain($"controller named '{UnregisteredEntity}'",
			because: "the MessageDetail should be surfaced so the caller sees the unregistered-controller cause");
		response.Error.Should().Contain(ODataResponseError.UnregisteredEntityHint,
			because: "the unregistered-entity hint (asserted via the shared constant to avoid literal drift) should steer the agent to wait-and-retry, not read this as a data gap");
		}
		finally {
			TryDeleteDirectory(tempHome);
		}
	}

	private static void TryDeleteDirectory(string path) {
		try {
			if (Directory.Exists(path)) {
				Directory.Delete(path, recursive: true);
			}
		} catch {
			// Best-effort cleanup of the isolated home directory; a leaked temp dir must not fail the test.
		}
	}

	private static async Task RegisterEnvironmentAsync(
		McpServerSession session,
		string environmentName,
		string baseUrl,
		CancellationToken cancellationToken) {
		IReadOnlyCollection<string> toolNames = await session.ListReachableToolNamesAsync(cancellationToken);
		toolNames.Should().Contain(RegisterToolName,
			because: $"the {RegisterToolName} MCP tool must be discoverable before the test can register the stub environment");

		CallToolResult registerResult = await session.CallToolAsync(
			RegisterToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["uri"] = baseUrl,
					["login"] = "Supervisor",
					["password"] = "Supervisor"
				}
			},
			cancellationToken);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(registerResult);
		execution.ExitCode.Should().Be(0,
			because: "the stub environment must register successfully before odata-read can be exercised against it");
	}
}
