using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// clio-run / clio-run-destructive now dispatch by MCP tool NAME against the invoker registry (not by
/// CLI [Verb]): unknown tool → structured miss, destructiveness gate from the tool annotation, and a
/// known tool is invoked through the SDK and its CallToolResult is returned unchanged.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ClioRunDispatchTests {

	private IMcpToolInvokerRegistry _registry;
	private ClioRunExecutor _sut;

	[SetUp]
	public void SetUp() {
		_registry = Substitute.For<IMcpToolInvokerRegistry>();
		// An inert catalog substitute: TryResolveAlias defaults to false, so alias resolution never
		// interferes with the direct-name dispatch behavior these tests pin.
		_sut = new ClioRunExecutor(_registry, Substitute.For<IMcpToolCompatibilityCatalog>());
	}

	// A real SDK-built tool over a static echo method, so InvokeAsync executes without a live server.
	[McpServerToolType]
	private static class EchoToolType {
		[McpServerTool(Name = "echo-tool", Destructive = false)]
		[System.ComponentModel.Description("Echoes its input back.")]
		public static string Echo([System.ComponentModel.Description("payload")] string value) => $"echo:{value}";
	}

	private static McpServerTool BuildEchoTool() =>
		McpServerTool.Create(
			typeof(EchoToolType).GetMethod(nameof(EchoToolType.Echo))!,
			target: null,
			new McpServerToolCreateOptions { SerializerOptions = JsonSerializerOptions.Default });

	// A real SDK-built tool whose method throws, so InvokeAsync surfaces an exception (which the SDK
	// wraps) for the error-masking guard test.
	[McpServerToolType]
	private static class ThrowingToolType {
		internal const string FailureMessage = "boom: underlying tool blew up";

		[McpServerTool(Name = "throwing-tool", Destructive = false)]
		[System.ComponentModel.Description("Always throws.")]
		public static string Throw([System.ComponentModel.Description("payload")] string value) =>
			throw new InvalidOperationException(FailureMessage);
	}

	private static McpServerTool BuildThrowingTool() =>
		McpServerTool.Create(
			typeof(ThrowingToolType).GetMethod(nameof(ThrowingToolType.Throw))!,
			target: null,
			new McpServerToolCreateOptions { SerializerOptions = JsonSerializerOptions.Default });

	// A real SDK-built tool that catches internally and RETURNS a structured error result with raw
	// sensitive text (path + URI), rather than throwing — the return-path redaction guard.
	[McpServerToolType]
	private static class ErrorResultToolType {
		internal const string SensitiveErrorMessage =
			"Error: push to https://target.creatio.com/0/rest failed; see /Users/dev/secret/appsettings.json";

		[McpServerTool(Name = "error-result-tool", Destructive = false)]
		[System.ComponentModel.Description("Returns a structured error result with raw text.")]
		public static CallToolResult ReturnError([System.ComponentModel.Description("payload")] string value) =>
			new() { IsError = true, Content = [new TextContentBlock { Text = SensitiveErrorMessage }] };
	}

	private static McpServerTool BuildErrorResultTool() =>
		McpServerTool.Create(
			typeof(ErrorResultToolType).GetMethod(nameof(ErrorResultToolType.ReturnError))!,
			target: null,
			new McpServerToolCreateOptions { SerializerOptions = JsonSerializerOptions.Default });

	// A typed-POCO failure envelope: success:false + a raw error carrying a host/URI, returned as a plain
	// record (NOT a CallToolResult), so the SDK serialises it into StructuredContent WITHOUT setting
	// IsError — the long-tail leak the AttachDispatchAudit backstop must still scrub.
	public sealed record PocoFailureEnvelope(bool Success, string Error);

	[McpServerToolType]
	private static class PocoFailureToolType {
		internal const string SensitiveError =
			"Failed to connect to http://secret-host:88/0/odata; see /Users/dev/secret/appsettings.json";

		[McpServerTool(Name = "poco-failure-tool", Destructive = false)]
		[System.ComponentModel.Description("Returns a typed POCO failure envelope with raw text.")]
		public static PocoFailureEnvelope ReturnFailure([System.ComponentModel.Description("payload")] string value) =>
			new(Success: false, Error: SensitiveError);
	}

	private static McpServerTool BuildPocoFailureTool() =>
		McpServerTool.Create(
			typeof(PocoFailureToolType).GetMethod(nameof(PocoFailureToolType.ReturnFailure))!,
			target: null,
			new McpServerToolCreateOptions { SerializerOptions = JsonSerializerOptions.Default });

	// A real SDK-built tool whose method blocks past a tiny deadline, honouring the SDK-injected
	// CancellationToken, so the clio-run retry-safe branch can be driven into the read-response timeout
	// without a real slow backend.
	[McpServerToolType]
	private static class BlockingToolType {
		[McpServerTool(Name = "blocking-tool", Destructive = false, ReadOnly = true)]
		[System.ComponentModel.Description("Blocks until cancelled.")]
		public static async Task<string> Block(
			[System.ComponentModel.Description("payload")] string value,
			CancellationToken cancellationToken) {
			await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
			return $"echo:{value}";
		}
	}

	private static McpServerTool BuildBlockingTool() =>
		McpServerTool.Create(
			typeof(BlockingToolType).GetMethod(nameof(BlockingToolType.Block))!,
			target: null,
			new McpServerToolCreateOptions { SerializerOptions = JsonSerializerOptions.Default });

	// A typed-POCO SUCCESS envelope whose data legitimately carries URI/path-shaped strings, returned as a
	// plain record without IsError — the audit backstop must leave it completely untouched.
	public sealed record PocoSuccessEnvelope(bool Success, string Message, string Url, string FullPath);

	[McpServerToolType]
	private static class PocoSuccessToolType {
		internal const string LegitimateUrl = "https://legit-host:443/0/odata/Contact";
		internal const string LegitimatePath = @"F:\CreatioBuilds\10.1.268\build.zip";

		[McpServerTool(Name = "poco-success-tool", Destructive = false)]
		[System.ComponentModel.Description("Returns a typed POCO success envelope carrying a URL field.")]
		public static PocoSuccessEnvelope ReturnSuccess([System.ComponentModel.Description("payload")] string value) =>
			new(Success: true, Message: "Found 36 builds.", Url: LegitimateUrl, FullPath: LegitimatePath);
	}

	private static McpServerTool BuildPocoSuccessTool() =>
		McpServerTool.Create(
			typeof(PocoSuccessToolType).GetMethod(nameof(PocoSuccessToolType.ReturnSuccess))!,
			target: null,
			new McpServerToolCreateOptions { SerializerOptions = JsonSerializerOptions.Default });

	// A typed-POCO failure envelope that parks its raw failure detail under NON-"error" field names
	// (message + detail) — the long tail that IsFailureResult detects (success:false) but which the
	// narrow error-only redactor used to pass through verbatim. The backstop must now scrub these too.
	public sealed record PocoMessageFailureEnvelope(bool Success, string Message, string Detail);

	[McpServerToolType]
	private static class PocoMessageFailureToolType {
		internal const string SensitiveMessage = "Failed to connect to prod-db.internal:1433 while opening the pool";
		internal const string SensitiveDetail = "see /Library/Logs/clio/trace.log for the full stack";

		[McpServerTool(Name = "poco-message-failure-tool", Destructive = false)]
		[System.ComponentModel.Description("Returns a typed POCO failure envelope whose detail lives under message/detail, not error.")]
		public static PocoMessageFailureEnvelope ReturnFailure([System.ComponentModel.Description("payload")] string value) =>
			new(Success: false, Message: SensitiveMessage, Detail: SensitiveDetail);
	}

	private static McpServerTool BuildPocoMessageFailureTool() =>
		McpServerTool.Create(
			typeof(PocoMessageFailureToolType).GetMethod(nameof(PocoMessageFailureToolType.ReturnFailure))!,
			target: null,
			new McpServerToolCreateOptions { SerializerOptions = JsonSerializerOptions.Default });

	// A typed-POCO failure whose sensitive detail is NESTED under a non-"error" key inside a child
	// object, exercising the StructuredContent recursion path (RedactStructuredErrorFields) rather
	// than only the top-level text-content redaction.
	public sealed record NestedFailureDetail(string Reason);
	public sealed record PocoNestedFailureEnvelope(bool Success, NestedFailureDetail Inner);

	[McpServerToolType]
	private static class PocoNestedFailureToolType {
		internal const string SensitiveReason = "host unreachable: 10.0.0.5:1433";

		[McpServerTool(Name = "poco-nested-failure-tool", Destructive = false)]
		[System.ComponentModel.Description("Returns a typed POCO failure envelope whose detail is nested under a non-error key.")]
		public static PocoNestedFailureEnvelope ReturnFailure([System.ComponentModel.Description("payload")] string value) =>
			new(Success: false, Inner: new NestedFailureDetail(SensitiveReason));
	}

	private static McpServerTool BuildPocoNestedFailureTool() =>
		McpServerTool.Create(
			typeof(PocoNestedFailureToolType).GetMethod(nameof(PocoNestedFailureToolType.ReturnFailure))!,
			target: null,
			new McpServerToolCreateOptions { SerializerOptions = JsonSerializerOptions.Default });

	// RequestContext's constructor rejects a null server, so build an uninitialized instance (the
	// executor reuses this context and only sets Params/MatchedPrimitive before InvokeAsync).
	private static RequestContext<CallToolRequestParams> CallContext() =>
		(RequestContext<CallToolRequestParams>)System.Runtime.CompilerServices.RuntimeHelpers
			.GetUninitializedObject(typeof(RequestContext<CallToolRequestParams>));

	private void RegisterTool(string name, McpServerTool tool, bool destructive) {
		_registry.TryGetTool(name, out Arg.Any<McpServerTool>())
			.Returns(call => {
				call[1] = tool;
				return true;
			});
		_registry.IsDestructive(name).Returns(destructive);
	}

	private static string ErrorText(CallToolResult result) =>
		string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

	[Test]
	[Category("Unit")]
	[Description("Returns a structured 'unknown tool' result (not an exception) when the tool name is not registered.")]
	public async Task RunAsync_ShouldReturnUnknownToolResult_WhenToolIsNotRegistered() {
		// Arrange
		_registry.TryGetTool("nope", out Arg.Any<McpServerTool>()).Returns(false);

		// Act
		CallToolResult result = await _sut.RunAsync("nope", null, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		string text = ErrorText(result);
		result.IsError.Should().BeTrue(because: "an unknown tool is a failure result");
		text.Should().Contain("unknown tool 'nope'",
			because: "the failure must be a structured unknown-tool message");
		text.Should().Contain("get-tool-contract",
			because: "the unknown-tool error must always point the agent at the discovery tool");
		text.Should().Contain("compact index of every tool",
			because: "the discovery hint must name the cheap compact-index path so a wrong guess has an in-band recovery route");
	}

	[Test]
	[Category("Unit")]
	[Description("Appends a 'did you mean' shortlist of the nearest real tool names to the unknown-tool error so the agent can self-correct without an extra discovery round-trip.")]
	public async Task RunAsync_ShouldSuggestNearestRealTools_WhenToolNameIsNearMiss() {
		// Arrange
		_registry.ToolNames.Returns(new[] { "find-app", "find-entity-schema", "list-apps" });
		_registry.TryGetTool("find-ap", out Arg.Any<McpServerTool>()).Returns(false);

		// Act
		CallToolResult result = await _sut.RunAsync("find-ap", null, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		string text = ErrorText(result);
		result.IsError.Should().BeTrue(because: "an unknown tool is a failure result");
		text.Should().Contain("Did you mean",
			because: "a near-miss must carry a self-correction hint instead of a bare error");
		text.Should().Contain("find-app",
			because: "the nearest real tool name (Levenshtein distance 1) must lead the suggestions");
	}

	[Test]
	[Category("Unit")]
	[Description("Appends the get-tool-contract compact-index discovery hint to the unknown-tool error WHEN a 'did you mean' shortlist is present, so even after wrong guesses the agent still has the full-catalog path.")]
	public async Task RunAsync_ShouldAppendDiscoveryHint_WhenUnknownToolHasSuggestions() {
		// Arrange
		_registry.ToolNames.Returns(new[] { "find-app", "find-entity-schema", "list-apps" });
		_registry.TryGetTool("find-ap", out Arg.Any<McpServerTool>()).Returns(false);

		// Act
		CallToolResult result = await _sut.RunAsync("find-ap", null, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		string text = ErrorText(result);
		result.IsError.Should().BeTrue(because: "an unknown tool is a failure result");
		text.Should().Contain("Did you mean",
			because: "this case must carry the suggestion shortlist so the hint coexists with suggestions");
		text.Should().EndWith(ToolContractGetTool.DiscoveryHint,
			because: "the discovery hint must trail the message even after the 'did you mean' suggestions");
		text.Should().NotContain("  ",
			because: "the hint must follow with exactly one space (no double space) after the suggestions segment");
	}

	// The executor's suggestion source unions the (mockable) registry with the static reflection catalog
	// of every clio MCP tool, which is always non-empty — so a real ClioRunExecutor cannot emit a literally
	// empty 'did you mean' segment. The append is unconditional (outside the suggestions ternary), so this
	// pins the invariant that matters regardless of the shortlist: the hint is the trailing sentence and is
	// appended with correct single-space punctuation, with no empty-fragment double punctuation.
	[Test]
	[Category("Unit")]
	[Description("The get-tool-contract compact-index discovery hint is always the trailing sentence of the unknown-tool error and is appended with single-space punctuation (no double space) — independent of the 'did you mean' shortlist.")]
	public async Task RunAsync_ShouldEndWithDiscoveryHint_WhenToolIsUnknown() {
		// Arrange — registry advertises ONLY the executor names (BuildSuggestions strips them), so any
		// shortlist here can only come from the reflection catalog, exercising the hint append path.
		_registry.ToolNames.Returns(new[] { ClioRunTool.ToolName, ClioRunDestructiveTool.ToolName });
		_registry.TryGetTool("zzzzzzzzzzzzzzzz", out Arg.Any<McpServerTool>()).Returns(false);

		// Act
		CallToolResult result = await _sut.RunAsync("zzzzzzzzzzzzzzzz", null, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		string text = ErrorText(result);
		result.IsError.Should().BeTrue(because: "an unknown tool is a failure result");
		text.Should().Contain("get-tool-contract",
			because: "the discovery hint must always point the agent at the discovery tool");
		text.Should().Contain("compact index of every tool",
			because: "the error must name the cheap compact-index discovery path");
		text.Should().EndWith(ToolContractGetTool.DiscoveryHint,
			because: "the hint must always be the trailing sentence regardless of whether suggestions exist");
		text.Should().NotContain(". .",
			because: "an empty 'did you mean' fragment must not double-punctuate before the hint");
	}

	[Test]
	[Category("Unit")]
	[Description("The 'did you mean' suggestions never include the executor names clio-run / clio-run-destructive, so a near-miss never advises re-entering the executor.")]
	public async Task RunAsync_ShouldNeverSuggestExecutorNames_WhenBuildingSuggestions() {
		// Arrange — registry advertises only the executor names, so without the exclusion they would surface.
		_registry.ToolNames.Returns(new[] { ClioRunTool.ToolName, ClioRunDestructiveTool.ToolName });
		_registry.TryGetTool("clio-rin", out Arg.Any<McpServerTool>()).Returns(false);

		// Act
		CallToolResult result = await _sut.RunAsync("clio-rin", null, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		string text = ErrorText(result);
		result.IsError.Should().BeTrue(because: "an unknown tool is a failure result");
		text.Should().NotContain("clio-run-destructive",
			because: "suggesting the destructive executor would advise re-entering the executor instead of a real target");
		text.Should().NotMatchRegex(@"Did you mean:[^?]*\bclio-run\b",
			because: "suggesting clio-run itself would loop the agent back into the executor rather than a concrete tool");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects dispatching clio-run to itself, preventing unbounded recursive self-dispatch (DoS).")]
	public async Task RunAsync_ShouldRejectSelfDispatch_WhenTargetIsClioRun() {
		// Arrange

		// Act
		CallToolResult result = await _sut.RunAsync(ClioRunTool.ToolName, null, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(because: "clio-run must not be able to target itself");
		ErrorText(result).Should().Contain("self/cross-dispatch is not allowed",
			because: "the guard must explain that the executors cannot be dispatch targets");
		// A read-only registry lookup IS allowed before the guard (alias canonicalization runs first so
		// the guard always sees the final dispatch target); the refusal message above proves no dispatch.
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects clio-run-destructive cross-dispatching to clio-run (or vice versa), closing the recursion path.")]
	public async Task RunAsync_ShouldRejectCrossDispatch_WhenTargetIsTheOtherExecutor() {
		// Arrange

		// Act
		CallToolResult result = await _sut.RunAsync(ClioRunTool.ToolName, null, destructiveSurface: true, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(because: "one executor must not be able to target the other");
		ErrorText(result).Should().Contain("self/cross-dispatch is not allowed",
			because: "cross-dispatch between the executors is the same recursion hazard as self-dispatch");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a different-cased executor name (e.g. 'CLIO-RUN'), since the registry resolves names case-insensitively — a case mismatch must not slip past the recursion guard.")]
	public async Task RunAsync_ShouldRejectSelfDispatch_WhenTargetIsDifferentlyCasedExecutorName() {
		// Arrange
		string mixedCaseName = ClioRunTool.ToolName.ToUpperInvariant();

		// Act
		CallToolResult result = await _sut.RunAsync(mixedCaseName, null, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(
			because: "the registry matches names with OrdinalIgnoreCase, so a different-cased executor name is still self-dispatch and must be refused");
		ErrorText(result).Should().Contain("self/cross-dispatch is not allowed",
			because: "the guard must reject the case-variant alias for the same recursion reason");
		// Read-only registry lookups may precede the guard (alias canonicalization); the refusal above
		// proves the differently-cased executor name was still never dispatched.
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured error when 'command' is null or whitespace.")]
	public async Task RunAsync_ShouldReturnError_WhenCommandIsBlank() {
		// Arrange

		// Act
		CallToolResult result = await _sut.RunAsync("   ", null, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(because: "a blank command cannot be dispatched");
		ErrorText(result).Should().Contain("'command' is required",
			because: "the error must explain the missing command");
	}

	// The destructive-vs-safe REFUSAL was removed: field testing showed capable models loop forever on
	// the "use the other executor" redirect and never act. Both surfaces now run ANY tool directly;
	// host-level Destructive=true is the safety boundary. These tests pin that there is no refusal.
	[Test]
	[Category("Unit")]
	[Description("clio-run runs a destructive tool directly (no 'use the other executor' refusal) — eliminates the model-looping footgun.")]
	public async Task RunAsync_ShouldRunDestructiveTool_WhenInvokedOnSafeSurface() {
		// Arrange
		RegisterTool("echo-tool", BuildEchoTool(), destructive: true);
		JsonElement args = JsonDocument.Parse("{\"value\":\"hi\"}").RootElement;

		// Act
		CallToolResult result = await _sut.RunAsync("echo-tool", args, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().NotBe(true, because: "a destructive tool must execute, not be refused with a redirect");
		result.Content.OfType<TextContentBlock>().Should().Contain(
			block => block.Text.Contains("echo:hi", StringComparison.Ordinal),
			because: "the executor must reach the real tool regardless of the surface");
	}

	[Test]
	[Category("Unit")]
	[Description("clio-run-destructive runs a non-destructive tool directly (no refusal) — either executor works for any tool.")]
	public async Task RunAsync_ShouldRunNonDestructiveTool_WhenInvokedOnDestructiveSurface() {
		// Arrange
		RegisterTool("echo-tool", BuildEchoTool(), destructive: false);
		JsonElement args = JsonDocument.Parse("{\"value\":\"hi\"}").RootElement;

		// Act
		CallToolResult result = await _sut.RunAsync("echo-tool", args, destructiveSurface: true, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().NotBe(true, because: "either executor must run any tool without a cross-redirect");
		result.Content.OfType<TextContentBlock>().Should().Contain(
			block => block.Text.Contains("echo:hi", StringComparison.Ordinal),
			because: "the executor must reach the real tool regardless of the surface");
	}

	[Test]
	[Category("Unit")]
	[Description("Records the dispatched tool name and its resolved destructiveness into the result's out-of-band _meta, so a host that auto-allows clio-run still has an audit trail of the concrete (possibly destructive) tool it ran.")]
	public async Task RunAsync_ShouldEchoResolvedDestructivenessIntoMeta_WhenToolIsDispatched() {
		// Arrange
		RegisterTool("echo-tool", BuildEchoTool(), destructive: true);
		JsonElement args = JsonDocument.Parse("{\"value\":\"hi\"}").RootElement;

		// Act
		CallToolResult result = await _sut.RunAsync("echo-tool", args, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.Meta.Should().NotBeNull(because: "every dispatch must leave an audit record in _meta");
		System.Text.Json.Nodes.JsonNode auditNode = result.Meta!["clio-run"];
		auditNode.Should().NotBeNull(because: "the audit entry is stored under a dedicated 'clio-run' key");
		auditNode!["dispatchedTool"]!.GetValue<string>().Should().Be("echo-tool",
			because: "the audit trail must record which concrete tool clio-run actually executed");
		auditNode["destructive"]!.GetValue<bool>().Should().BeTrue(
			because: "the resolved tool's destructiveness (registry.IsDestructive) must be echoed for the host/audit trail");
	}

	[Test]
	[Category("Unit")]
	[Description("Bounds the clio-run retry-safe dispatch vector (the PRIMARY long-tail read vector) by the read-response deadline: when a retry-safe target's work outlives the deadline, clio-run returns a structured creatio-timeout envelope AND still attaches the clio-run dispatch audit to _meta (ENG-93373). Exercised via the internal readDeadline test seam so the timeout branch is covered without a 120 s wait.")]
	public async Task RunAsync_ShouldReturnStructuredTimeout_WhenRetrySafeToolExceedsDeadline() {
		// Arrange — a retry-safe target whose dispatch blocks well past the tiny deadline.
		RegisterTool("blocking-tool", BuildBlockingTool(), destructive: false);
		_registry.IsRetrySafe("blocking-tool").Returns(true);
		JsonElement args = JsonDocument.Parse("{\"value\":\"hi\"}").RootElement;

		// Act — a tiny deadline wins the race against the parked work (request token never cancelled, so
		// this is a timeout, not a caller cancellation).
		CallToolResult result = await _sut.RunAsync(
			"blocking-tool", args, destructiveSurface: false, CallContext(), CancellationToken.None,
			TimeSpan.FromMilliseconds(50));

		// Assert — the clio-run vector returned the bounded timeout envelope, not a hang.
		result.IsError.Should().BeTrue(
			because: "a timed-out retry-safe dispatch must be reported as an error result");
		JsonElement structured = result.StructuredContent!.Value;
		structured.GetProperty("error-class").GetString().Should().Be("creatio-timeout",
			because: "the clio-run vector must emit the same machine-readable timeout token as the other dispatch paths");
		structured.GetProperty("read-response-timed-out").GetBoolean().Should().BeTrue(
			because: "the read envelope must be distinguishable from a write in-progress envelope");
		structured.GetProperty("tool").GetString().Should().Be("blocking-tool",
			because: "the envelope must name the tool that timed out");
		// AttachDispatchAudit still runs on the timed-out result — the audit trail must not be lost on timeout.
		result.Meta.Should().NotBeNull(because: "even a timed-out dispatch must leave the clio-run audit record");
		result.Meta!["clio-run"]!["dispatchedTool"]!.GetValue<string>().Should().Be("blocking-tool",
			because: "the audit trail must record the concrete tool clio-run attempted, even on timeout");
	}

	[Test]
	[Category("Unit")]
	[Description("A retry-safe tool that completes within the deadline passes its real result through the clio-run read-deadline wrapper unchanged (ENG-93373 happy path).")]
	public async Task RunAsync_ShouldReturnResultUnchanged_WhenRetrySafeToolCompletesWithinDeadline() {
		// Arrange — a retry-safe fast tool; the deadline is generous so the work wins the race.
		RegisterTool("echo-tool", BuildEchoTool(), destructive: false);
		_registry.IsRetrySafe("echo-tool").Returns(true);
		JsonElement args = JsonDocument.Parse("{\"value\":\"hello\"}").RootElement;

		// Act
		CallToolResult result = await _sut.RunAsync(
			"echo-tool", args, destructiveSurface: false, CallContext(), CancellationToken.None,
			TimeSpan.FromSeconds(30));

		// Assert — the fast retry-safe result is returned unchanged, not a timeout envelope.
		result.IsError.Should().NotBe(true, because: "a fast retry-safe read must return its real payload, not a timeout");
		result.Content.OfType<TextContentBlock>().Should().Contain(
			block => block.Text.Contains("echo:hello", StringComparison.Ordinal),
			because: "the deadline wrapper must pass the real tool output through unchanged when work wins the race");
	}

	[Test]
	[Category("Unit")]
	[Description("A known safe tool is invoked through the SDK and its CallToolResult is returned to the caller.")]
	public async Task RunAsync_ShouldInvokeToolAndReturnResult_WhenSafeToolIsValid() {
		// Arrange
		RegisterTool("echo-tool", BuildEchoTool(), destructive: false);
		JsonElement args = JsonDocument.Parse("{\"value\":\"hello\"}").RootElement;

		// Act
		CallToolResult result = await _sut.RunAsync("echo-tool", args, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().NotBe(true, because: "a valid invocation must not be an error");
		result.Content.OfType<TextContentBlock>().Should().Contain(
			block => block.Text.Contains("echo:hello", StringComparison.Ordinal),
			because: "clio-run must reach the real tool method and return its output");
	}

	[Test]
	[Category("Unit")]
	[Description("The caller's ProgressToken is preserved onto the rebuilt child params so a dispatched tool (e.g. deploy-creatio) can emit notifications/progress; without this the token is dropped and typed stage events are silently lost.")]
	public async Task RunAsync_ShouldPreserveCallerProgressToken_WhenDispatchingTool() {
		// Arrange — a valid dispatch whose incoming call carries a ProgressToken (as a real MCP progress-tracked
		// call does). BuildChildParams rebuilds Params from Name+Arguments only, so the fix must copy the token.
		RegisterTool("echo-tool", BuildEchoTool(), destructive: false);
		JsonElement args = JsonDocument.Parse("{\"value\":\"hello\"}").RootElement;
		// RequestParams exposes ProgressToken as a read-only view over Meta["progressToken"], so seed the token
		// through _meta exactly as a progress-tracked MCP call arrives on the wire.
		const string tokenValue = "ring-progress-token-1";
		RequestContext<CallToolRequestParams> ctx = CallContext();
		ctx.Params = new CallToolRequestParams {
			Name = ClioRunTool.ToolName,
			Meta = new System.Text.Json.Nodes.JsonObject { ["progressToken"] = tokenValue }
		};

		// Act — dispatch through clio-run, which rewrites ctx.Params to the child tool's params.
		await _sut.RunAsync("echo-tool", args, destructiveSurface: false, ctx, CancellationToken.None);

		// Assert — the rewritten child params still carry the caller's ProgressToken (via preserved _meta), so
		// the dispatched tool's forwarder (which reads Params.ProgressToken) can send progress to the host.
		ctx.Params!.ProgressToken.Should().Be(new ProgressToken(tokenValue),
			because: "clio-run must carry the caller's ProgressToken onto the dispatched tool or progress is lost");
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces the real failure message in a structured Error result (not a thrown exception or a generic message) when the dispatched tool fails (field-test defect #3).")]
	public async Task RunAsync_ShouldReturnStructuredErrorWithRealMessage_WhenDispatchedToolThrows() {
		// Arrange
		RegisterTool("throwing-tool", BuildThrowingTool(), destructive: false);
		JsonElement args = JsonDocument.Parse("{\"value\":\"hello\"}").RootElement;

		// Act
		CallToolResult result = await _sut.RunAsync("throwing-tool", args, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(
			because: "a failing dispatched tool must yield a structured error result, not escape to the generic outer filter");
		ErrorText(result).Should().Contain(ThrowingToolType.FailureMessage,
			because: "the real (inner-most) failure message must be surfaced so the agent can self-correct instead of seeing a generic 'An error occurred' message");
	}

	[Test]
	[Category("Unit")]
	[Description("Redacts paths/URIs out of a RETURNED error result (IsError = true) — many clio tools catch internally and return a structured error rather than throwing, so the return path must be redacted too, not just the catch block.")]
	public async Task RunAsync_ShouldRedactSensitiveTokens_WhenDispatchedToolReturnsErrorResult() {
		// Arrange
		RegisterTool("error-result-tool", BuildErrorResultTool(), destructive: false);
		JsonElement args = JsonDocument.Parse("{\"value\":\"hi\"}").RootElement;

		// Act
		CallToolResult result = await _sut.RunAsync("error-result-tool", args, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(
			because: "a tool that returns a structured error result must keep IsError set after the audit pass");
		string text = ErrorText(result);
		text.Should().NotContain("https://target.creatio.com",
			because: "the target host/URI in a returned error result must be redacted, not just on the throw path");
		text.Should().NotContain("/Users/dev/secret/appsettings.json",
			because: "the absolute path in a returned error result must be redacted before reaching the transcript");
		text.Should().Contain("[redacted",
			because: "redaction replaces the sensitive tokens with a stable placeholder rather than dropping them");
	}

	[Test]
	[Category("Unit")]
	[Description("Scrubs a typed-POCO failure envelope (success:false + raw error) returned WITHOUT IsError: the SDK serialises it into the JSON text content, so the audit backstop must detect the failure signal and redact the host/URI/path even though the IsError gate alone would miss it.")]
	public async Task RunAsync_ShouldRedactFailureEnvelope_WhenToolReturnsPocoWithoutIsError() {
		// Arrange
		RegisterTool("poco-failure-tool", BuildPocoFailureTool(), destructive: false);
		JsonElement args = JsonDocument.Parse("{\"value\":\"hi\"}").RootElement;

		// Act
		CallToolResult result = await _sut.RunAsync("poco-failure-tool", args, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		string text = ErrorText(result);
		text.Should().NotContain("secret-host",
			because: "the raw target host in a typed-POCO failure envelope must be redacted by the backstop even without IsError set");
		text.Should().NotContain("/Users/dev/secret/appsettings.json",
			because: "the absolute path in the failure envelope's error field must be redacted before reaching the transcript");
		text.Should().Contain("[redacted",
			because: "the failure content is rewritten with a stable placeholder rather than dropped");
	}

	[Test]
	[Category("Unit")]
	[Description("Leaves a typed-POCO SUCCESS envelope untouched even when it carries URI/path-shaped data: the audit backstop only scrubs failure content and must never redact legitimate successful payloads.")]
	public async Task RunAsync_ShouldNotRedactSuccessEnvelope_WhenToolReturnsPocoWithUrl() {
		// Arrange
		RegisterTool("poco-success-tool", BuildPocoSuccessTool(), destructive: false);
		JsonElement args = JsonDocument.Parse("{\"value\":\"hi\"}").RootElement;

		// Act
		CallToolResult result = await _sut.RunAsync("poco-success-tool", args, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		string text = ErrorText(result);
		text.Should().Contain(PocoSuccessToolType.LegitimateUrl,
			because: "a successful payload's legitimate URL data must survive — the backstop only touches failure content");
		using JsonDocument successPayload = JsonDocument.Parse(text);
		successPayload.RootElement.GetProperty("FullPath").GetString().Should().Be(
			PocoSuccessToolType.LegitimatePath,
			because: "a successful discovery payload with a normal message must preserve its usable filesystem path exactly");
		text.Should().NotContain("[redacted",
			because: "no redaction placeholder may appear in a successful envelope");
	}

	[Test]
	[Category("Unit")]
	[Description("Scrubs a typed-POCO failure envelope whose sensitive detail is parked under non-'error' keys (message/detail): the result is classified a failure by success:false, so the backstop must redact every failure-bearing field, not only one literally named 'error'.")]
	public async Task RunAsync_ShouldRedactFailureFields_WhenFailureDetailIsUnderMessageOrDetail() {
		// Arrange
		RegisterTool("poco-message-failure-tool", BuildPocoMessageFailureTool(), destructive: false);
		JsonElement args = JsonDocument.Parse("{\"value\":\"hi\"}").RootElement;

		// Act
		CallToolResult result = await _sut.RunAsync("poco-message-failure-tool", args, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		string text = ErrorText(result);
		text.Should().NotContain("prod-db.internal:1433",
			because: "a scheme-less host:port carried under the 'message' field of a failure envelope must be redacted");
		text.Should().NotContain("/Library/Logs/clio/trace.log",
			because: "an absolute path carried under the 'detail' field of a failure envelope must be redacted");
		text.Should().Contain("[redacted",
			because: "the failure fields are rewritten with stable placeholders rather than passed through");
	}

	[Test]
	[Category("Unit")]
	[Description("Scrubs a NESTED failure-bearing field (reason inside a child object) via the StructuredContent recursion path, proving the broadened predicate is wired into RedactStructuredErrorFields and not only the top-level redaction.")]
	public async Task RunAsync_ShouldRedactNestedFailureField_WhenDetailIsUnderNestedReasonKey() {
		// Arrange
		RegisterTool("poco-nested-failure-tool", BuildPocoNestedFailureTool(), destructive: false);
		JsonElement args = JsonDocument.Parse("{\"value\":\"hi\"}").RootElement;

		// Act
		CallToolResult result = await _sut.RunAsync("poco-nested-failure-tool", args, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		string text = ErrorText(result);
		text.Should().NotContain("10.0.0.5:1433",
			because: "an ip:port nested under a non-error 'reason' key must be reached by the structured-content recursion and redacted");
		string structured = result.StructuredContent?.GetRawText() ?? string.Empty;
		structured.Should().NotContain("10.0.0.5:1433",
			because: "the StructuredContent graph must be rewritten too so the host does not read the raw value from the structured payload");
		(text + structured).Should().Contain("[redacted",
			because: "the nested failure field is rewritten with a stable placeholder");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a non-object 'args' value with a structured error before dispatch.")]
	public async Task RunAsync_ShouldReturnError_WhenArgsIsNotObject() {
		// Arrange
		RegisterTool("echo-tool", BuildEchoTool(), destructive: false);
		JsonElement args = JsonDocument.Parse("\"not-an-object\"").RootElement;

		// Act
		CallToolResult result = await _sut.RunAsync("echo-tool", args, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(because: "args must be a JSON object");
		ErrorText(result).Should().Contain("must be a JSON object",
			because: "the error must explain the expected args shape");
	}

	[Test]
	[Category("Unit")]
	[Description("ClioRunTool delegates to the executor with destructiveSurface=false.")]
	public async Task ClioRunTool_ShouldDelegateToExecutor_WithSafeSurface() {
		// Arrange
		IClioRunExecutor executor = Substitute.For<IClioRunExecutor>();
		CallToolResult expected = new() { Content = [] };
		executor.RunAsync("get-thing", Arg.Any<JsonElement?>(), false, Arg.Any<RequestContext<CallToolRequestParams>>(), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<CallToolResult>(expected));
		ClioRunTool tool = new(executor);

		// Act
		CallToolResult result = await tool.Run(CallContext(), "get-thing");

		// Assert
		result.Should().BeSameAs(expected, because: "the tool returns the executor result unchanged");
		await executor.Received(1).RunAsync("get-thing", Arg.Any<JsonElement?>(), false, Arg.Any<RequestContext<CallToolRequestParams>>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Category("Unit")]
	[Description("ClioRunDestructiveTool delegates to the executor with destructiveSurface=true.")]
	public async Task ClioRunDestructiveTool_ShouldDelegateToExecutor_WithDestructiveSurface() {
		// Arrange
		IClioRunExecutor executor = Substitute.For<IClioRunExecutor>();
		CallToolResult expected = new() { Content = [] };
		executor.RunAsync("delete-thing", Arg.Any<JsonElement?>(), true, Arg.Any<RequestContext<CallToolRequestParams>>(), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<CallToolResult>(expected));
		ClioRunDestructiveTool tool = new(executor);

		// Act
		CallToolResult result = await tool.Run(CallContext(), "delete-thing");

		// Assert
		result.Should().BeSameAs(expected, because: "the tool returns the executor result unchanged");
		await executor.Received(1).RunAsync("delete-thing", Arg.Any<JsonElement?>(), true, Arg.Any<RequestContext<CallToolRequestParams>>(), Arg.Any<CancellationToken>());
	}

	// --- Wrapped-form tolerance ----------------------------------------------------------------------
	// clio-run / clio-run-destructive take TWO top-level params (command + args), but most clio tools take
	// ONE record param named `args`, so the SDK wraps everything under `args`. An agent habituated to the
	// wrapper sends {"args":{"command":"X", ...}}, leaving top-level `command` null. The executor recovers
	// the real command/args so BOTH call shapes work without breaking the normal top-level form.

	[Test]
	[Category("Unit")]
	[Description("NORMAL top-level form {\"command\":\"echo-tool\",\"args\":{...}} is dispatched unchanged (recovery is a no-op when command is present) — regression guard.")]
	public async Task RunAsync_ShouldDispatchUnchanged_WhenCommandIsProvidedTopLevel() {
		// Arrange
		RegisterTool("echo-tool", BuildEchoTool(), destructive: false);
		JsonElement args = JsonDocument.Parse("{\"value\":\"hi\"}").RootElement;

		// Act
		CallToolResult result = await _sut.RunAsync("echo-tool", args, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().NotBe(true, because: "the normal top-level form must keep dispatching unchanged");
		result.Content.OfType<TextContentBlock>().Should().Contain(
			block => block.Text.Contains("echo:hi", StringComparison.Ordinal),
			because: "the top-level command/args pair must reach the real tool with its args intact");
	}

	[Test]
	[Category("Unit")]
	[Description("WRAPPED-with-inner-args {\"args\":{\"command\":\"echo-tool\",\"args\":{\"value\":\"hi\"}}} recovers command and the inner args object, then dispatches.")]
	public async Task RunAsync_ShouldRecoverCommandAndInnerArgs_WhenCalledWithWrappedInnerArgsShape() {
		// Arrange
		RegisterTool("echo-tool", BuildEchoTool(), destructive: false);
		JsonElement wrapped = JsonDocument.Parse("{\"command\":\"echo-tool\",\"args\":{\"value\":\"hi\"}}").RootElement;

		// Act — top-level command is null; the wrapped object lives in `args` (the SDK wrapper shape).
		CallToolResult result = await _sut.RunAsync(null, wrapped, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().NotBe(true, because: "the wrapped-with-inner-args form must be recovered and dispatched");
		result.Content.OfType<TextContentBlock>().Should().Contain(
			block => block.Text.Contains("echo:hi", StringComparison.Ordinal),
			because: "the recovered command must run with the inner args object as its arguments");
	}

	[Test]
	[Category("Unit")]
	[Description("WRAPPED-flat {\"args\":{\"command\":\"echo-tool\",\"value\":\"hi\"}} recovers command and strips the 'command' key so the remaining keys become the flat target args, then dispatches.")]
	public async Task RunAsync_ShouldRecoverCommandAndStripCommandKey_WhenCalledWithWrappedFlatShape() {
		// Arrange
		RegisterTool("echo-tool", BuildEchoTool(), destructive: false);
		JsonElement wrapped = JsonDocument.Parse("{\"command\":\"echo-tool\",\"value\":\"hi\"}").RootElement;

		// Act — top-level command is null; the flat-wrapped object (command + target params) is in `args`.
		CallToolResult result = await _sut.RunAsync(null, wrapped, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().NotBe(true, because: "the wrapped-flat form must be recovered and dispatched after stripping the command key");
		result.Content.OfType<TextContentBlock>().Should().Contain(
			block => block.Text.Contains("echo:hi", StringComparison.Ordinal),
			because: "after stripping 'command' the remaining 'value' key must bind as the tool's argument");
	}

	[Test]
	[Category("Unit")]
	[Description("WRAPPED self-dispatch {\"args\":{\"command\":\"clio-run\"}} is STILL refused — recovery resolves the real command BEFORE the recursion guard, so the recovered name hits the guard.")]
	public async Task RunAsync_ShouldRejectSelfDispatch_WhenWrappedFormTargetsClioRun() {
		// Arrange
		JsonElement wrapped = JsonDocument.Parse("{\"command\":\"clio-run\"}").RootElement;

		// Act
		CallToolResult result = await _sut.RunAsync(null, wrapped, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(because: "a wrapped clio-run target must still be caught by the recursion guard after recovery");
		ErrorText(result).Should().Contain("self/cross-dispatch is not allowed",
			because: "recovery runs before the guard, so the recovered command 'clio-run' must trip the same self-dispatch refusal");
		// Read-only registry lookups may precede the guard (alias canonicalization); the refusal above
		// proves the recovered executor name was still never dispatched.
	}

	[Test]
	[Category("Unit")]
	[Description("No command anywhere (empty wrapper {\"args\":{}}) returns a structured Error naming the correct call shape and dispatches nothing.")]
	public async Task RunAsync_ShouldReturnShapeError_WhenWrapperHasNoCommand() {
		// Arrange
		JsonElement wrapped = JsonDocument.Parse("{}").RootElement;

		// Act
		CallToolResult result = await _sut.RunAsync(null, wrapped, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(because: "a call with no recoverable command cannot be dispatched");
		string text = ErrorText(result);
		text.Should().Contain("'command' is required",
			because: "the error must explain the missing command");
		text.Should().Contain("{\"command\":\"<tool>\",\"args\":{...}}",
			because: "the error must name the correct top-level call shape so the agent can fix its call");
		_registry.DidNotReceive().TryGetTool(Arg.Any<string>(), out Arg.Any<McpServerTool>());
	}

	[Test]
	[Category("Unit")]
	[Description("A primitive (non-object) args value with a null command is NOT a recoverable wrapper — returns the structured shape Error and dispatches nothing.")]
	public async Task RunAsync_ShouldReturnShapeError_WhenCommandIsNullAndArgsIsPrimitive() {
		// Arrange
		JsonElement primitive = JsonDocument.Parse("\"echo-tool\"").RootElement;

		// Act
		CallToolResult result = await _sut.RunAsync(null, primitive, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(because: "a primitive args value cannot carry a recoverable command property");
		string text = ErrorText(result);
		text.Should().Contain("'command' is required",
			because: "with no object wrapper there is no command to recover, so the missing-command error must surface");
		text.Should().Contain("{\"command\":\"<tool>\",\"args\":{...}}",
			because: "the error must still name the correct call shape for a primitive args value");
		_registry.DidNotReceive().TryGetTool(Arg.Any<string>(), out Arg.Any<McpServerTool>());
	}

	[Test]
	[Category("Unit")]
	[Description("Null command and null args returns the structured shape Error and dispatches nothing (the bare {} call with no args at all).")]
	public async Task RunAsync_ShouldReturnShapeError_WhenCommandAndArgsAreBothNull() {
		// Arrange

		// Act
		CallToolResult result = await _sut.RunAsync(null, null, destructiveSurface: false, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().BeTrue(because: "with neither a command nor an args wrapper there is nothing to dispatch");
		ErrorText(result).Should().Contain("'command' is required",
			because: "the missing-command error must surface when nothing is provided at all");
	}

	[Test]
	[Category("Unit")]
	[Description("The shared executor path also serves clio-run-destructive: a wrapped-with-inner-args call recovers and dispatches through the same RunAsync used by the destructive surface.")]
	public async Task RunAsync_ShouldRecoverWrappedForm_WhenInvokedOnDestructiveSurface() {
		// Arrange
		RegisterTool("echo-tool", BuildEchoTool(), destructive: true);
		JsonElement wrapped = JsonDocument.Parse("{\"command\":\"echo-tool\",\"args\":{\"value\":\"hi\"}}").RootElement;

		// Act — destructiveSurface=true mirrors clio-run-destructive routing through the shared executor.
		CallToolResult result = await _sut.RunAsync(null, wrapped, destructiveSurface: true, CallContext(), CancellationToken.None);

		// Assert
		result.IsError.Should().NotBe(true,
			because: "the wrapped-form recovery lives in the shared executor, so the destructive surface gets it too");
		result.Content.OfType<TextContentBlock>().Should().Contain(
			block => block.Text.Contains("echo:hi", StringComparison.Ordinal),
			because: "clio-run-destructive must recover and dispatch the wrapped call just like clio-run");
	}

	[Test]
	[Category("Unit")]
	[Description("clio-run advertises Destructive=true (host-gated) and never ReadOnly, since it can run write/destructive tools.")]
	public void ClioRunTool_ShouldExposeDestructiveNonReadOnlyMetadata() {
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ClioRunTool)
			.GetMethod(nameof(ClioRunTool.Run))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Act

		// Assert
		attribute.Name.Should().Be(ClioRunTool.ToolName, because: "the tool uses its stable name constant");
		attribute.ReadOnly.Should().BeFalse(because: "clio-run must never be ReadOnly/auto-approved");
		attribute.Destructive.Should().BeTrue(
			because: "clio-run can dispatch destructive tools, so the host must gate it (host-level safety replaces the removed executor split)");
	}

	[Test]
	[Category("Unit")]
	[Description("clio-run-destructive advertises a destructive MCP tool so hosts can prompt for confirmation.")]
	public void ClioRunDestructiveTool_ShouldExposeDestructiveMetadata() {
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ClioRunDestructiveTool)
			.GetMethod(nameof(ClioRunDestructiveTool.Run))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Act

		// Assert
		attribute.Name.Should().Be(ClioRunDestructiveTool.ToolName, because: "the tool uses its stable name constant");
		attribute.Destructive.Should().BeTrue(because: "the destructive surface must flag Destructive=true");
	}

	// --- Schema guard: the emitted input schema must declare args as "type":"object" -------------------
	// ENG-92653: without "type":"object" Claude Code (and other clients that rely on the schema for
	// serialization decisions) drops or stringifies the args payload, making the entire hidden-tool
	// surface unreachable. This test builds the tool through the SDK (the same path BindingsModule uses)
	// and inspects the actual emitted schema — a regression guard against future type-widening.

	[Test]
	[Category("Unit")]
	[Description("clio-run input schema declares args as type=object so MCP clients serialize the payload correctly (ENG-92653 regression guard).")]
	public void ClioRunTool_ShouldDeclareArgsAsObjectType_InEmittedInputSchema() {
		// Arrange — instance method requires a target; pass a stub executor since the schema is derived
		// from the method signature, not from the runtime instance state.
		McpServerTool tool = McpServerTool.Create(
			typeof(ClioRunTool).GetMethod(nameof(ClioRunTool.Run))!,
			target: new ClioRunTool(Substitute.For<IClioRunExecutor>()),
			new McpServerToolCreateOptions { SerializerOptions = BindingsModule.CreateMcpSerializerOptions() });

		// Act
		JsonElement schema = tool.ProtocolTool.InputSchema;
		JsonElement argsProperty = schema.GetProperty("properties").GetProperty("args");

		// Assert — nullable Dictionary<string, JsonElement>? emits type as ["object","null"] (an array)
		// or "object" (a string) depending on the SDK version; either shape includes "object".
		argsProperty.TryGetProperty("type", out JsonElement typeElement).Should().BeTrue(
			because: "the args property must have an explicit type declaration so MCP clients know to serialize it as a JSON object (ENG-92653)");
		AssertTypeIncludesObject(typeElement);
	}

	[Test]
	[Category("Unit")]
	[Description("clio-run-destructive input schema declares args as type=object (same fix as clio-run, ENG-92653 regression guard).")]
	public void ClioRunDestructiveTool_ShouldDeclareArgsAsObjectType_InEmittedInputSchema() {
		// Arrange
		McpServerTool tool = McpServerTool.Create(
			typeof(ClioRunDestructiveTool).GetMethod(nameof(ClioRunDestructiveTool.Run))!,
			target: new ClioRunDestructiveTool(Substitute.For<IClioRunExecutor>()),
			new McpServerToolCreateOptions { SerializerOptions = BindingsModule.CreateMcpSerializerOptions() });

		// Act
		JsonElement schema = tool.ProtocolTool.InputSchema;
		JsonElement argsProperty = schema.GetProperty("properties").GetProperty("args");

		// Assert
		argsProperty.TryGetProperty("type", out JsonElement typeElement).Should().BeTrue(
			because: "the args property must have an explicit type declaration (ENG-92653)");
		AssertTypeIncludesObject(typeElement);
	}

	private static void AssertTypeIncludesObject(JsonElement typeElement) {
		if (typeElement.ValueKind == JsonValueKind.String) {
			typeElement.GetString().Should().Be("object",
				because: "args must be declared as type=object for MCP client serialization compatibility");
		} else if (typeElement.ValueKind == JsonValueKind.Array) {
			typeElement.EnumerateArray()
				.Select(item => item.GetString())
				.Should().Contain("object",
					because: "args type array must include 'object' so MCP clients serialize the payload correctly (ENG-92653)");
		} else {
			typeElement.ValueKind.Should().BeOneOf([JsonValueKind.String, JsonValueKind.Array],
				because: "JSON Schema type must be a string or an array of strings");
		}
	}

	// --- Dictionary→JsonElement conversion at the tool method level ------------------------------------
	// The tool methods accept Dictionary<string, JsonElement>? (to emit the correct schema) and convert to
	// JsonElement? before passing to the executor. These tests verify the conversion, covering the path the
	// delegation tests (null args) do not reach.

	[Test]
	[Category("Unit")]
	[Description("ClioRunTool converts a non-null Dictionary args to a JsonElement object and passes it through to the executor.")]
	public async Task ClioRunTool_ShouldConvertDictionaryArgs_WhenArgsAreProvided() {
		// Arrange
		IClioRunExecutor executor = Substitute.For<IClioRunExecutor>();
		CallToolResult expected = new() { Content = [] };
		JsonElement? capturedArgs = null;
		executor.RunAsync(
			"echo-tool",
			Arg.Any<JsonElement?>(),
			false,
			Arg.Any<RequestContext<CallToolRequestParams>>(),
			Arg.Any<CancellationToken>())
			.Returns(call => {
				capturedArgs = call.ArgAt<JsonElement?>(1);
				return new ValueTask<CallToolResult>(expected);
			});
		ClioRunTool tool = new(executor);

		// Act
		Dictionary<string, JsonElement> args = new() {
			["value"] = JsonDocument.Parse("\"hello\"").RootElement
		};
		CallToolResult result = await tool.Run(CallContext(), "echo-tool", args);

		// Assert
		result.Should().BeSameAs(expected,
			because: "the tool must forward the converted args to the executor and return its result");
		capturedArgs.Should().NotBeNull(because: "a non-null Dictionary must convert to a non-null JsonElement");
		capturedArgs!.Value.ValueKind.Should().Be(JsonValueKind.Object,
			because: "the converted JsonElement must be a JSON object");
		capturedArgs.Value.GetProperty("value").GetString().Should().Be("hello",
			because: "the Dictionary entries must survive the conversion intact");
	}

	[Test]
	[Category("Unit")]
	[Description("ClioRunTool passes null to executor when Dictionary args is null.")]
	public async Task ClioRunTool_ShouldPassNullArgs_WhenDictionaryIsNull() {
		// Arrange
		IClioRunExecutor executor = Substitute.For<IClioRunExecutor>();
		CallToolResult expected = new() { Content = [] };
		executor.RunAsync(
			"echo-tool",
			Arg.Is<JsonElement?>(el => !el.HasValue),
			false,
			Arg.Any<RequestContext<CallToolRequestParams>>(),
			Arg.Any<CancellationToken>())
			.Returns(new ValueTask<CallToolResult>(expected));
		ClioRunTool tool = new(executor);

		// Act
		CallToolResult result = await tool.Run(CallContext(), "echo-tool", null);

		// Assert
		result.Should().BeSameAs(expected,
			because: "null Dictionary must convert to null JsonElement for the executor");
	}

	[Test]
	[Category("Unit")]
	[Description("ClioRunTool preserves the wrapped call shape when the Dictionary carries an inner command/args, so the executor's RecoverWrappedCall still sees the original object (ENG-92653).")]
	public async Task ClioRunTool_ShouldPreserveWrappedShape_WhenDictionaryContainsCommandAndArgs() {
		// Arrange — the SDK binds the wrapped shape {"args":{"command":"X","args":{...}}} entirely under
		// the `args` Dictionary with `command` left null. The Dictionary→JsonElement conversion must keep
		// that object intact so ClioRunExecutor.RecoverWrappedCall can extract the real command/args.
		IClioRunExecutor executor = Substitute.For<IClioRunExecutor>();
		CallToolResult expected = new() { Content = [] };
		JsonElement? capturedArgs = null;
		executor.RunAsync(
			null,
			Arg.Any<JsonElement?>(),
			false,
			Arg.Any<RequestContext<CallToolRequestParams>>(),
			Arg.Any<CancellationToken>())
			.Returns(call => {
				capturedArgs = call.ArgAt<JsonElement?>(1);
				return new ValueTask<CallToolResult>(expected);
			});
		ClioRunTool tool = new(executor);

		// Act — command is null (wrapped shape); the real command lives inside the args Dictionary.
		Dictionary<string, JsonElement> wrapped = new() {
			["command"] = JsonDocument.Parse("\"echo-tool\"").RootElement,
			["args"] = JsonDocument.Parse("{\"value\":\"hello\"}").RootElement
		};
		CallToolResult result = await tool.Run(CallContext(), null, wrapped);

		// Assert
		result.Should().BeSameAs(expected,
			because: "the tool must forward the converted wrapped args to the executor and return its result");
		capturedArgs.Should().NotBeNull(because: "a non-null wrapped Dictionary must convert to a non-null JsonElement");
		capturedArgs!.Value.ValueKind.Should().Be(JsonValueKind.Object,
			because: "the converted wrapped payload must remain a JSON object for RecoverWrappedCall to parse");
		capturedArgs.Value.GetProperty("command").GetString().Should().Be("echo-tool",
			because: "the inner command must survive the conversion so the executor can recover the real target tool");
		capturedArgs.Value.GetProperty("args").GetProperty("value").GetString().Should().Be("hello",
			because: "the inner target args must survive the conversion intact");
	}

	[Test]
	[Category("Unit")]
	[Description("ClioRunDestructiveTool converts a non-null Dictionary args to a JsonElement object and passes it through to the executor on the destructive surface (ENG-92653).")]
	public async Task ClioRunDestructiveTool_ShouldConvertDictionaryArgs_WhenArgsAreProvided() {
		// Arrange — the destructive alias runs the same DictionaryToElement conversion; cover its flat path.
		IClioRunExecutor executor = Substitute.For<IClioRunExecutor>();
		CallToolResult expected = new() { Content = [] };
		JsonElement? capturedArgs = null;
		executor.RunAsync(
			"sync-schemas",
			Arg.Any<JsonElement?>(),
			true,
			Arg.Any<RequestContext<CallToolRequestParams>>(),
			Arg.Any<CancellationToken>())
			.Returns(call => {
				capturedArgs = call.ArgAt<JsonElement?>(1);
				return new ValueTask<CallToolResult>(expected);
			});
		ClioRunDestructiveTool tool = new(executor);

		// Act
		Dictionary<string, JsonElement> args = new() {
			["value"] = JsonDocument.Parse("\"hello\"").RootElement
		};
		CallToolResult result = await tool.Run(CallContext(), "sync-schemas", args);

		// Assert
		result.Should().BeSameAs(expected,
			because: "the destructive tool must forward the converted args to the executor and return its result");
		capturedArgs.Should().NotBeNull(because: "a non-null Dictionary must convert to a non-null JsonElement");
		capturedArgs!.Value.ValueKind.Should().Be(JsonValueKind.Object,
			because: "the converted JsonElement must be a JSON object");
		capturedArgs.Value.GetProperty("value").GetString().Should().Be("hello",
			because: "the Dictionary entries must survive the conversion intact on the destructive surface");
	}

	[Test]
	[Category("Unit")]
	[Description("ClioRunDestructiveTool passes null to the executor when the Dictionary args is null (ENG-92653).")]
	public async Task ClioRunDestructiveTool_ShouldPassNullArgs_WhenDictionaryIsNull() {
		// Arrange
		IClioRunExecutor executor = Substitute.For<IClioRunExecutor>();
		CallToolResult expected = new() { Content = [] };
		executor.RunAsync(
			"sync-schemas",
			Arg.Is<JsonElement?>(el => !el.HasValue),
			true,
			Arg.Any<RequestContext<CallToolRequestParams>>(),
			Arg.Any<CancellationToken>())
			.Returns(new ValueTask<CallToolResult>(expected));
		ClioRunDestructiveTool tool = new(executor);

		// Act
		CallToolResult result = await tool.Run(CallContext(), "sync-schemas", null);

		// Assert
		result.Should().BeSameAs(expected,
			because: "null Dictionary must convert to null JsonElement for the executor on the destructive surface");
	}
}
