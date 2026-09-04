using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for MCP tool argument shape handling.
/// Verifies that the <see cref="Clio.Command.McpServer.McpToolErrorFilter"/> normalizes the canonical
/// FLAT argument shape into the wrapped shape over a real MCP transport (ENG-95885), and still refuses
/// the shapes that cannot be normalized without guessing — unknown-only, ambiguous hybrid, and an empty
/// payload on a tool that has not declared a no-arguments operation.
/// </summary>
/// <remarks>
/// These cases carry the load-bearing assumption of the whole ENG-95885 approach: a unit test can only
/// prove the filter MUTATED the request, never that the rewritten <c>Arguments</c> survive SDK argument
/// binding and reach the tool method. Only the real server process can prove that.
/// </remarks>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("error-filter")]
[Parallelizable(ParallelScope.Self)]
public sealed class McpToolErrorFilterE2ETests : McpContractFixtureBase
{
	// Text fragment unique to the pre-ENG-95885 wrapper hint. Its ABSENCE is what proves normalization
	// replaced the hint-only behavior; its presence would mean the flat payload never reached the tool.
	private const string WrapperHintFragment = "expects arguments wrapped inside";

	[Test]
	[AllureTag("list-apps")]
	[AllureName("A canonical flat payload reaches SDK argument binding instead of returning a wrapper hint")]
	[Description("ENG-95885 binding proof: sends flat arguments (environment-name at top level) to a single-composite-args tool and verifies the call reaches the tool method with the argument BOUND — the response is the environment-resolution outcome, not the wrapper hint.")]
	public async Task FlatArgs_ShouldReachBoundToolMethod_WhenEveryKeyIsCanonical()
	{
		// Arrange
		await using ArrangeContext arrangeContext = Arrange();
		string toolName = ApplicationGetListTool.ApplicationGetListToolName;
		string missingEnvironment = $"missing-env-{Guid.NewGuid():N}";

		// Act — send flat args WITHOUT the "args" wrapper
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> {
				["environment-name"] = missingEnvironment
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		string responseText = string.Join("\n",
			callResult.Content.OfType<TextContentBlock>().Select(b => b.Text));

		responseText.Should().NotContain(WrapperHintFragment,
			because: "a canonical flat payload is normalized into the wrapped shape, so the caller never sees the wrapper hint");

		responseText.Should().Contain(missingEnvironment,
			because: "the rewritten arguments must survive SDK binding and reach the tool method, which then "
				+ "reports on the environment name it was actually given — this is the proof that an "
				+ "in-filter Arguments rewrite reaches argument binding");
	}

	[Test]
	[AllureTag("list-apps")]
	[AllureName("A canonical flat payload and the equivalent wrapped payload produce the same outcome")]
	[Description("ENG-95885: sends the same argument twice — once flat, once wrapped — and verifies both calls reach the same tool behavior, so the flat shape is a true alias of the wrapped shape rather than a differently-handled path.")]
	public async Task FlatArgs_ShouldMatchWrappedArgsOutcome_WhenSameArgumentIsSent()
	{
		// Arrange
		await using ArrangeContext arrangeContext = Arrange();
		string toolName = ApplicationGetListTool.ApplicationGetListToolName;
		string missingEnvironment = $"missing-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult flatResult = await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> {
				["environment-name"] = missingEnvironment
			},
			arrangeContext.CancellationTokenSource.Token);
		CallToolResult wrappedResult = await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = missingEnvironment
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		string flatText = string.Join("\n", flatResult.Content.OfType<TextContentBlock>().Select(b => b.Text));
		string wrappedText = string.Join("\n", wrappedResult.Content.OfType<TextContentBlock>().Select(b => b.Text));

		flatResult.IsError.Should().Be(wrappedResult.IsError,
			because: "the two accepted shapes must classify identically");
		flatText.Should().Be(wrappedText,
			because: "normalization makes the flat shape an exact alias of the wrapped shape; any divergence "
				+ "would mean the two shapes take different code paths inside the tool");
	}

	[Test]
	[AllureTag("get-guidance")]
	[AllureName("A canonical flat payload is accepted for an args record that carries an overflow bag")]
	[Description("ENG-95885: sends a flat get-guidance call whose single key is a canonical wire property and verifies the call is normalized and executed rather than answered with the wrapper hint.")]
	public async Task FlatArgs_ShouldBeNormalized_WhenArgsTypeAlsoHasExtensionDataBucket()
	{
		// Arrange
		await using ArrangeContext arrangeContext = Arrange();
		string toolName = GuidanceGetTool.ToolName;

		// Act — send flat args WITHOUT the "args" wrapper
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> {
				["name"] = "routing"
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		string responseText = string.Join("\n",
			callResult.Content.OfType<TextContentBlock>().Select(b => b.Text));

		responseText.Should().NotContain(WrapperHintFragment,
			because: "the flat key 'name' is a canonical wire property, so the payload is normalized instead of refused");
		responseText.Should().NotContain("Unknown args",
			because: "a canonical flat key must not be reported as an unknown argument after normalization");
	}

	[Test]
	[AllureTag("list-apps")]
	[AllureName("Correctly wrapped arguments still pass through untouched")]
	[Description("Sends correctly wrapped arguments to a composite-args tool and verifies the call proceeds past the filter to normal execution, byte-compatible with pre-ENG-95885 behavior.")]
	public async Task WrappedArgs_ShouldNotTriggerHint_WhenCompositeParameterIsPresent()
	{
		// Arrange
		await using ArrangeContext arrangeContext = Arrange();
		string toolName = ApplicationGetListTool.ApplicationGetListToolName;

		// Act — send correctly wrapped args
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = $"missing-env-{Guid.NewGuid():N}"
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert — should reach past the filter to actual execution (which fails on missing env, not on wrapping)
		string responseText = string.Join("\n",
			callResult.Content.OfType<TextContentBlock>().Select(b => b.Text));

		responseText.Should().NotContain(WrapperHintFragment,
			because: "correctly wrapped arguments should pass through the filter without a wrapper hint");
	}

	[Test]
	[AllureTag("list-apps")]
	[AllureName("An unknown-only flat payload is refused with the canonical field list, never a default success")]
	[Description("ENG-95885 regression boundary: an args record with no [JsonExtensionData] overflow bag must never be reached with a defaulted instance, because the tool would then answer a validation mistake with a plausible list/default SUCCESS.")]
	public async Task UnknownOnlyFlatArgs_ShouldBeRefused_WhenArgsRecordHasNoOverflowBucket()
	{
		// Arrange
		await using ArrangeContext arrangeContext = Arrange();
		string toolName = ApplicationGetListTool.ApplicationGetListToolName;

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> {
				["totally-unknown-key"] = "value"
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().BeTrue(
			because: "an unknown-only payload must fail fast instead of materializing a defaulted args record");

		string responseText = string.Join("\n",
			callResult.Content.OfType<TextContentBlock>().Select(b => b.Text));

		responseText.Should().Contain("totally-unknown-key",
			because: "the error must name the offending key so the agent can self-correct on the first attempt");
		responseText.Should().Contain("environment-name",
			because: "the error must list the canonical field names the tool actually accepts");
	}

	[Test]
	[AllureTag("list-apps")]
	[AllureName("A hybrid wrapped-plus-flat payload is refused as ambiguous")]
	[Description("ENG-95885: a payload carrying both an \"args\" object and a conflicting top-level key has no defensible precedence, so it must be refused rather than silently resolved to one of the two values.")]
	public async Task HybridArgs_ShouldBeRefusedAsAmbiguous_WhenWrapperAndTopLevelKeyConflict()
	{
		// Arrange
		await using ArrangeContext arrangeContext = Arrange();
		string toolName = ApplicationGetListTool.ApplicationGetListToolName;
		string wrappedEnvironment = $"wrapped-env-{Guid.NewGuid():N}";
		string flatEnvironment = $"flat-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = wrappedEnvironment
				},
				["environment-name"] = flatEnvironment
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().BeTrue(
			because: "an ambiguous shape must be refused, with no silent precedence in either direction");

		string responseText = string.Join("\n",
			callResult.Content.OfType<TextContentBlock>().Select(b => b.Text));

		responseText.Should().Contain("ambiguous",
			because: "the error must name the ambiguity rather than reporting a downstream environment failure");
		responseText.Should().NotContain(wrappedEnvironment,
			because: "neither candidate value may be silently chosen and acted upon");
		responseText.Should().NotContain(flatEnvironment,
			because: "neither candidate value may be silently chosen and acted upon");
	}

	[Test]
	[AllureTag("get-component-info")]
	[AllureName("get-component-info called flat with a non-canonical field name is answered with the canonical field list, not the component catalog")]
	[Description("ENG-95885 R2: 'component-name' is not a field of get-component-info (the field is 'component-type'). The first attempt must name the valid fields — never fall through to a catalog or defaulted response the agent would read as an answer.")]
	public async Task NonCanonicalFieldName_ShouldReturnCanonicalFieldList_WhenGetComponentInfoIsCalledFlat()
	{
		// Arrange
		await using ArrangeContext arrangeContext = Arrange();

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ComponentInfoTool.ToolName,
			new Dictionary<string, object?> {
				["component-name"] = "crt.Input"
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().BeTrue(
			because: "an all-unknown payload must fail fast on the first attempt");

		string responseText = string.Join("\n",
			callResult.Content.OfType<TextContentBlock>().Select(b => b.Text));

		responseText.Should().Contain("component-name",
			because: "the offending key must be named so the agent knows what to change");
		responseText.Should().Contain("component-type",
			because: "the canonical field the caller almost certainly meant must be offered");
		responseText.Should().NotContain("crt.Gallery",
			because: "a catalog listing would mean the tool ran with a defaulted args record — the exact "
				+ "plausible-but-wrong success this change exists to prevent");
	}

	[Test]
	[AllureTag("get-tool-contract")]
	[AllureName("get-tool-contract called flat with a single name key returns that tool's contract, not the full index")]
	[Description("ENG-95885: a flat {\"name\":\"<tool>\"} call used to bind args to null, fall through to the no-tool-names branch, and hand back the entire tool INDEX as a plausible success. It must resolve the named contract instead.")]
	public async Task FlatNameOnlyCall_ShouldResolveNamedContract_WhenGetToolContractIsCalledFlat()
	{
		// Arrange
		await using ArrangeContext arrangeContext = Arrange();
		const string requestedTool = "get-guidance";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["name"] = requestedTool
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		string responseText = string.Join("\n",
			callResult.Content.OfType<TextContentBlock>().Select(b => b.Text));

		callResult.IsError.Should().NotBeTrue(
			because: "a flat name-only call is a legitimate discovery call");
		responseText.Should().Contain(requestedTool,
			because: "the requested tool's contract must be the subject of the response");
		responseText.Should().NotContain("\"index\"",
			because: "returning the compact index for a NAMED request is the exact defect this closes — the "
				+ "agent asked one question and silently got the catalog back as a success");
	}

	[Test]
	[AllureTag("list-apps")]
	[AllureName("An empty payload behaves identically to an explicit empty wrapper for a tool that declares a no-arguments operation")]
	[Description("ENG-95885 R3: list-apps declares a natural no-arguments operation, so {} must be accepted exactly like {\"args\":{}} instead of failing SDK binding with a missing-parameter error.")]
	public async Task EmptyPayload_ShouldMatchExplicitEmptyWrapper_WhenToolDeclaresNoArgumentsCapability()
	{
		// Arrange
		await using ArrangeContext arrangeContext = Arrange();
		string toolName = ApplicationGetListTool.ApplicationGetListToolName;

		// Act
		CallToolResult emptyResult = await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?>(),
			arrangeContext.CancellationTokenSource.Token);
		CallToolResult explicitWrapperResult = await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?>()
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		string emptyText = string.Join("\n", emptyResult.Content.OfType<TextContentBlock>().Select(b => b.Text));
		string wrapperText = string.Join("\n",
			explicitWrapperResult.Content.OfType<TextContentBlock>().Select(b => b.Text));

		emptyResult.IsError.Should().Be(explicitWrapperResult.IsError,
			because: "the declared no-arguments call must classify exactly like the explicit empty wrapper");
		emptyText.Should().Be(wrapperText,
			because: "before ENG-95885 the empty payload failed at SDK binding while the explicit wrapper bound "
				+ "fine — identical outcomes are the proof the empty wrapper is now synthesized");
	}

	[Test]
	[AllureTag("get-request-info")]
	[AllureName("An empty payload reaches the catalog operation for get-request-info")]
	[Description("ENG-95885 R3: returning the whole request catalog is get-request-info's documented no-arguments operation, so {} must behave exactly like {\"args\":{}}.")]
	public async Task EmptyPayload_ShouldMatchExplicitEmptyWrapper_ForRequestInfoCatalogCall()
	{
		// Arrange
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult emptyResult = await arrangeContext.Session.CallToolAsync(
			RequestInfoTool.ToolName,
			new Dictionary<string, object?>(),
			arrangeContext.CancellationTokenSource.Token);
		CallToolResult explicitWrapperResult = await arrangeContext.Session.CallToolAsync(
			RequestInfoTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?>()
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert — compared on the error CLASSIFICATION, not on payload bytes: the catalog body depends on
		// registry reachability, which is not what this case is about.
		emptyResult.IsError.Should().Be(explicitWrapperResult.IsError,
			because: "an empty payload must take the same path as the explicit empty wrapper");
	}

	[Test]
	[AllureTag("clio-run")]
	[AllureName("A JSON-encoded args value is refused with one precise shape-naming error")]
	[Description("ENG-95885 R5: args passed as a string containing JSON text used to surface the raw deserializer message ('... BytePositionInLine'), which tells an agent nothing. It must name the required object shape instead — and must NOT be silently parsed.")]
	public async Task JsonEncodedArgs_ShouldReturnPreciseShapeError_WhenClioRunArgsIsAString()
	{
		// Arrange
		await using ArrangeContext arrangeContext = Arrange();

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ClioRunTool.ToolName,
			new Dictionary<string, object?> {
				["command"] = "get-guidance",
				["args"] = "{\"name\":\"routing\"}"
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		string responseText = string.Join("\n",
			callResult.Content.OfType<TextContentBlock>().Select(b => b.Text));

		callResult.IsError.Should().BeTrue(
			because: "a JSON-encoded object is refused rather than decoded, keeping the accepted input set narrow");
		responseText.Should().Contain("must be a JSON object",
			because: "the error must name the required shape so the call can be fixed on the next attempt");
		responseText.Should().NotContain("BytePositionInLine",
			because: "the raw deserializer text is exactly what ENG-95885 replaces");
	}

	[Test]
	[AllureTag("tools/list")]
	[AllureName("The published tools/list schema still requires the args wrapper")]
	[Description("ENG-95885 R7: the widened accepted input set is a TOLERANT RUNTIME layer, not a schema change. tools/list must keep advertising the wrapped shape, so this test fails if someone later 'aligns' the schema with the runtime tolerance.")]
	public async Task PublishedToolSchema_ShouldStillRequireArgsWrapper_AfterNormalizationLanded()
	{
		// Arrange
		await using ArrangeContext arrangeContext = Arrange();

		// Act
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(
			arrangeContext.CancellationTokenSource.Token);
		McpClientTool? listApps = tools.FirstOrDefault(
			tool => tool.Name == ApplicationGetListTool.ApplicationGetListToolName);

		// Assert
		listApps.Should().NotBeNull(because: "list-apps is a resident tool and must be advertised");
		string schema = listApps!.ProtocolTool.InputSchema.ToString();
		schema.Should().Contain("args",
			because: "the published schema deliberately stays wrapped — the flat shape is accepted at runtime "
				+ "only, and the contract text says so rather than claiming the two are identical");
	}
}
