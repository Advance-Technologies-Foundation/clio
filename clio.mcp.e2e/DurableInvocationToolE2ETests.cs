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
/// The durable handler restores the pre-lazy contract: a read-only tool executes with an advisory note,
/// a write-capable tool returns a structured <c>confirmation-required</c> retry shape (never silently
/// executed — issue #953 moved that gate from <c>destructiveHint</c> to <c>readOnlyHint</c>, so an
/// additive-only write is gated too), a deprecated alias resolves to its canonical tool, and an unknown
/// name returns a machine-readable did-you-mean outcome. All cases here are environment-free: they prove
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
	[Description("A direct tools/call to a READ-ONLY long-tail tool executes through the forgiving handler and carries the model-visible advisory in Content (ENG-93370: the pre-#743 invocation contract is restored).")]
	[AllureTag("durable-invocation")]
	[AllureName("direct long-tail read-only call executes with advisory")]
	public async Task DirectCall_ShouldExecuteWithAdvisory_WhenLongTailToolIsReadOnly() {
		// Arrange — `check-settings-health` is a long-tail, READ-ONLY, environment-free tool: it inspects the
		// local appsettings.json bootstrap state. It replaced `experimental` here when the gate moved to
		// readOnlyHint (issue #953): `experimental` is ReadOnly=false (it can toggle a persisted feature flag),
		// so it is now gated rather than executed and can no longer prove the execute path.
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act — call it by BARE NAME, exactly as stale static guidance would.
		CallToolResult callResult = await context.Session.CallToolRawAsync(
			"check-settings-health",
			new Dictionary<string, object?>(),
			context.CancellationTokenSource.Token);

		// Assert
		string serialized = SerializeResult(callResult);
		serialized.Should().NotContain("Unknown tool",
			because: "the durable handler must resolve a real long-tail tool instead of dead-ending");
		// Note: quotes inside the serialized JSON are '-escaped, so the marker avoids them.
		serialized.Should().Contain("[clio] Executed",
			because: "a forgiving execution must teach the agent the advertised clio-run path via a Content advisory");
		serialized.Should().Contain("settings-file-path",
			because: "the tool's own payload must be returned alongside the advisory (it really executed)");
	}

	[Test]
	[Category("E2E")]
	[Description("A direct tools/call to an ADDITIVE-ONLY write (odata-create: ReadOnly=false, Destructive=false) is NOT executed but answered with confirmation-required — the exact leak issue #953 reported, now closed by gating on write-capability instead of destructiveness.")]
	[AllureTag("durable-invocation")]
	[AllureName("direct additive-only write returns confirmation-required, no execution")]
	public async Task DirectCall_ShouldReturnConfirmationRequired_WhenToolIsAdditiveOnlyWrite() {
		// Arrange — odata-create is write-capable but NOT destructive. The gate must refuse BEFORE any
		// execution attempt, so no live environment is needed to prove it (the environment name below is
		// deliberately non-existent: reaching it at all would itself be the failure).
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolRawAsync(
			"odata-create",
			new Dictionary<string, object?> {
				["environment-name"] = "e2e-nonexistent-env",
				["collection"] = "Contact",
				["records"] = new[] { new Dictionary<string, object?> { ["Name"] = "e2e-should-never-be-created" } }
			},
			context.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().BeTrue(
			because: "a write-capable tool must never be silently executed from the forgiving path");
		string serialized = SerializeResult(callResult);
		serialized.Should().Contain("confirmation-required",
			because: "the outcome must be machine-readable so the agent can branch on it");
		serialized.Should().Contain("write-capable",
			because: "the payload must name write-capability as the reason the call was gated");
		serialized.Should().NotContain("e2e-should-never-be-created",
			because: "the retry shape must never echo caller argument VALUES back into the transcript");
	}

	[Test]
	[Category("E2E")]
	[Description("A direct tools/call to a DESTRUCTIVE long-tail tool is NOT executed: the handler returns a structured confirmation-required outcome with a ready-to-retry clio-run shape (ENG-93370).")]
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
		serialized.Should().NotContain("clio-run-destructive",
			because: "the retry shape names the CANONICAL executor, not the deprecated clio-run-destructive alias");
		serialized.Should().Contain("clio-run",
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
	[Description("The machine-readable outcome code survives the real McpToolErrorFilter in StructuredContent (not flattened to text): the return-not-thrown contract holds end to end over the wire (ENG-93370, TC-I-04).")]
	[AllureTag("durable-invocation")]
	[AllureName("outcome code survives the error filter in StructuredContent")]
	public async Task DirectCall_ShouldPreserveStructuredCode_ThroughErrorFilter() {
		// Arrange — an unknown name produces an IsError outcome. If the handler had THROWN (or the filter
		// flattened the error), McpToolErrorFilter would leave a text-only error with no StructuredContent,
		// losing the machine-readable code. The whole design rests on the outcome being RETURNED, so the
		// StructuredContent must arrive intact.
		await using var context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolRawAsync(
			"definitely-not-a-real-tool-xyz",
			new Dictionary<string, object?>(),
			context.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().BeTrue(because: "an unknown tool cannot be executed");
		callResult.StructuredContent.Should().NotBeNull(
			because: "the filter must preserve the returned StructuredContent, not flatten the outcome to text");
		JsonElement structured = callResult.StructuredContent!.Value;
		structured.GetProperty("code").GetString().Should().Be("unknown-tool",
			because: "the machine-readable outcome code must survive the McpToolErrorFilter intact");
		structured.GetProperty("correlation-id").GetString().Should().NotBeNullOrWhiteSpace(
			because: "the correlation id travels in StructuredContent alongside the code");
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
