using System.Text.Json;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for issue #1322: <c>create-sql-schema</c> invoked through <c>clio-run</c> against a
/// <c>ScriptSchemaDesignerService</c> that answers with a body clio cannot parse must report a failure that
/// names the service, the operation and the endpoint — never the bare Newtonsoft parser message the issue
/// was filed about, and never the HTML of an error or login page.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(SqlSchemaCreateTool.ToolName)]
[NonParallelizable]
public sealed class SqlSchemaCreateNonJsonResponseE2ETests {

	private const string EnvironmentKey = "sql-schema-1322-e2e";
	private const string RawParserMessage = "Error reading JObject from JsonReader";

	[TestCase(SqlSchemaDesignerStubResponse.EmptyBody, "empty response",
		TestName = "CreateSqlSchema_ReportsNamedFailure_WhenDesignerReturnsEmptyBody")]
	[TestCase(SqlSchemaDesignerStubResponse.HtmlErrorPage, "HTML page instead of JSON",
		TestName = "CreateSqlSchema_ReportsNamedFailure_WhenDesignerReturnsHtmlErrorPage")]
	[TestCase(SqlSchemaDesignerStubResponse.HtmlLoginPage, "HTML page instead of JSON",
		TestName = "CreateSqlSchema_ReportsNamedFailure_WhenDesignerReturnsLoginPage")]
	[Category("E2E")]
	[Description("create-sql-schema run through clio-run reports a classified, service-named failure instead of the raw JSON parser message when ScriptSchemaDesignerService answers with an unusable body (issue #1322).")]
	[AllureTag(SqlSchemaCreateTool.ToolName)]
	[AllureName("create-sql-schema classifies an unusable designer response")]
	public async Task ClioRun_ShouldReportClassifiedFailure_WhenDesignerResponseIsNotJson(
		SqlSchemaDesignerStubResponse designerResponse, string expectedClassification) {
		// Arrange
		await using SqlSchemaDesignerStubServer creatioStub = SqlSchemaDesignerStubServer.Start(designerResponse);
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-sql-schema-1322-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		string envVarName = OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME";
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		settings.ProcessEnvironmentVariables[envVarName] = tempHome;
		using TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
			$$"""
			{
			  "ActiveEnvironmentKey": "{{EnvironmentKey}}",
			  "Environments": {
			    "{{EnvironmentKey}}": {
			      "Uri": "{{creatioStub.BaseUrl}}",
			      "Login": "Supervisor",
			      "Password": "Supervisor",
			      "IsNetCore": false
			    }
			  }
			}
			""",
			settings.ClioProcessPath,
			settings.ProcessEnvironmentVariables);
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await AllureApi.Step(
			"Arrange the real MCP process and the designer stub",
			async () => await McpServerSession.StartAsync(settings, cancellationTokenSource.Token));

		try {
			// Act
			CallToolResult callResult = await AllureApi.Step(
				"Act by invoking create-sql-schema through clio-run",
				async () => await session.CallToolAsync(
					ClioRunTool.ToolName,
					new Dictionary<string, object?> {
						["command"] = SqlSchemaCreateTool.ToolName,
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = EnvironmentKey,
							["schema-name"] = "UsrIssue1322Probe",
							["package-name"] = "Custom"
						}
					},
					cancellationTokenSource.Token));
			string serialized = JsonSerializer.Serialize(callResult);

			// Assert
			AllureApi.Step("Assert the failure names the service and the operation", () =>
				serialized.Should().Contain("ScriptSchemaDesignerService CreateNewSchema",
					because: "the caller must learn which designer service and operation produced the unusable body"));
			AllureApi.Step("Assert the failure classifies the response", () =>
				serialized.Should().Contain(expectedClassification,
					because: "an empty body and an HTML page are different causes and must be reported as such"));
			AllureApi.Step("Assert the raw parser message is gone", () =>
				serialized.Should().NotContain(RawParserMessage,
					because: "the bare Newtonsoft message is exactly what issue #1322 reported as unactionable"));
			AllureApi.Step("Assert no page markup is echoed back", () =>
				serialized.Should().NotContain("stub-session-token",
					because: "an error or login page can carry session tokens, so its body is never echoed into a transcript"));
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
		}
		catch (IOException) { /* best-effort cleanup */ }
		catch (UnauthorizedAccessException) { /* best-effort cleanup */ }
	}
}
