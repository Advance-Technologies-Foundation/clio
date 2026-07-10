using System.Text.Json.Nodes;
using ATF.Repository.Providers;
using Clio.Command.ProcessModel;
using Clio.Common;
using ErrorOr;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// HTTP-layer tests for <see cref="ServerProcessDescriber"/>: the wrapped <c>{"request":{name|uid}}</c> body, the
/// resolved DescribeProcess route, and each <see cref="ErrorOr{T}"/> branch (success / server-failure / empty /
/// unexpected shape / no identity). These exercise the actual clio→server contract, which the tool tests fake.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "ProcessModel")]
public sealed class ServerProcessDescriberTests {

	private const string DescribeUrl = "http://sandbox/0/rest/ProcessDesignService/DescribeProcess";

	private static ServerProcessDescriber CreateDescriber(IApplicationClient client) {
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.DescribeProcess).Returns(DescribeUrl);
		return new ServerProcessDescriber(client,
			Substitute.For<IDataProvider>(), urlBuilder);
	}

	private static IApplicationClient ClientReturning(string response) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(DescribeUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(response);
		return client;
	}

	[Test]
	[Description("Posts the process code wrapped under 'request.name' to the DescribeProcess route and returns the parsed graph on success.")]
	public void Describe_ShouldPostWrappedNameToDescribeRoute_AndReturnResult_OnSuccess() {
		// Arrange — the element carries its name (= local handle/Name) and uid (= element UId); both must survive.
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\",\"schemaUId\":\"5c58c4c4-134b-4744-9c67-96d9c69c9d55\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\"}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "a successful describe returns the graph, not an error");
		result.Value.Name.Should().Be("UsrProc", because: "the process name is read from the server result");
		result.Value.Elements[0].Name.Should().Be("task1", because: "the element local handle (Name) is read back");
		result.Value.Elements[0].Uid.Should().Be("a1b2c3d4-0000-0000-0000-000000000001",
			because: "the element UId must be surfaced, not dropped (PR #715 review)");
		client.Received(1).ExecutePostRequest(DescribeUrl,
			Arg.Is<string>(body => Wrapped(body)["name"].GetValue<string>() == "UsrProc"),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Deserializes an element parameter's direction and isResult from the server response into the DescribedParameter DTO (so callers can tell an element's outputs, mappable as a source, from its inputs).")]
	public void Describe_ShouldReadParameterDirectionAndIsResult_WhenServerReportsThem() {
		// Arrange — a user task whose parameter is an output (isResult true) while its direction is Variable
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\","
			+ "\"parameters\":[{\"name\":\"PResult\",\"uid\":\"p1\",\"type\":\"Guid\",\"direction\":\"Variable\",\"isResult\":true,\"source\":\"None\"}]}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedParameter parameter = result.Value.Elements[0].Parameters[0];
		parameter.Direction.Should().Be("Variable",
			because: "the parameter's direction must be read from the server, not dropped by the clio DTO");
		parameter.IsResult.Should().BeTrue(
			because: "isResult marks an element output usable as a mapping source and must be deserialized");
	}

	[Test]
	[Description("Leaves direction/isResult unset (null) when an older server omits them, so the absent fields serialize away cleanly.")]
	public void Describe_ShouldLeaveDirectionAndIsResultNull_WhenServerOmitsThem() {
		// Arrange — an older clioprocessbuilder that does not report direction/isResult on parameters
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\","
			+ "\"parameters\":[{\"name\":\"PResult\",\"uid\":\"p1\",\"type\":\"Guid\",\"source\":\"None\"}]}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedParameter parameter = result.Value.Elements[0].Parameters[0];
		parameter.Direction.Should().BeNull(
			because: "an omitted direction stays null so it serializes away (WhenWritingNull) for older servers");
		parameter.IsResult.Should().BeNull(
			because: "an omitted isResult stays null rather than defaulting to false, avoiding a misleading output");
	}

	[Test]
	[Description("Deserializes an element's data source filter (object + logical operation + conditions + nested groups) from the server response into the DescribedFilter DTO, so describe read-back surfaces the filter instead of dropping it.")]
	public void Describe_ShouldReadElementFilter_WhenServerReportsIt() {
		// Arrange — a signal start whose decoded filter is Age > 30 AND (Address = 'x')
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"SignalStart1\",\"type\":\"ProcessSchemaStartSignalEvent\",\"buildType\":\"signalstart\","
			+ "\"filter\":{\"object\":\"Contact\",\"logicalOperation\":\"and\","
			+ "\"conditions\":[{\"column\":\"Age\",\"comparison\":\"greater\",\"value\":\"30\"}],"
			+ "\"groups\":[{\"logicalOperation\":\"or\",\"conditions\":[{\"column\":\"Address\",\"comparison\":\"equal\",\"value\":\"x\"}]}]}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedFilter filter = result.Value.Elements[0].Filter;
		filter.Should().NotBeNull(because: "the element's data source filter must be surfaced, not dropped by the clio DTO");
		filter.Object.Should().Be("Contact", because: "the filter's root object is read back");
		filter.LogicalOperation.Should().Be("and", because: "the root logical operation is read back");
		filter.Conditions.Should().ContainSingle(because: "the single root condition is deserialized");
		filter.Conditions[0].Column.Should().Be("Age", because: "the condition column round-trips");
		filter.Conditions[0].Comparison.Should().Be("greater", because: "the comparison round-trips");
		filter.Conditions[0].Value.Should().Be("30", because: "the constant value round-trips");
		filter.Groups.Should().ContainSingle(because: "the nested group is deserialized");
		filter.Groups[0].LogicalOperation.Should().Be("or", because: "the nested group operator round-trips");
		filter.Groups[0].Conditions[0].Column.Should().Be("Address", because: "the nested condition round-trips");
	}

	[Test]
	[Description("Deserializes a lookup condition's displayValue (the resolved caption) alongside its raw id value, so a lookup reads back as a human-readable caption instead of the clio DTO dropping it and leaving only a GUID.")]
	public void Describe_ShouldReadFilterConditionDisplayValue_WhenServerReportsLookupCaption() {
		// Arrange — UsrStage = <guid> with the resolved caption "Approved" carried on displayValue
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"SignalStart1\",\"type\":\"ProcessSchemaStartSignalEvent\",\"buildType\":\"signalstart\","
			+ "\"filter\":{\"object\":\"UsrClioFilterTest\",\"logicalOperation\":\"and\","
			+ "\"conditions\":[{\"column\":\"UsrStage\",\"comparison\":\"equal\",\"value\":\"09cd1bea-6a0e-4972-a6ad-97be3ea83dac\",\"displayValue\":\"Approved\"}]}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedFilterCondition condition = result.Value.Elements[0].Filter.Conditions[0];
		condition.Value.Should().Be("09cd1bea-6a0e-4972-a6ad-97be3ea83dac",
			because: "the raw lookup id round-trips as the value for an unambiguous re-build");
		condition.DisplayValue.Should().Be("Approved",
			because: "the resolved lookup caption is surfaced so the read-back is human-readable, not a bare GUID");
	}

	[Test]
	[Description("Leaves the element filter null when the server reports no filter, so it serializes away for non-filtered elements.")]
	public void Describe_ShouldLeaveFilterNull_WhenServerOmitsIt() {
		// Arrange — an element with no data source filter
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\"}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.Value.Elements[0].Filter.Should().BeNull(
			because: "an element without a filter keeps Filter null so it serializes away (WhenWritingNull)");
	}

	[Test]
	[Description("Deserializes a filter condition's macro (and its integer macroArgument) so a relative-date / system macro survives read-back instead of being dropped by the clio DTO.")]
	public void Describe_ShouldReadFilterConditionMacro_WhenServerReportsIt() {
		// Arrange — CreatedOn = Today (no argument) AND CreatedOn > NextNDays(7)
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"SignalStart1\",\"type\":\"ProcessSchemaStartSignalEvent\",\"buildType\":\"signalstart\","
			+ "\"filter\":{\"object\":\"Contact\",\"logicalOperation\":\"and\",\"conditions\":["
			+ "{\"column\":\"CreatedOn\",\"comparison\":\"equal\",\"macro\":\"Today\"},"
			+ "{\"column\":\"CreatedOn\",\"comparison\":\"greater\",\"macro\":\"NextNDays\",\"macroArgument\":7}]}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedFilter filter = result.Value.Elements[0].Filter;
		filter.Conditions[0].Macro.Should().Be("Today",
			because: "a no-argument macro must surface on read-back, not be dropped by the clio DTO");
		filter.Conditions[0].MacroArgument.Should().BeNull(because: "Today takes no argument");
		filter.Conditions[1].Macro.Should().Be("NextNDays", because: "an argument macro's name surfaces");
		filter.Conditions[1].MacroArgument.Should().Be(7,
			because: "the macro's integer argument must surface, not be dropped by the clio DTO");
	}

	[Test]
	[Description("Deserializes a filter condition's date-part (Year(CreatedOn) = 2026) so the left-hand date-part modifier survives read-back instead of being dropped by the clio DTO.")]
	public void Describe_ShouldReadFilterConditionDatePart_WhenServerReportsIt() {
		// Arrange — Year(CreatedOn) = 2026
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"SignalStart1\",\"type\":\"ProcessSchemaStartSignalEvent\",\"buildType\":\"signalstart\","
			+ "\"filter\":{\"object\":\"Contact\",\"logicalOperation\":\"and\",\"conditions\":["
			+ "{\"column\":\"CreatedOn\",\"comparison\":\"equal\",\"datePart\":\"Year\",\"value\":\"2026\"}]}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedFilter filter = result.Value.Elements[0].Filter;
		filter.Conditions[0].DatePart.Should().Be("Year",
			because: "the left-hand date-part modifier must surface on read-back, not be dropped by the clio DTO");
		filter.Conditions[0].Column.Should().Be("CreatedOn", because: "the date-part column round-trips");
		filter.Conditions[0].Value.Should().Be("2026", because: "the extracted-part integer value round-trips");
	}

	[Test]
	[Description("A parameter reference (process- or element-level) surfaces on read-back as the raw expression meta-path token, matching the server decoder, which never emits a structured processParameter/elementParameter; the reserved structured fields stay null.")]
	public void Describe_ShouldSurfaceParameterReferencesAsExpressionTokens_WhenServerReportsThem() {
		// Arrange — the real server surfaces BOTH reference kinds as an expression token: Address = a raw [#..#]
		// token, Account = an element-parameter meta-path token (never a structured elementParameter object).
		const string elementRefToken =
			"[IsOwnerSchema:false].[IsSchema:false].[Element:{02f3221a-1111-2222-3333-444444444444}].[Parameter:{4d2571e8-5555-6666-7777-888888888888}]";
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"read1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\","
			+ "\"filter\":{\"object\":\"Contact\",\"logicalOperation\":\"and\",\"conditions\":["
			+ "{\"column\":\"Address\",\"comparison\":\"equal\",\"expression\":\"[#Custom.Token#]\"},"
			+ "{\"column\":\"Account\",\"comparison\":\"equal\",\"expression\":\"" + elementRefToken + "\"}]}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedFilter filter = result.Value.Elements[0].Filter;
		filter.Conditions[0].Expression.Should().Be("[#Custom.Token#]",
			because: "a raw expression token must surface on read-back, not be dropped by the clio DTO");
		filter.Conditions[1].Expression.Should().Be(elementRefToken,
			because: "an element-parameter reference is surfaced as the raw meta-path expression token, exactly as the server decoder emits it");
		filter.Conditions[1].ElementParameter.Should().BeNull(
			because: "the current server never emits a structured elementParameter — the reference lives in expression, so the reserved field stays null");
		filter.Conditions[1].ProcessParameter.Should().BeNull(
			because: "the current server never emits a structured processParameter either — references are expression tokens only");
	}

	[Test]
	[Description("Forward-compat only: the reserved elementParameter DTO field still deserializes a structured reference if a future server emits one; documents that the field binds, NOT that the current server produces this shape (it surfaces references as expression tokens).")]
	public void Describe_ShouldBindStructuredElementParameter_AsReservedForwardCompatShape() {
		// Arrange — a synthetic response in a shape the CURRENT server does NOT emit (real references come back as
		// expression tokens); this pins only that the reserved DTO field would bind a future structured ref.
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"read1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\","
			+ "\"filter\":{\"object\":\"Contact\",\"logicalOperation\":\"and\",\"conditions\":["
			+ "{\"column\":\"Account\",\"comparison\":\"equal\",\"elementParameter\":{\"elementId\":\"task1\",\"parameter\":\"Account\"}}]}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedFilterCondition condition = result.Value.Elements[0].Filter.Conditions[0];
		condition.ElementParameter.Should().NotBeNull(
			because: "the reserved elementParameter field must still deserialize a structured ref for forward-compat, even though the current server does not emit this shape");
		condition.ElementParameter.ElementId.Should().Be("task1",
			because: "the structured reference's element id binds when present");
		condition.ElementParameter.Parameter.Should().Be("Account",
			because: "the structured reference's parameter name binds when present");
	}

	[Test]
	[Description("Posts the uid (not the name) when the identity is a uid.")]
	public void Describe_ShouldPostWrappedUid_WhenIdentityIsUid() {
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\"}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		describer.Describe(new ProcessIdentity(null, "5c58c4c4-134b-4744-9c67-96d9c69c9d55", null), null);

		client.Received(1).ExecutePostRequest(DescribeUrl,
			Arg.Is<string>(body => Wrapped(body)["uid"].GetValue<string>() == "5c58c4c4-134b-4744-9c67-96d9c69c9d55"),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Returns an error (not a throw) carrying the server's message when the result reports success=false.")]
	public void Describe_ShouldReturnError_WhenSuccessFalse() {
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":false,\"errorMessage\":\"Process 'UsrProc' was not found.\"}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		result.IsError.Should().BeTrue(because: "a server-reported failure becomes an ErrorOr error");
		result.FirstError.Description.Should().Contain("was not found",
			because: "the server message is surfaced to the caller");
	}

	[Test]
	[Description("Returns an error when the server response body is empty.")]
	public void Describe_ShouldReturnError_WhenResponseEmpty() {
		ServerProcessDescriber describer = CreateDescriber(ClientReturning(""));

		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		result.IsError.Should().BeTrue(because: "an empty server response is a failure, not a graph");
	}

	[Test]
	[Description("Returns an error (without calling the server) when no identity is provided.")]
	public void Describe_ShouldReturnError_WhenNoIdentity() {
		IApplicationClient client = ClientReturning("{}");
		ServerProcessDescriber describer = CreateDescriber(client);

		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity(null, null, null), null);

		result.IsError.Should().BeTrue(because: "a describe needs a code, uid, or caption");
		client.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default, default, default, default);
	}

	// The describer wraps the identity under a "request" property (ProcessDesignService BodyStyle=Wrapped).
	private static JsonNode Wrapped(string body) => JsonNode.Parse(body)["request"];
}
