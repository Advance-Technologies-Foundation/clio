using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// An argument binding failure names the failing member, what the contract expects there and, when the
/// argument documents one, an example - instead of "argument 'args' must be an object" for an object that
/// omits a required member, or "contains a value that does not match the documented shape" for any nested
/// mistake. Each case is a shape an agent actually sent through clio-run.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpToolErrorFilterShapeDiagnosisTests {

	[Test]
	[Category("Unit")]
	[Description("An odata-read payload that sends entity-set instead of entity is refused with the missing required member named and entity suggested for the unknown key, not with 'args must be an object' - the payload was an object.")]
	public async Task HandleCallToolErrors_ShouldNameMissingMemberAndSuggestIt_WhenRequiredArgsMemberIsMisspelled() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateODataReadContext(
			"{\"environment-name\":\"dev\",\"entity-set\":\"Contact\",\"top\":1}");

		// Act
		string text = await CallAndReadTextAsync(context);

		// Assert
		text.Should().Contain("invalid-parameter-type: argument 'args' for MCP tool 'odata-read' is missing required member 'entity'.",
			because: "the refusal must name the member the payload omitted, keeping the advertised error code");
		text.Should().Contain("'entity-set' (did you mean 'entity'?)",
			because: "the unknown key the caller sent is named with the member it most likely meant");
		text.Should().Contain("entity (required)",
			because: "the expected shape of args lists its members and marks the required ones");
		text.Should().NotContain("must be an object",
			because: "the payload WAS an object, so that advice names a correction the caller has already made");
	}

	[Test]
	[Category("Unit")]
	[Description("A filter condition written as {column, operator, value} is refused at filters.all[0] with 'field' named as missing, both unknown keys listed, op suggested for operator, the condition's shape, and the filters example from the contract.")]
	public async Task HandleCallToolErrors_ShouldNameTheConditionAndShowItsShape_WhenFilterConditionUsesUnknownMembers() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateODataReadContext(
			"{\"environment-name\":\"dev\",\"entity\":\"Contact\","
			+ "\"filters\":{\"all\":[{\"column\":\"Name\",\"operator\":\"eq\",\"value\":\"x\"}]}}");

		// Act
		string text = await CallAndReadTextAsync(context);

		// Assert
		text.Should().Contain("argument 'filters' for MCP tool 'odata-read' contains a value that does not match the documented shape.",
			because: "the lead sentence keeps naming the argument the caller can navigate from");
		text.Should().Contain("At filters.all[0]: missing required member 'field'.",
			because: "the message must point at the condition that failed and the member it lacks");
		text.Should().Contain("'column'",
			because: "every unknown key in the failing object is named back");
		text.Should().Contain("'operator' (did you mean 'op'?)",
			because: "operator is the obvious long form of op");
		text.Should().Contain("Expected filters.all[0]: an object with field (required), op, value, in.",
			because: "the caller must learn the condition's members without reading the full contract");
		text.Should().Contain("Example filters: { \"all\": [{ \"field\":",
			because: "the example documented on the filters argument shows the whole shape in one line");
	}

	[Test]
	[Category("Unit")]
	[Description("filters.all sent as an object instead of an array is refused at filters.all with both JSON kinds named and the array-of-conditions shape.")]
	public async Task HandleCallToolErrors_ShouldNameBothKinds_WhenFilterGroupIsAnObjectInsteadOfAnArray() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateODataReadContext(
			"{\"environment-name\":\"dev\",\"entity\":\"Contact\",\"filters\":{\"all\":{\"Name\":\"x\"}}}");

		// Act
		string text = await CallAndReadTextAsync(context);

		// Assert
		text.Should().Contain("At filters.all: expected an array, received an object.",
			because: "the failing member and both JSON kinds are what the caller needs to correct the call");
		text.Should().Contain("Expected filters.all: an array of objects with field (required), op, value, in.",
			because: "an array of conditions is described with the members of one condition");
	}

	[Test]
	[Category("Unit")]
	[Description("A filter condition that uses the column name as its key ({\"Name\": \"x\"}) is refused at filters.any[0] with 'field' missing and 'Name' listed as unknown, with no invented suggestion.")]
	public async Task HandleCallToolErrors_ShouldNotInventSuggestion_WhenUnknownKeyResemblesNoMember() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateODataReadContext(
			"{\"environment-name\":\"dev\",\"entity\":\"Contact\",\"filters\":{\"any\":[{\"Name\":\"x\"}]}}");

		// Act
		string text = await CallAndReadTextAsync(context);

		// Assert
		text.Should().Contain("At filters.any[0]: missing required member 'field'. Unknown member 'Name'.",
			because: "the condition's own column name is not a member, and nothing it resembles may be guessed");
		text.Should().NotContain("did you mean",
			because: "a suggestion is offered only for a prefix or a near spelling of a real member");
	}

	[Test]
	[Category("Unit")]
	[Description("sync-pages resources sent as a JSON object is refused at pages[0].resources with 'expected a string, received an object', the JSON-text hint, and the start of the member's own description.")]
	public async Task HandleCallToolErrors_ShouldSayResourcesIsJsonText_WhenSyncPagesResourcesIsAnObject() {
		// Arrange
		RequestContext<CallToolRequestParams> context = McpRequestContextTestFactory.CreateCallToolContext(
			PageSyncTool.ToolName,
			new Dictionary<string, JsonElement> {
				["args"] = JsonDocument.Parse(
					"{\"environment-name\":\"dev\",\"pages\":[{\"schema-name\":\"UsrX_FormPage\",\"body\":\"define()\","
					+ "\"resources\":{\"UsrDetailsTab_caption\":\"Details\"}}]}").RootElement
			});
		context.MatchedPrimitive = McpServerTool.Create(
			typeof(PageSyncTool).GetMethod(nameof(PageSyncTool.SyncPages))!,
			_ => throw new AssertionException("tool body must not run"));

		// Act
		string text = await CallAndReadTextAsync(context);

		// Assert
		text.Should().Contain("argument 'pages' for MCP tool 'sync-pages' contains a value that does not match the documented shape.",
			because: "the lead sentence keeps naming the argument the caller sent");
		text.Should().Contain("At pages[0].resources: expected a string, received an object.",
			because: "the message must name the nested member and the JSON kind it takes");
		text.Should().Contain("send it as one JSON string",
			because: "a member documented as JSON text is the case where the JSON value itself is the mistake");
		text.Should().Contain("About pages[0].resources: JSON object string",
			because: "the member's own description says what the string must hold");
		text.Should().NotContain("UsrDetailsTab_caption",
			because: "only member names are echoed - the value the caller sent stays out of the message");
	}

	[Test]
	[Category("Unit")]
	[Description("A binding failure the diagnosis cannot explain - a converter that refuses a value of the right JSON kind - keeps the generic message instead of naming a wrong member.")]
	public async Task HandleCallToolErrors_ShouldKeepGenericMessage_WhenTheFailingNodeShowsNothingToName() {
		// Arrange
		RequestContext<CallToolRequestParams> context = McpRequestContextTestFactory.CreateCallToolContext(
			"fake-converter-tool",
			new Dictionary<string, JsonElement> {
				["args"] = JsonDocument.Parse("{\"value\":\"refused\"}").RootElement
			});
		context.MatchedPrimitive = McpServerTool.Create(
			typeof(FakeConverterTool).GetMethod(nameof(FakeConverterTool.Execute))!,
			new FakeConverterTool());

		// Act
		string text = await CallAndReadTextAsync(context);

		// Assert
		text.Should().Contain("Received an incompatible JSON value.",
			because: "a node of the documented kind that still fails is not something this can explain");
		text.Should().NotContain("At value:",
			because: "naming nothing is better than naming the wrong thing");
	}

	[Test]
	[Category("Unit")]
	[Description("The same misspelled required member dispatched through clio-run gets the same named-member refusal as the direct call, since clio-run reproduces the binding failure through the shared diagnostic.")]
	public async Task ClioRun_ShouldNameMissingMember_WhenDispatchedODataReadMisspellsRequiredMember() {
		// Arrange
		IMcpToolInvokerRegistry registry = Substitute.For<IMcpToolInvokerRegistry>();
		McpServerTool tool = McpServerTool.Create(
			typeof(ODataReadTool).GetMethod(nameof(ODataReadTool.Read))!,
			target: CreateODataReadTool());
		registry.TryGetTool(ODataReadTool.ToolName, out Arg.Any<McpServerTool>())
			.Returns(call => {
				call[1] = tool;
				return true;
			});
		registry.IsDestructive(ODataReadTool.ToolName).Returns(false);
		ClioRunExecutor executor = new(
			registry,
			Substitute.For<IMcpToolCompatibilityCatalog>(),
			new McpExecutionRouter(
				new McpToolExecutionMetadataReader(Substitute.For<IMcpToolCompatibilityCatalog>()),
				new McpWorkerCohort([]),
				new McpWorkerPathGate(() => McpHostTransportKind.Stdio, () => false),
				workerPathWired: true));
		JsonElement arguments = JsonDocument
			.Parse("{\"environment-name\":\"dev\",\"entity-set\":\"Contact\"}").RootElement;

		// Act
		CallToolResult result = await executor.RunAsync(ODataReadTool.ToolName, arguments, destructiveSurface: false,
			McpRequestContextTestFactory.CreateCallToolContext(ClioRunTool.ToolName), CancellationToken.None);

		// Assert
		string text = string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
		result.IsError.Should().BeTrue(because: "a payload that omits a required member never reaches the tool body");
		text.Should().Contain("is missing required member 'entity'",
			because: "the nested dispatch must answer with the same named-member refusal as the direct call");
		text.Should().Contain("(did you mean 'entity'?)",
			because: "the suggestion travels on the clio-run path as well");
	}

	[Test]
	[Category("Unit")]
	[Description("A polymorphic member is never described by its base type's members: the derived members and the discriminator decide its shape, so listing the base's alone would send the caller to a second failure.")]
	public async Task HandleCallToolErrors_ShouldNameNoMembers_WhenTheFailingNodeIsPolymorphic() {
		// Arrange
		RequestContext<CallToolRequestParams> arrayContext = CreateFakeContext<FakeRuleTool>("{\"actions\":{}}");
		RequestContext<CallToolRequestParams> elementContext = CreateFakeContext<FakeRuleTool>("{\"actions\":[\"make-editable\"]}");

		// Act
		string arrayText = await CallAndReadTextAsync(arrayContext);
		string elementText = await CallAndReadTextAsync(elementContext);

		// Assert
		arrayText.Should().Contain("argument 'actions' for MCP tool 'fake-tool' must be an array. Received an object.",
			because: "the JSON kinds are still certain when the element type is polymorphic");
		elementText.Should().Contain("At actions[0]: expected an object, received a string.",
			because: "an element of a polymorphic type is still an object on the wire");
		(arrayText + elementText).Should().NotContain("uId",
			because: "the base type's members are not the shape of any derived action");
	}

	[Test]
	[Category("Unit")]
	[Description("A dictionary key on the path is shown bracketed and sanitized, and an element is not described by its container's description or JSON-text hint.")]
	public async Task HandleCallToolErrors_ShouldBracketDictionaryKeysAndKeepContainerTextOffElements() {
		// Arrange
		RequestContext<CallToolRequestParams> keyContext = CreateFakeContext<FakeMapTool>(
			"{\"map\":{\"en\\nfake prose\":{\"x\":1}}}");
		RequestContext<CallToolRequestParams> elementContext = CreateFakeContext<FakeMapTool>(
			"{\"map\":{\"en\":{\"label\":\"a\",\"tags\":[{\"a\":1}]}}}");

		// Act
		string keyText = await CallAndReadTextAsync(keyContext);
		string elementText = await CallAndReadTextAsync(elementContext);

		// Assert
		keyText.Should().Contain("At map['",
			because: "a caller's dictionary key is framed so it cannot read as the server's own prose");
		keyText.Should().Contain("missing required member 'label'",
			because: "the dictionary value is still diagnosed as the record it is");
		keyText.Should().NotContain("\n",
			because: "a line break in a caller's key must not start a new line of the message");
		elementText.Should().Contain("At map['en'].tags[0]: expected a string, received an object.",
			because: "the element is located through the dictionary key and the member");
		elementText.Should().NotContain("About ",
			because: "the container's description describes the list, not one of its elements");
		elementText.Should().NotContain("as TEXT",
			because: "the JSON-text hint belongs to the member whose description says so, not to its elements");
	}

	[Test]
	[Category("Unit")]
	[Description("Unknown keys are echoed sanitized and capped: control characters and quotes never reach the message, at most ten keys are named with the rest counted, and an over-long key gets no suggestion.")]
	public async Task HandleCallToolErrors_ShouldSanitizeAndCapUnknownKeys_WhenRequiredMemberIsMissing() {
		// Arrange
		string longKey = new('k', 10_000);
		string extraKeys = string.Join(",", Enumerable.Range(1, 13).Select(index => $"\"extra{index}\":1"));
		RequestContext<CallToolRequestParams> context = CreateODataReadContext(
			"{\"environment-name\":\"dev\",\"bad\\u001b[31m'\\n\\\"key\":1,\"" + longKey + "\":1," + extraKeys + "}");

		// Act
		string text = await CallAndReadTextAsync(context);

		// Assert
		text.Should().Contain("is missing required member 'entity'",
			because: "the refusal is still about the member the payload omitted");
		text.Should().Contain("and 5 more.",
			because: "fifteen unknown keys are named ten at a time with the rest counted");
		text.Should().NotContain("\u001b", because: "a terminal escape in a caller's key must not reach the transcript");
		text.Should().NotContain("\n", because: "a line break in a caller's key must not start a new line of the message");
		text.Should().NotContain("'\"", because: "a quote in a caller's key must not close the quoting around it");
		text.Length.Should().BeLessThan(3_000,
			because: "an echoed key is truncated, so one huge key cannot inflate the refusal");
	}

	[Test]
	[Category("Unit")]
	[Description("A string-bound member with a format is worded with that format: an unknown enum value names the allowed values instead of 'must be a string' for a value that was one.")]
	public async Task HandleCallToolErrors_ShouldNameTheAllowedValues_WhenAnEnumValueIsUnknown() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateFakeContext<FakeColorTool>("{\"color\":\"Purple\"}");

		// Act
		string text = await CallAndReadTextAsync(context);

		// Assert
		text.Should().Contain("argument 'color' for MCP tool 'fake-tool' must be a string naming one of Red, Blue.",
			because: "the caller sent a string, so the correction is which string, not which JSON kind");
	}

	[Test]
	[Category("Unit")]
	[Description("An explicit JSON null for a required GUID parameter is refused as 'must be a GUID string', the format the member takes, not as 'an object'.")]
	public async Task HandleCallToolErrors_ShouldWordTheGuidFormat_WhenARequiredGuidParameterIsNull() {
		// Arrange
		RequestContext<CallToolRequestParams> context = McpRequestContextTestFactory.CreateCallToolContext(
			"fake-tool",
			new Dictionary<string, JsonElement> {
				["id"] = JsonDocument.Parse("null").RootElement
			});
		context.MatchedPrimitive = McpServerTool.Create(typeof(FakeGuidTool).GetMethod("Execute")!, new FakeGuidTool());

		// Act
		string text = await CallAndReadTextAsync(context);

		// Assert
		text.Should().Contain("argument 'id' for MCP tool 'fake-tool' must be a GUID string. Received a JSON null",
			because: "a GUID travels as a string on the wire, and the message says which kind of string");
	}

	[Test]
	[Category("Unit")]
	[Description("A dictionary parameter's key is located as a key, and the dictionary's indexer is never reported as a member named Item.")]
	public async Task HandleCallToolErrors_ShouldLocateTheKey_WhenADictionaryParameterHoldsAWrongValue() {
		// Arrange
		RequestContext<CallToolRequestParams> context = CreateFakeContext<FakeDictionaryTool>("{\"item\":\"abc\"}");

		// Act
		string text = await CallAndReadTextAsync(context);

		// Assert
		text.Should().Contain("At args['item']: expected a number, received a string.",
			because: "the failing value is the dictionary's entry, named by its key");
		text.Should().NotContain("'Item'",
			because: "System.Text.Json never binds an indexer, so it is not a member a caller can send");
	}

	private static RequestContext<CallToolRequestParams> CreateODataReadContext(string argsJson) {
		RequestContext<CallToolRequestParams> context = McpRequestContextTestFactory.CreateCallToolContext(
			ODataReadTool.ToolName,
			new Dictionary<string, JsonElement> {
				["args"] = JsonDocument.Parse(argsJson).RootElement
			});
		context.MatchedPrimitive = McpServerTool.Create(
			typeof(ODataReadTool).GetMethod(nameof(ODataReadTool.Read), BindingFlags.Public | BindingFlags.Instance)!,
			CreateODataReadTool());
		return context;
	}

	private static RequestContext<CallToolRequestParams> CreateFakeContext<TTool>(string argsJson) where TTool : new() {
		RequestContext<CallToolRequestParams> context = McpRequestContextTestFactory.CreateCallToolContext(
			"fake-tool",
			new Dictionary<string, JsonElement> {
				["args"] = JsonDocument.Parse(argsJson).RootElement
			});
		context.MatchedPrimitive = McpServerTool.Create(typeof(TTool).GetMethod("Execute")!, new TTool());
		return context;
	}

	private static ODataReadTool CreateODataReadTool() =>
		new(Substitute.For<IToolCommandResolver>(), new OperationCorrelationIdProvider(), Substitute.For<ILogger>());

	private static async Task<string> CallAndReadTextAsync(RequestContext<CallToolRequestParams> context) {
		McpRequestHandler<CallToolRequestParams, CallToolResult> handler =
			McpToolErrorFilter.HandleCallToolErrors((_, _) => throw new AssertionException("tool body must not run"));
		CallToolResult result = await handler(context, CancellationToken.None);
		result.IsError.Should().BeTrue(because: "an argument that does not bind is refused before the tool body runs");
		return string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
	}

	/// <summary>A converter that refuses every value, so the failing node has the documented kind.</summary>
	public sealed class RefusingConverter : JsonConverter<string> {
		public override string Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options) =>
			throw new JsonException("refused by the converter");

		public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
			writer.WriteStringValue(value);
	}

	public sealed record FakeConverterArgs(
		[property: JsonPropertyName("value")]
		[property: JsonConverter(typeof(RefusingConverter))]
		string Value);

	public sealed class FakeConverterTool {
		public string Execute(FakeConverterArgs args) => "ok";
	}

	[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
	[JsonDerivedType(typeof(FakeMakeEditableAction), "make-editable")]
	public abstract record FakeAction {
		[JsonPropertyName("uId")]
		public string? UId { get; init; }
	}

	public sealed record FakeMakeEditableAction : FakeAction {
		[JsonPropertyName("items")]
		public List<string>? Items { get; init; }
	}

	public sealed record FakeRuleArgs(
		[property: JsonPropertyName("actions")]
		List<FakeAction> Actions);

	public sealed class FakeRuleTool {
		public string Execute(FakeRuleArgs args) => "ok";
	}

	public sealed record FakeLabel {
		[JsonPropertyName("label")]
		public required string Label { get; init; }

		[JsonPropertyName("tags")]
		[System.ComponentModel.Description("JSON array of tag names.")]
		public List<string>? Tags { get; init; }
	}

	public sealed record FakeMapArgs(
		[property: JsonPropertyName("map")]
		Dictionary<string, FakeLabel> Map);

	public sealed class FakeMapTool {
		public string Execute(FakeMapArgs args) => "ok";
	}

	public enum FakeColor {
		Red,
		Blue
	}

	public sealed record FakeColorArgs(
		[property: JsonPropertyName("color")]
		FakeColor Color);

	public sealed class FakeColorTool {
		public string Execute(FakeColorArgs args) => "ok";
	}

	public sealed class FakeGuidTool {
		public string Execute([System.ComponentModel.DataAnnotations.Required] System.Guid id) => "ok";
	}

	public sealed class FakeDictionaryTool {
		public string Execute(Dictionary<string, int> args) => "ok";
	}
}
