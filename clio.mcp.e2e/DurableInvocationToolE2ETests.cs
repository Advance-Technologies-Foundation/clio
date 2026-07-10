using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the durable (forgiving) unmatched-name invocation path (ENG-93370) over the
/// real MCP server. After the lazy-schema split (PR #743) hid the long tail from <c>tools/list</c>, a
/// direct <c>tools/call</c> naming a long-tail tool used to dead-end with an opaque "Unknown tool".
/// The durable handler restores the pre-lazy contract: a non-destructive tool executes with an
/// advisory note, a destructive tool returns a structured <c>confirmation-required</c> retry shape
/// (never silently executed), a deprecated alias resolves to its canonical tool, and an unknown name
/// returns a machine-readable did-you-mean outcome. All cases here are environment-free: they prove
/// the invocation contract without needing a live Creatio.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("durable-invocation")]
[NonParallelizable]
public sealed class DurableInvocationToolE2ETests : McpContractFixtureBase {

	[Test]
	[Category("E2E")]
	[Description("A direct tools/call to a NON-DESTRUCTIVE long-tail tool executes through the forgiving handler and carries the model-visible advisory in Content (ENG-93370: the pre-#743 invocation contract is restored).")]
	[AllureTag("durable-invocation")]
	[AllureName("direct long-tail non-destructive call executes with advisory")]
	public async Task DirectCall_ShouldExecuteWithAdvisory_WhenLongTailToolIsNonDestructive() {
		// Arrange — `experimental` (list mode, no args) is a long-tail, non-destructive,
		// environment-free tool: it reads the local feature flags only.
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act — call it by BARE NAME, exactly as stale static guidance would.
		CallToolResult callResult = await context.Session.CallToolRawAsync(
			"experimental",
			new Dictionary<string, object?>(),
			context.CancellationTokenSource.Token);

		// Assert
		string serialized = SerializeResult(callResult);
		serialized.Should().NotContain("Unknown tool",
			because: "the durable handler must resolve a real long-tail tool instead of dead-ending");
		// Note: quotes inside the serialized JSON are '-escaped, so the marker avoids them.
		serialized.Should().Contain("[clio] Executed",
			because: "a forgiving execution must teach the agent the advertised clio-run path via a Content advisory");
		serialized.Should().Contain("exit-code",
			because: "the tool's own payload must be returned alongside the advisory (it really executed)");
	}

	[Test]
	[Category("E2E")]
	[Description("A direct tools/call to a DESTRUCTIVE long-tail tool is NOT executed: the handler returns a structured confirmation-required outcome with a ready-to-retry clio-run-destructive shape (ENG-93370).")]
	[AllureTag("durable-invocation")]
	[AllureName("direct destructive call returns confirmation-required, no execution")]
	public async Task DirectCall_ShouldReturnConfirmationRequired_WhenToolIsDestructive() {
		// Arrange — restart-by-environment-name is destructive; with no live environment nothing could
		// restart anyway, but the handler must refuse BEFORE any execution attempt.
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolRawAsync(
			"restart-by-environment-name",
			new Dictionary<string, object?> { ["environmentName"] = "e2e-nonexistent-env" },
			context.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().BeTrue(
			because: "a destructive tool must never be silently executed from the forgiving path");
		string serialized = SerializeResult(callResult);
		serialized.Should().Contain("confirmation-required",
			because: "the outcome must be machine-readable so the agent can branch on it");
		serialized.Should().Contain("clio-run-destructive",
			because: "the retry shape routes the call through the advertised, host-gated executor");
		serialized.Should().Contain("correlation-id",
			because: "every handler outcome carries a correlation id");
	}

	[Test]
	[Category("E2E")]
	[Description("A deprecated camelCase alias resolves through the compatibility catalog to its canonical tool over the wire (ENG-93370: MCP-boundary backward compatibility).")]
	[AllureTag("durable-invocation")]
	[AllureName("deprecated alias resolves to canonical tool")]
	public async Task DirectCall_ShouldResolveDeprecatedAlias_WhenLegacyNameIsUsed() {
		// Arrange — restart-by-environmentName is no longer a registered tool method; only the
		// compatibility catalog can resolve it. The canonical tool is destructive, so the proof of
		// resolution is a confirmation-required outcome naming the CANONICAL tool.
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolRawAsync(
			"restart-by-environmentName",
			new Dictionary<string, object?> { ["environmentName"] = "e2e-nonexistent-env" },
			context.CancellationTokenSource.Token);

		// Assert
		string serialized = SerializeResult(callResult);
		serialized.Should().Contain("restart-by-environment-name",
			because: "the legacy alias must resolve to the canonical kebab-case tool name");
		serialized.Should().Contain("confirmation-required",
			because: "the resolved canonical tool is destructive, so the pre-lazy prompt is reproduced");
		serialized.Should().NotContain("unknown-tool",
			because: "a declared alias is never an unknown name");
	}

	[Test]
	[Category("E2E")]
	[Description("An unknown tool name returns a structured unknown-tool outcome with did-you-mean candidates and the discovery hint instead of an opaque dead end (ENG-93370).")]
	[AllureTag("durable-invocation")]
	[AllureName("unknown name returns did-you-mean + discovery hint")]
	public async Task DirectCall_ShouldReturnDidYouMean_WhenNameIsUnknown() {
		// Arrange — a one-letter typo of a real long-tail tool.
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolRawAsync(
			"get-fsm-modee",
			new Dictionary<string, object?>(),
			context.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().BeTrue(because: "an unknown name cannot be executed");
		string serialized = SerializeResult(callResult);
		serialized.Should().Contain("unknown-tool",
			because: "the outcome code must be machine-readable");
		serialized.Should().Contain("get-fsm-mode",
			because: "the nearest real tool name must be offered as a did-you-mean candidate");
		serialized.Should().Contain("get-tool-contract",
			because: "the discovery hint routes the agent to the compact catalog index");
	}

	[Test]
	[Category("E2E")]
	[Description("The forgiving handler does not change the advertised surface: tools/list still returns only the resident lazy profile (ENG-93370 preserves the PR #743 context economy).")]
	[AllureTag("durable-invocation")]
	[AllureName("tools/list surface is unchanged by the durable handler")]
	public async Task ToolsList_ShouldStayResidentOnly_WhenDurableHandlerIsRegistered() {
		// Arrange
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		var tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);

		// Assert
		tools.Select(tool => tool.Name).Should().NotContain("experimental",
			because: "long-tail tools stay hidden from tools/list; the durable handler only affects invocation");
		tools.Count.Should().BeLessThan(40,
			because: "the advertised surface must remain the small resident profile, not the full catalog");
	}

	// Serializes the tool result (structured content preferred, content blocks as fallback) to a JSON
	// string so assertions can look for markers without coupling to the response DTO shape.
	private static string SerializeResult(CallToolResult callResult) =>
		JsonSerializer.Serialize(callResult.StructuredContent) + JsonSerializer.Serialize(callResult.Content);
}
