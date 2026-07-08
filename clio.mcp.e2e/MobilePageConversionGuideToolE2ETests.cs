using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the get-mobile-page-conversion-guide MCP tool. These are NoEnvironment tests:
/// they exercise discovery and the graceful-failure contract of the real server process without a
/// stood-up Creatio (the happy path requires a source page and is covered by unit tests + the sandbox tier).
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(MobilePageConversionGuideTool.ToolName)]
[NonParallelizable]
public sealed class MobilePageConversionGuideToolE2ETests : McpContractFixtureBase {

	private const string ToolName = MobilePageConversionGuideTool.ToolName;

	// get-mobile-page-conversion-guide is gated behind [FeatureToggle("mobile-page-converter")], so the
	// shared child server is started with an isolated CLIO_HOME whose appsettings enables the flag —
	// otherwise the tool would not be registered and discovery/invocation would fail.
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		string clioHome = CreateIsolatedClioHome(
			"""
			{
			  "ActiveEnvironmentKey": "dev",
			  "Autoupdate": false,
			  "Features": { "mobile-page-converter": true },
			  "Environments": {
			    "dev": { "Uri": "http://localhost", "Login": "Supervisor", "Password": "Supervisor", "IsNetCore": true }
			  }
			}
			""",
			GetType().Name);
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = clioHome;
	}

	[Test]
	[Description("Advertises get-mobile-page-conversion-guide so MCP callers can discover the web->mobile conversion guide tool.")]
	[AllureTag(ToolName)]
	[AllureName("get-mobile-page-conversion-guide tool is discoverable")]
	[AllureDescription("Starts the real clio MCP server and verifies get-mobile-page-conversion-guide is reachable on the MCP tool surface.")]
	public async Task MobilePageConversionGuideTool_Should_Be_Discoverable() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "get-mobile-page-conversion-guide must be advertised so MCP callers can discover the conversion-guide tool");
	}

	[Test]
	[Description("Returns a structured failure (not a protocol error) when the target environment is not registered, so the caller can read why the source page could not be read.")]
	[AllureTag(ToolName)]
	[AllureName("get-mobile-page-conversion-guide reports invalid environment failures")]
	[AllureDescription("Calls get-mobile-page-conversion-guide with an unregistered environment through the real MCP server and verifies the tool returns a readable structured failure envelope.")]
	public async Task MobilePageConversionGuideTool_Should_Report_Failure_For_Invalid_Environment() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-mobile-guide-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrDoesNotExist_FormPage",
					["environment-name"] = invalidEnvironmentName
				}
			},
			context.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "the tool catches the read failure and returns a structured guide response instead of a protocol-level error");
		MobilePageConversionGuideResponse response =
			EntitySchemaStructuredResultParser.Extract<MobilePageConversionGuideResponse>(callResult);
		response.Success.Should().BeFalse(
			because: "the source page cannot be read from an unregistered environment, so the conversion guide must fail");
		response.Error.Should().NotBeNullOrWhiteSpace(
			because: "a failed conversion guide must carry an actionable diagnostic explaining the read failure");
	}
}
