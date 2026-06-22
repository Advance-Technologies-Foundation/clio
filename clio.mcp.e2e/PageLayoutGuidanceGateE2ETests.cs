using System;
using System.Collections.Generic;
using System.Threading;
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
/// End-to-end tests for the write-path layout-guidance gate shared by <c>update-page</c>
/// (<see cref="PageUpdateTool"/>) and <c>sync-pages</c> (<see cref="PageSyncTool"/>). The gate is
/// fail-closed: a body that adds or lays out <c>crt.*</c> view components (a <c>crt.*</c>
/// <c>insert</c> in <c>viewConfigDiff</c>) is rejected unless
/// <see cref="PageLayoutGuidanceGate.RequiredGuidanceName"/> was fetched this session, or the call
/// is forced. Unit tests cover the gate predicate in-process; this fixture proves the gate over the
/// real <c>clio mcp-server</c> stdio transport per the AGENTS.md MCP-e2e rule.
/// </summary>
/// <remarks>
/// The gate is exercised env-light through <c>update-page</c> with <c>dry-run:true</c>: the gate is
/// the LAST pre-execution check (after body syntax/content/lint validation and after sampling, which
/// dry-run skips) and fires BEFORE any environment connection or schema save, so no live Creatio
/// stand is required to reach it. The two calls in the gate-satisfied test reuse the SAME
/// <see cref="ArrangeContext"/> — one <see cref="McpServerSession"/> wraps one
/// <c>clio mcp-server</c> child process, so the priming get-guidance and the follow-up update-page
/// hit the same process-scoped <c>IGuidanceAccessLedger</c> singleton.
/// </remarks>
[TestFixture]
[AllureNUnit]
[AllureFeature(PageUpdateTool.ToolName)]
[NonParallelizable]
public sealed class PageLayoutGuidanceGateE2ETests {

	private const string UpdatePageTool = PageUpdateTool.ToolName;
	private const string GuidanceTool = GuidanceGetTool.ToolName;

	// A body that ADDS/LAYS OUT a crt.* component: a crt.FlexContainer insert in viewConfigDiff.
	// A pure layout container has no field binding or resource, so it clears the content validators
	// and reaches the layout-guidance gate. Matches the FlexContainer composition shape already used
	// by the existing destructive update-page/sync-pages tests in this suite.
	private const string LayoutCompositionBody =
		"define(\"UsrLayoutGate_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"insert\",\"name\":\"UsrE2EGateContainer\",\"values\":{\"type\":\"crt.FlexContainer\",\"direction\":\"row\",\"items\":[]},\"parentName\":\"Main\",\"propertyName\":\"items\",\"index\":0}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	// A NON-composition body: a merge-only viewConfigDiff (no insert) plus a handler. It adds/lays
	// out no components, so the layout-guidance gate must not fire even with an empty ledger.
	private const string NonCompositionBody =
		"define(\"UsrLayoutGate_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[{\"operation\":\"merge\",\"name\":\"UsrE2EGateContainer\",\"values\":{\"visible\":true}}]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[{ request: \"crt.HandleViewModelInitRequest\", handler: async (request, next) => { return next?.handle(request); } }]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	[Test]
	[Description("update-page rejects a layout-composing body (a crt.* insert in viewConfigDiff) end-to-end via the real MCP transport when get-guidance name=ui-page-layout was NOT called this session — proving the write-path layout-guidance gate fires before any environment connection.")]
	[AllureTag(UpdatePageTool)]
	[AllureName("update-page rejects layout composition when ui-page-layout was not fetched this session")]
	[AllureDescription("Starts the real clio MCP server and invokes update-page in dry-run mode with a crt.FlexContainer insert in a FRESH session where get-guidance name=ui-page-layout was never called. Verifies the structured response carries success=false and the layout-guidance rejection naming ui-page-layout — the gate must fire over the real MCP wire, not only in the in-process unit test, per the AGENTS.md MCP e2e rule. dry-run skips sampling so the gate is reached without a live Creatio stand.")]
	public async Task PageUpdateTool_Should_Reject_Layout_Composition_When_Guidance_Not_Fetched() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(TimeSpan.FromMinutes(3));

		// Act
		PageUpdateResponse response = await UpdatePageAsync(context, LayoutCompositionBody);

		// Assert
		response.Success.Should().BeFalse(
			because: "a crt.* layout-composing body must be blocked over the real MCP transport until ui-page-layout guidance is read this session");
		response.Error.Should().Contain(PageLayoutGuidanceGate.RequiredGuidanceName,
			because: "the rejection must name the get-guidance call the agent has to make (PageLayoutGuidanceGate.RequiredGuidanceName)");
		response.Error.Should().Contain("Layout guidance required",
			because: "the wire response must carry the canonical PageLayoutGuidanceGate.RejectionMessage prefix so the agent keys on the layout-guidance rejection specifically");
		response.Error.Should().Contain("force",
			because: "the rejection must tell the agent that force:true overrides the gate");
	}

	[Test]
	[Description("Within ONE mcp-server process, calling get-guidance name=ui-page-layout first satisfies the write-path layout-guidance gate so a subsequent update-page with the same crt.* layout-composing body is NOT rejected with the ui-page-layout gate message — proving the process-scoped guidance-access ledger spans calls within the same session.")]
	[AllureTag(UpdatePageTool)]
	[AllureName("update-page passes the layout-guidance gate after get-guidance name=ui-page-layout in the same session")]
	[AllureDescription("Reuses ONE clio mcp-server process (one McpServerSession). First calls get-guidance name=ui-page-layout (which records the canonical name in the process-scoped IGuidanceAccessLedger singleton), then calls update-page in dry-run mode with the same crt.FlexContainer composition body. Verifies the response is NOT the layout-guidance rejection — it may still fail downstream for unrelated reasons, but it must not carry the ui-page-layout gate message. This proves the ledger is shared across both calls on the same process.")]
	public async Task PageUpdateTool_Should_Pass_Layout_Gate_After_GetGuidance_In_Same_Session() {
		// Arrange — one session == one mcp-server process == one shared guidance-access ledger.
		await using ArrangeContext context = await ArrangeAsync(TimeSpan.FromMinutes(3));

		// Act 1: prime the ledger by fetching the ui-page-layout guidance leaf in this session.
		GuidanceGetResponse guidanceResponse = await GetGuidanceAsync(
			context, PageLayoutGuidanceGate.RequiredGuidanceName);
		// Act 2: submit the same layout-composing body on the SAME session/process.
		PageUpdateResponse response = await UpdatePageAsync(context, LayoutCompositionBody);

		// Assert
		guidanceResponse.Success.Should().BeTrue(
			because: "the priming get-guidance for ui-page-layout must succeed so the ledger records the canonical name");
		response.Error.Should().NotContain("Layout guidance required",
			because: "fetching ui-page-layout in this session satisfies the gate, so the layout-guidance rejection must not fire over the real MCP transport");
	}

	[Test]
	[Description("update-page does NOT fire the layout-guidance gate for a non-composition body (merge-only viewConfigDiff plus a handler, no crt.* insert) even with an empty ledger — proving the gate is scoped to component-adding bodies over the real MCP transport.")]
	[AllureTag(UpdatePageTool)]
	[AllureName("update-page does not fire the layout-guidance gate for a non-composition body")]
	[AllureDescription("Starts the real clio MCP server and invokes update-page in dry-run mode with a merge-only viewConfigDiff (no crt.* insert) in a fresh session where ui-page-layout was never fetched. Verifies the response does NOT carry the layout-guidance rejection — the gate must only block bodies that add/lay out components, mirroring the in-process unit coverage end to end.")]
	public async Task PageUpdateTool_Should_Not_Fire_Layout_Gate_For_Non_Composition_Body() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(TimeSpan.FromMinutes(3));

		// Act
		PageUpdateResponse response = await UpdatePageAsync(context, NonCompositionBody);

		// Assert
		response.Error.Should().NotContain("Layout guidance required",
			because: "a merge-only body adds no components, so the layout-guidance gate must not fire over the real MCP transport");
	}

	private static async Task<PageUpdateResponse> UpdatePageAsync(ArrangeContext context, string body) {
		// dry-run:true skips sampling so the gate is reached env-light (no environment-name needed);
		// the gate is the last pre-execution check and fires before any save.
		CallToolResult callResult = await context.Session.CallToolAsync(
			UpdatePageTool,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = $"UsrLayoutGate_{Guid.NewGuid():N}_FormPage",
					["body"] = body,
					["dry-run"] = true
				}
			},
			context.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(
			because: "the layout-guidance gate is a structured update-page response, not an MCP transport error");
		return EntitySchemaStructuredResultParser.Extract<PageUpdateResponse>(callResult);
	}

	private static async Task<GuidanceGetResponse> GetGuidanceAsync(ArrangeContext context, string name) {
		CallToolResult callResult = await context.Session.CallToolAsync(
			GuidanceTool,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["name"] = name }
			},
			context.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(
			because: "get-guidance should return a normal MCP tool result envelope for a registered guidance name");
		return EntitySchemaStructuredResultParser.Extract<GuidanceGetResponse>(callResult);
	}

	private static async Task<ArrangeContext> ArrangeAsync(TimeSpan timeout) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
