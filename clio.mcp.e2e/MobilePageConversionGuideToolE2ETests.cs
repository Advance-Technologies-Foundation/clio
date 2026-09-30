using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using System.Linq;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools.MobilePageConverter;
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

	// No ConfigureMcpServerSettings override: since ENG-94638 the converter is ungated, so it must be
	// reachable on the DEFAULT shared server. An isolated CLIO_HOME here would hide a re-gate.

	[Test]
	[Description("Registers get-mobile-page-conversion-guide on the DEFAULT server, proving the ENG-94638 un-gate: the index entry carries a destructive hint, which only the feature-filtered invoker registry can supply.")]
	[AllureTag(ToolName)]
	[AllureName("get-mobile-page-conversion-guide is registered on the default server")]
	[AllureDescription("Starts the real clio MCP server with no feature configuration and verifies get-mobile-page-conversion-guide is not merely NAMED in the discovery index but actually registered, by asserting the registry-derived destructive hint is present.")]
	public async Task MobilePageConversionGuideTool_Should_Be_Registered_On_The_Default_Server() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyList<ToolContractIndexEntry> index =
			await context.Session.GetToolContractIndexAsync(context.CancellationTokenSource.Token);

		// Assert
		ToolContractIndexEntry entry = index.SingleOrDefault(item =>
			string.Equals(item.Name, ToolName, StringComparison.OrdinalIgnoreCase));
		entry.Should().NotBeNull(
			because: "the converter must appear in the discovery index on a server with no feature configuration");
		entry!.Destructive.Should().BeFalse(
			because: "THIS is the GA guard, and it is the only assertion here that is one. The NAME proves nothing: it comes from the static, unfiltered CanonicalToolNames and survives a re-gate. The destructive hint is resolved from the FEATURE-FILTERED invoker registry and fails CLOSED, so a re-gated tool keeps its name in the index but flips to destructive=true. Verified by restoring the [FeatureToggle] and watching this line - and only this line - fail");
	}

	// The freedom-page-web-to-mobile-conversion article itself is no longer Clio-owned: since
	// "Externalize guidance delivery mechanics" (#927) get-guidance serves only articles delivered by an
	// installed, verified knowledge bundle, and the article now lives in the clio-knowledge repository.
	// A hermetic NoEnvironment fixture installs no knowledge source, so asserting the article's wording
	// here could only pass by contacting a real remote. The tool-surface contract stays covered by the
	// discovery and invalid-environment tests below; the article's wording belongs to clio-knowledge.

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

	[Test]
	[Description("ENG-95827: a REFUSAL reaches the caller as a structured result carrying the source schema name and a remedy, not as a transport error — so an agent can tell 'clio refused for a stated reason' from 'the tool crashed'. The branch now fails rather than degrading (an unobtainable template would ship inserts duplicating native elements), which makes the refusal envelope part of the tool's contract.")]
	[AllureTag(ToolName)]
	[AllureName("get-mobile-page-conversion-guide refusals arrive as structured results")]
	[AllureDescription("Calls get-mobile-page-conversion-guide through the real MCP server so it cannot obtain the templates it needs, and verifies the refusal is a readable structured envelope naming the schema and a remedy rather than a protocol-level error.")]
	public async Task MobilePageConversionGuideTool_Should_Deliver_Refusals_As_Structured_Results() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));
		const string schemaName = "UsrRefusalProbe_FormPage";

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = schemaName,
					["environment-name"] = $"missing-refusal-env-{Guid.NewGuid():N}"
				}
			},
			context.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a refusal is a decision the tool made, so it must travel as a result the caller can parse — never as a protocol fault, which an agent cannot distinguish from a crash");
		MobilePageConversionGuideResponse response =
			EntitySchemaStructuredResultParser.Extract<MobilePageConversionGuideResponse>(callResult);
		response.Success.Should().BeFalse(
			because: "the guide could not be produced, and the caller must not proceed to build a mobile page from it");
		response.SourceSchemaName.Should().Be(schemaName,
			because: "the refusal has to say WHICH page it is about — an agent converting several pages correlates on this");
		response.Error.Should().NotBeNullOrWhiteSpace(
			because: "every refusal states its cause and what to do about it; that is the whole reason failing beats returning a degraded guide");
		response.Guide.Should().BeNull(
			because: "a refused conversion must carry no guide at all — a partial one is exactly the degraded guide this branch replaced, and a caller might apply it");
	}
}
