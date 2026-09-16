using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Verifies missing-criteria diagnostics through both real MCP dispatch paths without a Creatio server.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(FindEntitySchemaTool.FindEntitySchemaToolName)]
public sealed class FindEntitySchemaValidationE2ETests : McpContractFixtureBase {

	/// <inheritdoc/>
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = CreateIsolatedClioHome("""
			{
			  "ActiveEnvironmentKey": "validation",
			  "Environments": {
			    "validation": {
			      "Uri": "http://127.0.0.1:1",
			      "Login": "unused",
			      "Password": "unused",
			      "IsNetCore": true
			    }
			  }
			}
			""", "find-entity-schema-validation");
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("Missing search criteria name MCP parameters without CLI prefixes through direct and clio-run dispatch.")]
	[AllureTag(FindEntitySchemaTool.FindEntitySchemaToolName)]
	[AllureName("Missing entity search criteria use parameter names")]
	[AllureDescription("Calls the real MCP server with an environment-only search and verifies the diagnostic on both dispatch paths. The configured endpoint is unreachable, so a network call cannot produce the expected validation response.")]
	public async Task FindEntitySchema_ShouldNameParameters_WhenCriteriaAreMissing(bool throughClioRun) {
		// Arrange
		await using var context = Arrange();
		Dictionary<string, object?> arguments = new() {
			["args"] = new Dictionary<string, object?> { ["environment-name"] = "validation" }
		};
		string toolName = FindEntitySchemaTool.FindEntitySchemaToolName;
		if (throughClioRun) {
			arguments["command"] = toolName;
			toolName = ClioRunTool.ToolName;
		}

		// Act
		CallToolResult result = await Session.CallToolRawAsync(
			toolName, arguments, context.CancellationTokenSource.Token);
		string text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

		// Assert
		AllureApi.Step("The missing-criteria call fails", () =>
			result.IsError.Should().BeTrue(because: "a search without criteria is invalid"));
		AllureApi.Step("The diagnostic names the contract parameters", () =>
			text.Should().Contain("At least one of 'schema-name', 'search-pattern', or 'uid' is required.",
				because: "MCP callers must be told the parameter names they can supply"));
		AllureApi.Step("The diagnostic contains no CLI flags", () =>
			text.Should().NotContain("--", because: "CLI flag syntax is not an MCP parameter name"));
	}
}
