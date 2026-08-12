using System.Text.Json;
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
/// End-to-end coverage for ENG-93365: when the DataService SelectQuery endpoint answers with HTML instead
/// of JSON, <c>find-entity-schema</c> must surface a typed error naming the endpoint — never the raw
/// <c>System.Text.Json</c> message <c>'&lt;' is an invalid start of a value</c>, and never the HTML body.
/// The stub keys the HTML response on the queried schema, so environment registration and runtime
/// detection (which probe <c>SysAdminUnit</c>) still succeed.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(FindEntitySchemaTool.FindEntitySchemaToolName)]
[NonParallelizable]
public sealed class FindEntitySchemaNonJsonResponseE2ETests {
	private const string RegisterToolName = "reg-web-app";
	private const string QueriedSchemaName = "SysSchema";
	private const string RawParserFragment = "is an invalid start of a value";

	[Test]
	[AllureTag(FindEntitySchemaTool.FindEntitySchemaToolName)]
	[AllureName("find-entity-schema reports an HTML DataService response as a typed error")]
	[AllureDescription("Registers an environment against a stub whose SelectQuery answers with an HTML error page, then verifies find-entity-schema returns a structured failure naming the endpoint without the raw parser message or the HTML body.")]
	[Description("find-entity-schema against a DataService endpoint that returns HTML surfaces a typed endpoint error with the URL, and leaks neither the raw System.Text.Json parser message nor the HTML body.")]
	public async Task FindEntitySchema_Should_Report_Html_Response_As_Typed_Error() {
		// Arrange
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-find-entity-schema-nonjson-e2e-{Guid.NewGuid():N}");
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
					HtmlSelectQuerySchemaName: QueriedSchemaName));
			using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
			await using McpServerSession session = await McpServerSession.StartAsync(
				settings, cancellationTokenSource.Token);
			string environmentName = $"find-entity-schema-nonjson-{Guid.NewGuid():N}";
			await RegisterEnvironmentAsync(
				session, environmentName, stubServer.BaseUrl, cancellationTokenSource.Token);

			// Act
			CallToolResult callResult = await session.CallToolAsync(
				FindEntitySchemaTool.FindEntitySchemaToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["schema-name"] = "Contact"
					}
				},
				cancellationTokenSource.Token);

			// Assert
			string surfacedText = SerializeSurfacedText(callResult);
			surfacedText.Should().Contain("HTML page instead of JSON",
				because: "the agent must be told the endpoint answered with HTML so it can act on it");
			surfacedText.Should().Contain("SelectQuery",
				because: "the surfaced error must name the operation whose response could not be used");
			surfacedText.Should().Contain("[redacted-uri]",
				because: "the message carries the endpoint URL, which the MCP boundary redactor replaces before it reaches the transcript");
			surfacedText.Should().NotContain(RawParserFragment,
				because: "the raw System.Text.Json parser message must never cross the MCP boundary (ENG-93365)");
			surfacedText.Should().NotContain(RuntimeDetectionStubServer.SelectQueryHtmlBodyMarker,
				because: "an HTML error or login page can carry session tokens, so its body must not be surfaced");
		} finally {
			TryDeleteDirectory(tempHome);
		}
	}

	/// <summary>
	/// Flattens everything the tool result carries (content blocks plus structured content) into one string,
	/// so the assertions cover whatever channel the failure was surfaced on.
	/// </summary>
	/// <param name="callResult">Tool result returned by the MCP server.</param>
	/// <returns>Serialized text of the whole result payload.</returns>
	private static string SerializeSurfacedText(CallToolResult callResult) =>
		JsonSerializer.Serialize(callResult.Content) + JsonSerializer.Serialize(callResult.StructuredContent);

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
			because: "the stub environment must register successfully before find-entity-schema can be exercised against it");
	}
}
