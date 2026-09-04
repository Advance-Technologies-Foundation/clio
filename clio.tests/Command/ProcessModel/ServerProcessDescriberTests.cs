using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
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
	[Description("Deserializes a Lookup ConstValue's valueDisplay - the referenced record's NAME - into the DescribedParameter DTO, beside the unchanged bare-Guid value, so a caller can show a word without a second read.")]
	public void Describe_ShouldReadParameterValueDisplay_WhenServerReportsIt() {
		// Arrange - a Lookup constant the server resolved a name for (ENG-96325); value stays the bare record id
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\","
			+ "\"parameters\":[{\"name\":\"ActivityCategory\",\"uid\":\"p1\",\"type\":\"Lookup\",\"source\":\"ConstValue\","
			+ "\"value\":\"03df85bf-6b19-4dea-8463-d5d49b80bb28\",\"valueDisplay\":\"Call\"}]}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedParameter parameter = result.Value.Elements[0].Parameters[0];
		parameter.Value.Should().Be("03df85bf-6b19-4dea-8463-d5d49b80bb28",
			because: "the runtime encoding is the bare record Guid and the display name must not replace it");
		parameter.ValueDisplay.Should().Be("Call",
			because: "valueDisplay is what the designer renders; dropping it in the clio DTO reinstates the Guid the "
				+ "fix removed, and only the manual e2e suite would notice");
	}

	[Test]
	[Description("Leaves valueDisplay unset (null) when the server omits it - an older package, or a record whose name did not resolve - so the absent field serializes away instead of becoming an empty string.")]
	public void Describe_ShouldLeaveValueDisplayNull_WhenServerOmitsIt() {
		// Arrange - a pre-1.4.0.40 package: the Lookup constant is reported without a display name
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\","
			+ "\"parameters\":[{\"name\":\"ActivityCategory\",\"uid\":\"p1\",\"type\":\"Lookup\",\"source\":\"ConstValue\","
			+ "\"value\":\"03df85bf-6b19-4dea-8463-d5d49b80bb28\"}]}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "an older package omitting the field is not an error");
		result.Value.Elements[0].Parameters[0].ValueDisplay.Should().BeNull(
			because: "an omitted display name must stay null so it serializes away, rather than surfacing as an empty "
				+ "label a caller would render");
	}

	[Test]
	[Description("Leaves direction/isResult unset (null) when an older server omits them, so the absent fields serialize away cleanly.")]
	public void Describe_ShouldLeaveDirectionAndIsResultNull_WhenServerOmitsThem() {
		// Arrange — an older CrtProcessBuilder that does not report direction/isResult on parameters
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
	[Description("Deserializes the element-level useBackgroundMode flag reported for every element, and leaves it null when an older server omits it.")]
	public void Describe_ShouldReadElementBackgroundMode_WhenServerReportsIt() {
		// Arrange — one element reporting the flag, one (older-server shape) omitting it
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"SignalStart1\",\"type\":\"ProcessSchemaStartSignalEvent\",\"buildType\":\"signalstart\",\"useBackgroundMode\":true},"
			+ "{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000002\",\"name\":\"EndEvent1\",\"type\":\"ProcessSchemaTerminateEvent\",\"buildType\":\"endevent\"}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		result.Value.Elements[0].UseBackgroundMode.Should().BeTrue(
			because: "the element-level background-mode flag must be deserialized, not dropped by the clio DTO");
		result.Value.Elements[1].UseBackgroundMode.Should().BeNull(
			because: "an omitted flag stays null so it serializes away (WhenWritingNull) for an older server");
	}

	[Test]
	[Description("Deserializes an EDIT-mode Open edit page element: the editing mode and the record it opens. Every other openEditPage fixture pins add mode with a null record, so the half of the contract that opens an EXISTING record - AC7's second mode and the whole of AC9 - executed in no unit test and rested entirely on stand-gated E2E that Assert.Ignores without a sandbox.")]
	public void Describe_ShouldReadOpenEditPageEditMode_WhenServerReportsARecord() {
		// Arrange - an edit-mode element whose record comes from a process parameter
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"OpenPage1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"openeditpage\",\"userTaskName\":\"OpenEditPageUserTask\","
			+ "\"openEditPage\":{\"page\":\"AccountPageV2\",\"object\":\"Account\",\"editMode\":\"edit\","
			+ "\"defaultValues\":null,"
			+ "\"recordId\":{\"processParameter\":\"AccountIdParameter\"},"
			+ "\"completionMode\":\"onSave\"}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedOpenEditPage block = result.Value.Elements[0].OpenEditPage;
		block.EditMode.Should().Be("edit",
			because: "the mode decides which payload the caller must supply on a re-apply, and 'edit' is the mode "
				+ "no other fixture exercises");
		block.RecordId.Should().NotBeNull(
			because: "an edit-mode element without its record is the state the write path refuses, so a read that "
				+ "dropped it would describe an element that cannot be re-applied");
		block.RecordId!.Value.GetProperty("processParameter").GetString().Should().Be("AccountIdParameter",
			because: "the record source is decoded back into the named shape the write path accepts, which is what "
				+ "makes the described block re-appliable rather than merely readable");
		block.DefaultValues.Should().BeNull(because: "edit mode carries no pre-filled values of its own");
	}

	[Test]
	[Description("Deserializes an element that stores BOTH pre-filled values and a record - the asymmetry the describe contract explicitly promises to report, because the runtime applies stored values in either editing mode. No other fixture produces this shape, so a read that silently dropped one side would have hidden live configuration undetected.")]
	public void Describe_ShouldReadOpenEditPageValuesAndRecord_WhenTheSchemaCarriesBoth() {
		// Arrange - the shape the write path refuses but the schema can hold, which the read must surface whole
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"OpenPage1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"openeditpage\",\"userTaskName\":\"OpenEditPageUserTask\","
			+ "\"openEditPage\":{\"page\":\"AccountPageV2\",\"object\":\"Account\",\"editMode\":\"edit\","
			+ "\"defaultValues\":[{\"column\":\"Address\",\"value\":\"Kyiv\"}],"
			+ "\"recordId\":{\"processParameter\":\"AccountIdParameter\"},"
			+ "\"completionMode\":\"onSave\"}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		DescribedOpenEditPage block = result.Value.Elements[0].OpenEditPage;
		block.DefaultValues.Should().ContainSingle(
			because: "the runtime applies stored values in either mode, so hiding them on an edit-mode element "
				+ "would hide configuration that actually runs");
		block.RecordId.Should().NotBeNull(
			because: "both halves are reported together - that is the documented asymmetry, and dropping either "
				+ "one is the failure this pins");
	}

	[Test]
	[Description("Deserializes an Open edit page element's configuration (page, object, record type, editing mode, pre-filled values, recommendation, hint, completion mode) into the DescribedOpenEditPage DTO, so the block is surfaced typed rather than falling into the element's extension bag unnoticed.")]
	public void Describe_ShouldReadOpenEditPageConfiguration_WhenServerReportsIt() {
		// Arrange - the shape a CrtProcessBuilder that supports the element returns for a configured add-mode element
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"OpenPage1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"openeditpage\",\"userTaskName\":\"OpenEditPageUserTask\","
			+ "\"openEditPage\":{\"page\":\"AccountPageV2\",\"pageSchemaUId\":\"f5edc79d-8d39-4e51-a255-57ccf3f1349e\",\"object\":\"Account\","
			+ "\"pageTypeUId\":null,\"editMode\":\"add\","
			+ "\"defaultValues\":[{\"column\":\"Address\",\"value\":\"Kyiv\"}],"
			+ "\"recordId\":null,\"recommendation\":\"Fill in the account details\",\"hint\":\"Confirm the address\","
			+ "\"completionMode\":\"onSave\"}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedOpenEditPage block = result.Value.Elements[0].OpenEditPage;
		block.Should().NotBeNull(
			because: "the openEditPage block must be deserialized into its own DTO, not silently absorbed by the "
				+ "element's [JsonExtensionData] bag where no caller would find it typed");
		block.Page.Should().Be("AccountPageV2", because: "the page name is what a caller feeds back as 'page'");
		block.PageSchemaUId.Should().Be("f5edc79d-8d39-4e51-a255-57ccf3f1349e",
			because: "the UId is the escape hatch when a name does not resolve");
		block.Object.Should().Be("Account",
			because: "the object is derived from the page server-side, so the read-back is the only place a caller "
				+ "sees which object the step edits");
		block.PageTypeUId.Should().BeNull(
			because: "an untyped object stores no record type, and null is what distinguishes it from a typed one");
		block.EditMode.Should().Be("add", because: "the editing mode decides which payload the block carries");
		block.DefaultValues.Should().ContainSingle(
			because: "the pre-filled values must survive the read-back to be re-appliable");
		block.RecordId.Should().BeNull(because: "add mode opens no existing record");
		block.Recommendation.Should().Be("Fill in the account details",
			because: "the recommendation shown on the page round-trips");
		block.Hint.Should().Be("Confirm the address", because: "the hint round-trips");
		block.CompletionMode.Should().Be("onSave",
			because: "the completion mode is derived from the stored flag, never from a designer caption");
	}

	[Test]
	[Description("Deserializes an Open edit page element's results-by-column block, keeping BOTH the resolved column name and its stored UId - the UId is what tells a caller 'the column no longer resolves here' apart from 'no column is set'.")]
	public void Describe_ShouldReadOpenEditPageResultsByColumn_WhenServerReportsIt() {
		// Arrange
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"OpenPage1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"openeditpage\","
			+ "\"openEditPage\":{\"page\":\"AccountPageV2\",\"editMode\":\"add\","
			+ "\"resultsByColumn\":{\"enabled\":true,\"column\":\"Owner\","
			+ "\"columnUId\":\"3c8c0b2f-3f0e-4a4a-9b1a-6f0f5a2b1c2d\"}}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		DescribedOpenEditPageResultsByColumn results = result.Value.Elements[0].OpenEditPage.ResultsByColumn;
		results.Should().NotBeNull(
			because: "the block must be deserialized into its own DTO rather than absorbed by the openEditPage block's "
				+ "extension bag, where a caller could not read it typed");
		results.Enabled.Should().BeTrue(because: "the flag is what makes the step produce results at all");
		results.Column.Should().Be("Owner",
			because: "the NAME is what a caller feeds back, so it has to survive the read");
		results.ColumnUId.Should().Be("3c8c0b2f-3f0e-4a4a-9b1a-6f0f5a2b1c2d",
			because: "the UId distinguishes an unresolvable column from an unset one - with only the name, both look "
				+ "identical");
	}

	[Test]
	[Description("Deserializes an Open edit page element's Log activity block, including each scheduling interval as a value plus the unit the server decoded from its stored period, so a caller can read what a step schedules without translating the platform's integer enum.")]
	public void Describe_ShouldReadOpenEditPageLogActivity_WhenServerReportsIt() {
		// Arrange - one interval per field, each with a different unit, so a mixed-up mapping cannot pass
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"OpenPage1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"openeditpage\","
			+ "\"openEditPage\":{\"page\":\"AccountPageV2\",\"editMode\":\"add\","
			+ "\"logActivity\":{\"enabled\":true,"
			+ "\"startIn\":{\"value\":2,\"unit\":\"hours\",\"period\":1},"
			+ "\"duration\":{\"value\":20,\"unit\":\"minutes\",\"period\":0},"
			+ "\"remindIn\":{\"value\":3,\"unit\":\"days\",\"period\":2},"
			+ "\"showInCalendar\":false}}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		DescribedOpenEditPageLogActivity activity = result.Value.Elements[0].OpenEditPage.LogActivity;
		activity.Should().NotBeNull(
			because: "the block must be deserialized into its own DTO, not absorbed by the openEditPage block's "
				+ "[JsonExtensionData] bag where no caller would find it typed");
		activity.Enabled.Should().BeTrue(because: "the gate decides whether any of the rest takes effect");
		activity.StartIn.Value.Should().Be(2);
		activity.StartIn.Unit.Should().Be("hours",
			because: "the unit is the half a caller cannot infer - 2 is two hours or two days depending on it");
		activity.StartIn.Period.Should().Be(1, because: "the raw period travels alongside the decoded token");
		activity.Duration.Unit.Should().Be("minutes", because: "each interval decodes independently");
		activity.RemindIn.Unit.Should().Be("days",
			because: "a mapping that confused the three fields would show up here");
		activity.ShowInCalendar.Should().BeFalse(because: "the calendar flag round-trips as reported");
	}

	[Test]
	[Description("Leaves the Log activity block null when the server omits it, so an element that stores none of those fields is not read back as one that schedules an activity - the designer's panel shows values for all of them from schema defaults, so this distinction is the only reliable one.")]
	public void Describe_ShouldLeaveOpenEditPageLogActivityNull_WhenServerOmitsIt() {
		// Arrange
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"OpenPage1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"openeditpage\","
			+ "\"openEditPage\":{\"page\":\"AccountPageV2\",\"editMode\":\"add\"}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.Value.Elements[0].OpenEditPage.LogActivity.Should().BeNull(
			because: "an absent block means the element stores none of it, and inventing one would report scheduling "
				+ "the process does not carry");
	}

	[Test]
	[Description("Deserializes an Open edit page element's performer block (kind, contact, role with its display name, and the show-page flag) into its own DTO, so a caller can see who a step is assigned to instead of finding the assignment only in the element's untyped extension bag.")]
	public void Describe_ShouldReadOpenEditPagePerformer_WhenServerReportsIt() {
		// Arrange - a role performer, the one kind that carries both a formula and a readable display value
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"OpenPage1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"openeditpage\",\"userTaskName\":\"OpenEditPageUserTask\","
			+ "\"openEditPage\":{\"page\":\"AccountPageV2\",\"pageSchemaUId\":\"f5edc79d-8d39-4e51-a255-57ccf3f1349e\",\"object\":\"Account\","
			+ "\"editMode\":\"add\","
			+ "\"performer\":{\"type\":\"role\",\"contact\":null,"
			+ "\"role\":\"[#Lookup.a1c9dfe4-0d1e-4f0f-b6b6-b0f0a1d0e0a1.2b0d3ad9-7a27-46a3-9483-ed70c2687211#]\","
			+ "\"roleDisplay\":\"All employees\",\"showPage\":true},"
			+ "\"completionMode\":\"onSave\"}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		DescribedPerformer performer = result.Value.Elements[0].OpenEditPage.Performer;
		performer.Should().NotBeNull(
			because: "the performer must be deserialized into its own DTO, not silently absorbed by the openEditPage "
				+ "block's [JsonExtensionData] bag where no caller would find it typed");
		performer.Type.Should().Be("role", because: "the kind is what decides which of contact/role carries the value");
		performer.Role.Should().Contain("2b0d3ad9-7a27-46a3-9483-ed70c2687211",
			because: "the stored macro round-trips so the block can be re-submitted verbatim");
		performer.RoleDisplay.Should().Be("All employees",
			because: "the display name is the only human-readable half of a role assignment");
		performer.ShowPage.Should().BeTrue(because: "the show-page flag round-trips as reported");
	}

	[Test]
	[Description("Leaves the performer null when the server reports an Open edit page element without one, so an unassigned step - the designer's own initial state - is not read back as assigned.")]
	public void Describe_ShouldLeaveOpenEditPagePerformerNull_WhenServerOmitsIt() {
		// Arrange - a configured element with no assignment at all
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"OpenPage1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"openeditpage\","
			+ "\"openEditPage\":{\"page\":\"AccountPageV2\",\"editMode\":\"add\"}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.Value.Elements[0].OpenEditPage.Performer.Should().BeNull(
			because: "an absent performer means UNASSIGNED, and inventing one here would report an assignment the "
				+ "process does not carry");
	}

	[Test]
	[Description("Leaves the openEditPage block null when the server does not report it, so an older CrtProcessBuilder - or any other element kind - reads back without inventing a configuration.")]
	public void Describe_ShouldLeaveOpenEditPageNull_WhenServerOmitsIt() {
		// Arrange - a plain user task, the shape any element other than an Open edit page one returns
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\"}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.Value.Elements[0].OpenEditPage.Should().BeNull(
			because: "an absent block must stay null so it serializes away for an older server, and so a caller "
				+ "cannot read 'configured' out of an element that is not");
	}

	[Test]
	[Description("Deserializes a Send email element's email configuration (mode, sender, subject, hasBody, the decoded body, importance, ignoreErrors, recipients, manual-mode performer) from the server response into the DescribedEmail DTO, so describe read-back surfaces the email block instead of dropping it.")]
	public void Describe_ShouldReadSendEmailConfiguration_WhenServerReportsIt() {
		// Arrange — the shape a runtime-verified CrtProcessBuilder DescribeProcess returns for a configured element
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"SendEmail1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"sendemail\",\"userTaskName\":\"EmailTemplateUserTask\","
			+ "\"email\":{\"mode\":\"manual\",\"sender\":\"[#Lookup.5e487721-02e2-48ee-b755-dfa5160f5315.11111111-2222-3333-4444-555555555555#]\",\"senderDisplay\":\"sales@example.com\","
			+ "\"subject\":\"After modify\",\"hasBody\":true,\"body\":\"<p>Hi [[param:ContactName]]</p>\",\"importance\":\"high\",\"ignoreErrors\":true,"
			+ "\"to\":[{\"name\":\"Recipient1\",\"uid\":\"p1\",\"type\":\"MaxSizeText\",\"source\":\"ConstValue\",\"value\":\"to@example.com\"}],"
			+ "\"performer\":{\"type\":\"role\",\"role\":\"[#Lookup.84f44b9a-4bc3-4cbf-a1a8-cec02c1c029c.a29a3ba5-4b0d-de11-9a51-005056c00008#]\",\"roleDisplay\":\"All employees\",\"showPage\":true}}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedEmail email = result.Value.Elements[0].Email;
		email.Should().NotBeNull(because: "the email block must be deserialized, not dropped by the clio DTO");
		email.Mode.Should().Be("manual", because: "the send-mode token maps to the DTO's mode property");
		email.SenderDisplay.Should().Be("sales@example.com",
			because: "senderDisplay carries the human-readable mailbox identity alongside the sender formula");
		email.Subject.Should().Be("After modify", because: "the subject constant survives the read-back");
		email.HasBody.Should().BeTrue(
			because: "hasBody flags that a custom-message body is present on the element");
		email.Body.Should().Be("<p>Hi [[param:ContactName]]</p>",
			because: "the body field carries the decoded HTML (process-macro tokens back in [[param:…]] author form), "
				+ "so a dropped [JsonPropertyName(\"body\")] or a rename would surface here rather than silently");
		email.Importance.Should().Be("high", because: "the importance token maps to the DTO");
		email.IgnoreErrors.Should().BeTrue(because: "the ignore-sending-errors flag maps to the DTO");
		email.To.Should().ContainSingle(because: "the recipient list must survive read-back")
			.Which.Value.Should().Be("to@example.com",
				because: "the recipient's constant address is carried on the parameter's value");
		email.Performer.Type.Should().Be("role",
			because: "the manual-mode performer kind maps to the nested performer DTO");
		email.Performer.RoleDisplay.Should().Be("All employees",
			because: "roleDisplay carries the resolved role name for a human reader");
		email.Performer.ShowPage.Should().BeTrue(
			because: "the show-execution-page flag is part of the performer block");
	}

	[Test]
	[Description("Deserializes a Perform task's TOP-LEVEL performer block (kind, the stored role formula, the resolved role name, the show-page flag) into DescribedElement.Performer, so the team assignment survives read-back instead of being dropped by the clio DTO — the same failure class that once dropped four email fields.")]
	public void Describe_ShouldReadTopLevelPerformer_WhenServerReportsIt() {
		// Arrange — a Perform task carrying a role performer, as CrtProcessBuilder reports it
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000002\",\"name\":\"Task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\",\"userTaskName\":\"ActivityUserTask\","
			+ "\"performer\":{\"type\":\"role\",\"role\":\"[#Lookup.84f44b9a-4bc3-4cbf-a1a8-cec02c1c029c.a29a3ba5-4b0d-de11-9a51-005056c00008#]\",\"roleDisplay\":\"All employees\",\"showPage\":false}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedPerformer performer = result.Value.Elements[0].Performer;
		performer.Should().NotBeNull(
			because: "the top-level performer must be deserialized, not dropped — it is the only read-back of a "
				+ "team assignment, and a dropped member fails silently rather than loudly");
		performer.Type.Should().Be("role", because: "the performer kind maps to the DTO");
		performer.Role.Should().Contain("a29a3ba5-4b0d-de11-9a51-005056c00008",
			because: "the stored role formula is the re-appliable value create/modify accept back");
		performer.RoleDisplay.Should().Be("All employees",
			because: "roleDisplay carries the resolved role name for a human reader");
		performer.ShowPage.Should().BeFalse(
			because: "the show-execution-page flag is part of the block, and false is its designer-parity value "
				+ "for a role performer — reading it as null would lose a written value");
	}

	[Test]
	[Description("Leaves a Perform task's performer null when the server (an older CrtProcessBuilder, or an element with no assignment) reports no block, so absence stays absent instead of materializing an empty performer a caller could mistake for a configured one.")]
	public void Describe_ShouldLeavePerformerNull_WhenServerOmitsIt() {
		// Arrange
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000003\",\"name\":\"Task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\",\"userTaskName\":\"ActivityUserTask\"}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.Value.Elements[0].Performer.Should().BeNull(
			because: "no reported block means no assignment; inventing an empty one would read as configured");
	}

	[Test]
	[Description("Leaves the email block's hasBody unset (null) when an older server omits it, so the absent flag serializes away instead of defaulting to false and reading as a verified 'no body'.")]
	public void Describe_ShouldLeaveEmailHasBodyNull_WhenServerOmitsIt() {
		// Arrange — an older CrtProcessBuilder that reports an email block without the hasBody flag
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"SendEmail1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"sendemail\",\"userTaskName\":\"EmailTemplateUserTask\","
			+ "\"email\":{\"mode\":\"auto\",\"subject\":\"After modify\"}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedEmail email = result.Value.Elements[0].Email;
		email.Should().NotBeNull(because: "the email block itself is still reported by an older server");
		email.HasBody.Should().BeNull(
			because: "an omitted hasBody stays null rather than defaulting to false, which would be indistinguishable "
				+ "from a server that reported 'this element has no custom-message body'. DEFENSIVE only: the server "
				+ "declares hasBody as a non-nullable bool introduced in the same commit as the email block, so no "
				+ "shipped build reports the block without the flag");
		email.IgnoreErrors.Should().BeNull(
			because: "ignoreErrors is genuinely optional on the server (a nullable bool there), so it is the flag that "
				+ "really does arrive absent — hasBody is modelled the same way for safety, not from a known gap");
	}

	[Test]
	[Description("Deserializes a signal start's record trigger — entity, on, and the tracked-change columns array — from the server response into the DescribedSignal DTO, so describe read-back surfaces changedColumns instead of dropping them.")]
	public void Describe_ShouldReadSignalTrackedColumns_WhenServerReportsThem() {
		// Arrange — a signalStart on Order, on:modified, restricted to the Amount + StatusId columns
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"SignalStart1\",\"type\":\"ProcessSchemaStartSignalEvent\",\"buildType\":\"signalstart\","
			+ "\"signal\":{\"entity\":\"Order\",\"entitySchemaUId\":\"5c58c4c4-134b-4744-9c67-96d9c69c9d55\",\"on\":\"modified\",\"changedColumns\":[\"Amount\",\"StatusId\"]}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedSignal signal = result.Value.Elements[0].Signal;
		signal.Should().NotBeNull(because: "a signal start's record trigger must be surfaced, not dropped by the clio DTO");
		signal.Entity.Should().Be("Order", because: "the trigger entity round-trips");
		signal.On.Should().Be("modified", because: "the change type round-trips");
		signal.ChangedColumns.Should().BeEquivalentTo(new[] { "Amount", "StatusId" },
			because: "the tracked-change columns array must be deserialized so describe round-trips them into a build/modify");
	}

	[Test]
	[Description("Leaves the signal's changedColumns null when the server omits them (an any-change signal), so the absent field serializes away cleanly.")]
	public void Describe_ShouldLeaveSignalChangedColumnsNull_WhenServerOmitsThem() {
		// Arrange — an any-change signalStart (no changedColumns)
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"SignalStart1\",\"type\":\"ProcessSchemaStartSignalEvent\",\"buildType\":\"signalstart\","
			+ "\"signal\":{\"entity\":\"Order\",\"on\":\"modified\"}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		result.Value.Elements[0].Signal.ChangedColumns.Should().BeNull(
			because: "an omitted changedColumns stays null so it serializes away (WhenWritingNull) for an any-change signal");
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
			+ "{\"column\":\"Account\",\"comparison\":\"equal\",\"elementParameter\":{\"elementName\":\"task1\",\"parameter\":\"Account\"}}]}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedFilterCondition condition = result.Value.Elements[0].Filter.Conditions[0];
		condition.ElementParameter.Should().NotBeNull(
			because: "the reserved elementParameter field must still deserialize a structured ref for forward-compat, even though the current server does not emit this shape");
		condition.ElementParameter.ElementName.Should().Be("task1",
			because: "the structured reference's element name binds when present");
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

	[Test]
	[Description("Deserializes an element's connections[] entry in full — column, registration state, raw macro and the decoded source — plus the element-level deprecated and writesConnectionsAtRuntime facts, because a member clio's DTO does not declare is dropped SILENTLY and the tool description promises all of them.")]
	public void Describe_ShouldReadConnectionsAndCapabilityFacts_WhenServerReportsThem() {
		// Arrange — one fixed-record connection on a registered column, and both element-level facts
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\","
			+ "\"connections\":[{\"column\":\"Account\",\"registered\":true,\"source\":\"Script\","
			+ "\"value\":\"[#Lookup.c449d832-a4cc-4b01-b9d5-8a12c42a9f89.e308b781-3c5b-4ecb-89ef-5c1ed4da488e#]\","
			+ "\"recordId\":\"e308b781-3c5b-4ecb-89ef-5c1ed4da488e\",\"referenceSchema\":\"Account\"}],"
			+ "\"deprecated\":false,\"writesConnectionsAtRuntime\":true}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		result.IsError.Should().BeFalse(because: "the response is a valid graph");
		DescribedElement element = result.Value.Elements[0];
		element.Connections.Should().HaveCount(1,
			because: "an undeclared collection member deserializes to null, which is how four promised fields were "
				+ "dropped in silence once already");
		DescribedConnection connection = element.Connections[0];
		connection.Column.Should().Be("Account", because: "the column names the connection and is its identity");
		connection.Registered.Should().BeTrue(
			because: "registration is what tells a caller whether the connection is a full citizen or a half one");
		connection.RecordId.Should().Be("e308b781-3c5b-4ecb-89ef-5c1ed4da488e",
			because: "the decoded source is what makes the read-back re-appliable without translating a metapath");
		connection.ReferenceSchema.Should().Be("Account", because: "the entity travels as a NAME the write side takes");
		connection.Value.Should().StartWith("[#Lookup.",
			because: "the raw persisted macro travels alongside the decoded form, which is the forward-compatibility guarantee");
		element.WritesConnectionsAtRuntime.Should().BeTrue(
			because: "this is the verdict a caller is told to read before trusting a binding, so it must reach them");
		element.Deprecated.Should().BeFalse(because: "the retirement fact is reported per element and must not be dropped");
	}

	[Test]
	[Description("Leaves connections/deprecated/writesConnectionsAtRuntime null when an older CrtProcessBuilder omits them, so a stale package degrades to 'not established' rather than to a wrong answer.")]
	public void Describe_ShouldLeaveConnectionsAndCapabilityFactsNull_WhenServerOmitsThem() {
		// Arrange — the pre-connections server shape
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"usertask\"}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);

		// Assert
		DescribedElement element = result.Value.Elements[0];
		element.Connections.Should().BeNull(because: "absent is not empty: nothing was reported, so nothing is claimed");
		element.WritesConnectionsAtRuntime.Should().BeNull(
			because: "null means NOT ESTABLISHED, which is exactly what an older package can say about it");
		element.Deprecated.Should().BeNull(because: "same reason — an omitted fact must not read as false");
	}

	[Test]
	[Description("A field a NEWER server reports that this build does not declare — at the graph root, on an element and inside the email block — survives the clio DTO round trip through [JsonExtensionData] under the command's own serializer options, so clio does not structurally lag the server by a release.")]
	public void Describe_ShouldPreserveUnknownServerFields_WhenReserializingTheGraph() {
		// Arrange — a root fact, an element block and an email-block field the DTOs do not declare. Without an
		// overflow bag each is dropped without a trace, the silent-loss failure the connections DTO calls out.
		// The email case is the one that matters most: that block is where the next email feature lands.
		IApplicationClient client = ClientReturning(
			"{\"DescribeProcessResult\":{\"success\":true,\"name\":\"UsrProc\",\"futureRootFact\":\"kept\","
			+ "\"elements\":[{\"uid\":\"a1b2c3d4-0000-0000-0000-000000000001\",\"name\":\"task1\",\"type\":\"ProcessSchemaUserTask\",\"buildType\":\"sendemail\","
			+ "\"email\":{\"mode\":\"auto\",\"futureEmailFact\":\"kept\"},"
			+ "\"futureBlock\":{\"setting\":42}}],"
			+ "\"flows\":[],\"parameters\":[]}}");
		ServerProcessDescriber describer = CreateDescriber(client);

		// Act — re-serialize with the SAME options DescribeProcessCommand uses for its output (WriteIndented +
		// WhenWritingNull), so this asserts what a caller actually reads rather than a default-options shape.
		JsonSerializerOptions commandOutputOptions = new() {
			WriteIndented = true,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
		};
		ErrorOr<DescribeProcessResult> result = describer.Describe(new ProcessIdentity("UsrProc", null, null), null);
		string reserialized = JsonSerializer.Serialize(result.Value, commandOutputOptions);

		// Assert
		result.IsError.Should().BeFalse(because: "an undeclared member must not fail the read");
		result.Value.AdditionalData.Should().ContainKey("futureRootFact",
			because: "the root overflow bag is what keeps an undeclared server field addressable at all");
		result.Value.Elements[0].AdditionalData.Should().ContainKey("futureBlock",
			because: "an element-level block needs the same protection — that is where a new email/signal-shaped "
				+ "feature lands first");
		result.Value.Elements[0].Email.AdditionalData.Should().ContainKey("futureEmailFact",
			because: "the email block is the feature under active development, so a field a newer server adds there "
				+ "(a template selection, a body format) must not be the one thing that still vanishes");

		// Re-parse rather than string-match: under WriteIndented the exact spacing is a formatting detail, and
		// asserting on it would make this test pass or fail for the wrong reason.
		JsonNode output = JsonNode.Parse(reserialized);
		output["futureRootFact"]!.GetValue<string>().Should().Be("kept",
			because: "capturing it is only half the fix: the describe output is what the caller reads, so the "
				+ "field has to come back out on re-serialization");
		output["elements"]![0]!["futureBlock"]!["setting"]!.GetValue<int>().Should().Be(42,
			because: "the element block must survive verbatim, nesting included, not be flattened or stringified");
		output["elements"]![0]!["email"]!["futureEmailFact"]!.GetValue<string>().Should().Be("kept",
			because: "the email block's unknown field has to reach the output too, not just the in-memory bag");
	}

	// The describer wraps the identity under a "request" property (ProcessDesignService BodyStyle=Wrapped).
	private static JsonNode Wrapped(string body) => JsonNode.Parse(body)["request"];
}
