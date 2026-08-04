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
	[Description("ENG-91228: returns the freedom-page-web-to-mobile-conversion guidance article over the real MCP surface and verifies it carries the deterministic empty-container removal contract (already removed by the converter, arrives as a drop with reason \"empty container\", never re-create / re-parent / ask the user) plus the AreaProfileContainer wrapper-content routing.")]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the conversion article with the deterministic empty-container removal contract")]
	[AllureDescription("Starts the real clio MCP server with mobile-page-converter enabled and verifies get-guidance resolves the feature-gated freedom-page-web-to-mobile-conversion article carrying the ENG-91228 empty-container removal wording end to end.")]
	public async Task GuidanceGet_Should_Return_Conversion_Guide_With_EmptyContainerRemoval_Contract() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			GuidanceGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["name"] = "freedom-page-web-to-mobile-conversion"
				}
			},
			context.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "get-guidance should return a normal MCP tool result envelope for a registered guidance name");
		GuidanceGetResponse response = EntitySchemaStructuredResultParser.Extract<GuidanceGetResponse>(callResult);
		response.Success.Should().BeTrue(
			because: "freedom-page-web-to-mobile-conversion is a registered guidance name while mobile-page-converter is enabled");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/freedom-page-web-to-mobile-conversion",
			because: "the canonical resource URI for the conversion guide should be stable");
		response.Article.Text.Should().Contain("removed deterministically by the converter",
			because: "the drop branch must state that empty converter-created containers are removed by the converter itself, not left for the user to delete (ENG-91228)");
		response.Article.Text.Should().Contain("\"empty container\"",
			because: "the article must name the exact drop reason the elementMap carries for a removed empty container so the caller can recognize it");
		response.Article.Text.Should().Contain(
			"Do NOT re-create such a container, do NOT re-parent anything into it, and do NOT ask the user",
			because: "the article must forbid the caller from resurrecting a removed empty container or turning its removal into a question (ENG-91228)");
		response.Article.Text.Should().Contain("CardContentWrapper→AreaProfileContainer",
			because: "the wrapper's non-tab content must be routed into the profile Area card, never directly into the general tab's grid (ENG-91228 wording fix)");
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

/// <summary>
/// Feature-gate coverage for get-mobile-page-conversion-guide. The tool is registered ONLY when the
/// `mobile-page-converter` feature flag is enabled; this fixture starts the real server with the flag
/// explicitly OFF and proves the tool is absent from the MCP surface — the negative of the discovery test
/// in <see cref="MobilePageConversionGuideToolE2ETests"/>. A separate fixture is required because the child
/// server (and its CLIO_HOME) is started once per fixture in one-time setup.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(MobilePageConversionGuideTool.ToolName)]
[NonParallelizable]
public sealed class MobilePageConversionGuideToolFeatureGateE2ETests : McpContractFixtureBase {

	private const string ToolName = MobilePageConversionGuideTool.ToolName;

	// Same isolated CLIO_HOME shape as the enabled fixture, but with the gating flag explicitly DISABLED, so
	// the tool must NOT be registered on the server.
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		string clioHome = CreateIsolatedClioHome(
			"""
			{
			  "ActiveEnvironmentKey": "dev",
			  "Autoupdate": false,
			  "Features": { "mobile-page-converter": false },
			  "Environments": {
			    "dev": { "Uri": "http://localhost", "Login": "Supervisor", "Password": "Supervisor", "IsNetCore": true }
			  }
			}
			""",
			GetType().Name);
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = clioHome;
	}

	[Test]
	[Description("Does NOT advertise get-mobile-page-conversion-guide when the mobile-page-converter feature flag is disabled, proving the tool is feature-gated.")]
	[AllureTag(ToolName)]
	[AllureName("get-mobile-page-conversion-guide is hidden when its feature flag is off")]
	[AllureDescription("Starts the real clio MCP server with mobile-page-converter disabled and verifies get-mobile-page-conversion-guide is NOT on the MCP tool surface.")]
	public async Task MobilePageConversionGuideTool_IsNotDiscoverable_WhenFeatureFlagDisabled() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().NotContain(ToolName,
			because: "get-mobile-page-conversion-guide is gated behind the mobile-page-converter feature flag and must be absent when the flag is off");
	}

	[Test]
	[Description("Hides the freedom-page-web-to-mobile-conversion guidance article when the mobile-page-converter feature flag is disabled, proving the article shares the tool's feature gate.")]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance hides the conversion article when its feature flag is off")]
	[AllureDescription("Starts the real clio MCP server with mobile-page-converter disabled and verifies get-guidance treats freedom-page-web-to-mobile-conversion as unknown and omits it from availableGuides.")]
	public async Task GuidanceGet_TreatsConversionGuideAsUnknown_WhenFeatureFlagDisabled() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			GuidanceGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["name"] = "freedom-page-web-to-mobile-conversion"
				}
			},
			context.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "get-guidance reports an unknown guidance name as a structured failure, not a protocol-level error");
		GuidanceGetResponse response = EntitySchemaStructuredResultParser.Extract<GuidanceGetResponse>(callResult);
		response.Success.Should().BeFalse(
			because: "the conversion article is gated behind the disabled mobile-page-converter feature and must resolve as unknown");
		response.Article.Should().BeNull(
			because: "a disabled gated guide must not return its article over the real MCP transport");
		response.AvailableGuides.Should().NotContain("freedom-page-web-to-mobile-conversion",
			because: "a disabled gated guide must not be advertised in availableGuides");
	}
}
