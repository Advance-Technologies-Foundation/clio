using System;
using System.Linq;
using Clio.Common;
using Clio.Common.ObjectRights;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.ObjectRights;

/// <summary>
/// The column layer of <see cref="RightManagementServiceClient.GetObjectRights"/>: <c>administratedByColumns</c> and
/// <c>entitySchemaColumnsRights</c> projected onto <see cref="ColumnRightsState"/>, in the shape measured on a live
/// service (columns with <c>columnUId</c>, <c>columnCaption</c> and <c>rights</c> of <c>position</c>,
/// <c>rightLevel</c> and <c>sysAdminUnit</c>; a row read back carries no id).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class RightManagementServiceClientColumnTests {

	private const string SchemaUId = "35a9057f-800d-414b-acfb-c919c215857a";
	private const string SelectUrl = "http://host/0/DataService/json/SyncReply/SelectQuery";
	private const string GetUrl = "http://host/0/ServiceModel/RightManagementService.svc/GetAdministratedObject";

	private static readonly Guid Employees = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");
	private static readonly Guid Admins = Guid.Parse("83a43ebc-f36b-1410-298d-001e8c82bcad");
	private static readonly Guid Description = Guid.Parse("9e53fd7c-1111-4c5b-9c3a-2a7b0e7e0001");
	private static readonly Guid Amount = Guid.Parse("9e53fd7c-2222-4c5b-9c3a-2a7b0e7e0002");

	private IApplicationClient _applicationClient;
	private RightManagementServiceClient _client;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetAdministratedObject).Returns(GetUrl);
		Post(SelectUrl).Returns($"{{\"success\":true,\"rows\":[{{\"UId\":\"{SchemaUId}\"}}]}}");
		_client = new RightManagementServiceClient(_applicationClient, urlBuilder);
	}

	private string Post(string url) =>
		_applicationClient.ExecutePostRequest(url, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());

	// administratedByColumns is the switch as JSON; null leaves the field out of the answer.
	private ColumnRightsState Read(string columnsJson, string administratedByColumns = "true") {
		Post(GetUrl).Returns("{\"success\":true,\"administratedObject\":{\"name\":\"UsrHoliday\","
			+ "\"administratedByOperations\":false,\"entitySchemaOperationsRights\":[]"
			+ (administratedByColumns is null ? "" : ",\"administratedByColumns\":" + administratedByColumns)
			+ (columnsJson is null ? "" : ",\"entitySchemaColumnsRights\":" + columnsJson) + "}}");
		ObjectRightsInfo info = _client.GetObjectRights("UsrHoliday", new CreatioRequestOptions());
		info.IsRead.Should().BeTrue(because: "the service answered the read");
		return info.ColumnState;
	}

	private static string Column(Guid uId, string caption, params string[] rows) =>
		"{\"columnUId\":\"" + uId + "\",\"columnCaption\":\"" + caption + "\",\"rights\":[" + string.Join(",", rows)
		+ "]}";

	// position and level are left out of the row when null; level is the rightLevel as JSON.
	private static string Row(Guid grantee, string name, int? position, string level) =>
		"{\"sysAdminUnit\":{\"id\":\"" + grantee + "\",\"name\":\"" + name + "\"}"
		+ (position is null ? "" : ",\"position\":" + position) + (level is null ? "" : ",\"rightLevel\":" + level) + "}";

	[Test]
	[Description("Projects the measured shape: the switch, each column's UId and caption, and each row's grantee, position and level.")]
	public void GetObjectRights_ShouldProjectColumnRights_WhenServiceReturnsColumns() {
		// Arrange
		string columns = "[" + Column(Description, "Description", Row(Employees, "All employees", 0, "1")) + "]";

		// Act
		ColumnRightsState state = Read(columns);

		// Assert
		state.AdministratedByColumns.Should().BeTrue(because: "administratedByColumns is the switch");
		state.Columns.Should().BeEquivalentTo(new[] {
			new ColumnRights(Description, "Description",
				new[] { new ColumnRightRow(Employees, "All employees", 0, ColumnRightLevel.Read) })
		}, because: "every wire field lands on the model, rightLevel 1 being Reading");
	}

	[Test]
	[Description("Rows come back in stored position order (lowest position first) whatever order the service lists them in, with every level the lookup knows.")]
	public void GetObjectRights_ShouldOrderColumnRowsByPosition_WhenServiceListsThemOutOfOrder() {
		// Arrange
		string columns = "[" + Column(Amount, "Amount",
			Row(Employees, "All employees", 2, "0"),
			Row(Admins, "System administrators", 0, "2"),
			Row(Employees, "All employees", 1, "1")) + "]";

		// Act
		ColumnRightsState state = Read(columns);

		// Assert
		state.Columns.Single().Rows.Should().BeEquivalentTo(new[] {
			new ColumnRightRow(Admins, "System administrators", 0, ColumnRightLevel.Edit),
			new ColumnRightRow(Employees, "All employees", 1, ColumnRightLevel.Read),
			new ColumnRightRow(Employees, "All employees", 2, ColumnRightLevel.Denied)
		}, options => options.WithStrictOrdering(), because: "rows are listed in stored position order and 0/1/2 are denied/read/edit");
	}

	[Test]
	[Description("A rightLevel outside the lookup is kept as the number it is, and a row with no rightLevel has no level: neither is shown as a level the service did not return.")]
	public void GetObjectRights_ShouldKeepUnknownAndMissingLevels_WhenRowsCarryThem() {
		// Arrange
		string columns = "[" + Column(Amount, "Amount",
			Row(Employees, "All employees", 0, "7"),
			Row(Admins, "System administrators", 1, null)) + "]";

		// Act
		ColumnRightsState state = Read(columns);

		// Assert
		ColumnRightRow[] rows = state.Columns.Single().Rows.ToArray();
		((int?)rows[0].Level).Should().Be(7, because: "an unknown stored level must survive as itself");
		rows[1].Level.Should().BeNull(because: "an absent level is not a level");
		ColumnRightLevelNames.Of(rows[0].Level).Should().Be("unknown (7)", because: "the output names the number it got");
		ColumnRightLevelNames.Of(rows[1].Level).Should().Be("unknown (no level)", because: "the output says no level came");
	}

	[TestCase("[]")]
	[TestCase("null")]
	[TestCase(null)]
	[Description("An empty, null or absent column collection reads as no column rows, and the switch is still read.")]
	public void GetObjectRights_ShouldReadNoColumns_WhenCollectionIsEmptyNullOrAbsent(string columnsJson) {
		// Arrange & Act
		ColumnRightsState state = Read(columnsJson);

		// Assert
		state.AdministratedByColumns.Should().BeTrue(because: "the switch is read on its own");
		state.Columns.Should().BeEmpty(because: "no column has rows");
		state.RestrictedColumns.Should().BeEmpty(because: "no column has rows");
	}

	[Test]
	[Description("The switch OFF is read as OFF, and rows stored while it is off are still projected.")]
	public void GetObjectRights_ShouldReadSwitchOff_WhenAdministratedByColumnsIsFalse() {
		// Arrange
		string columns = "[" + Column(Description, "Description", Row(Employees, "All employees", 0, "0")) + "]";

		// Act
		ColumnRightsState state = Read(columns, administratedByColumns: "false");

		// Assert
		state.AdministratedByColumns.Should().BeFalse(because: "administratedByColumns false is OFF");
		state.RestrictedColumns.Should().ContainSingle(because: "a stored row is a fact even while the switch is off");
	}

	[Test]
	[Description("A column whose rights list is null or empty has no rows and is not a restricted column; a column entry that is not an object is skipped.")]
	public void GetObjectRights_ShouldTreatColumnWithoutRightsAsUnrestricted_WhenRightsAreNullOrEmpty() {
		// Arrange
		string columns = "[{\"columnUId\":\"" + Description + "\",\"columnCaption\":\"Description\",\"rights\":null},"
			+ Column(Amount, "Amount") + ",42]";

		// Act
		ColumnRightsState state = Read(columns);

		// Assert
		state.Columns.Should().HaveCount(2, because: "both column objects are projected, the number is skipped");
		state.RestrictedColumns.Should().BeEmpty(because: "neither column has a row");
	}

	[Test]
	[Description("A failed read carries no column state: its column switch is unknown (null), never OFF, and it has no column rows.")]
	public void GetObjectRights_ShouldCarryNoColumnState_WhenServiceFails() {
		// Arrange
		Post(GetUrl).Returns("{\"success\":false,\"errorInfo\":{\"message\":\"Access denied\"}}");

		// Act
		ObjectRightsInfo info = _client.GetObjectRights("UsrHoliday", new CreatioRequestOptions());

		// Assert
		info.IsRead.Should().BeFalse(because: "the service reported a failure");
		info.ReadError.Should().Contain("Access denied", because: "the service's reason is kept");
		info.AdministratedByColumns.Should().BeNull(because: "a failed read never says column permissions are OFF");
		info.Columns.Should().BeNull(because: "a failed read has no column rows to report");
		info.ColumnState.AdministratedByColumns.Should().BeNull(because: "the column layer of a failed read is unknown");
	}

	[TestCase(null)]
	[TestCase("null")]
	[TestCase("\"true\"")]
	[Description("An answer without a boolean administratedByColumns leaves the column switch unknown (null), never OFF, and its rows are still read.")]
	public void GetObjectRights_ShouldLeaveColumnSwitchUnknown_WhenFieldIsMissingOrNotBoolean(string switchJson) {
		// Arrange
		string columns = "[" + Column(Amount, "Amount", Row(Employees, "All employees", 0, "0")) + "]";

		// Act
		ColumnRightsState state = Read(columns, administratedByColumns: switchJson);

		// Assert
		state.AdministratedByColumns.Should().BeNull(because: "the service did not return a readable column switch");
		state.RestrictedColumns.Should().ContainSingle(because: "the stored rows are read without the switch");
	}

	[Test]
	[Description("A row without a readable position keeps no position (null, not its index) and is placed after the rows with one, in the order the service listed it.")]
	public void GetObjectRights_ShouldPlaceRowsWithoutPositionLast_WhenPositionIsMissing() {
		// Arrange
		string columns = "[" + Column(Amount, "Amount",
			Row(Admins, "System administrators", null, "2"),
			Row(Employees, "All employees", 3, "0"),
			Row(Guid.Empty, "Nobody", null, "1"),
			Row(Admins, "System administrators", 1, "1")) + "]";

		// Act
		ColumnRightsState state = Read(columns);

		// Assert
		state.Columns.Single().Rows.Select(row => (row.Position, row.Level)).Should().Equal(new (int?, ColumnRightLevel?)[] {
			(1, ColumnRightLevel.Read), (3, ColumnRightLevel.Denied), (null, ColumnRightLevel.Edit), (null, ColumnRightLevel.Read)
		}, because: "rows with a position come first by position; rows without one keep no invented position and follow in service order");
	}

	[Test]
	[Description("A rightLevel the service returns as text that is a number is read as that number.")]
	public void GetObjectRights_ShouldReadTheLevel_WhenRightLevelIsNumericText() {
		// Arrange
		string columns = "[" + Column(Amount, "Amount", Row(Employees, "All employees", 0, "\"1\"")) + "]";

		// Act
		ColumnRightsState state = Read(columns);

		// Assert
		ColumnRightRow row = state.Columns.Single().Rows.Single();
		row.Level.Should().Be(ColumnRightLevel.Read, because: "\"1\" is the number 1, Reading");
		ColumnRightLevelNames.Of(row).Should().Be("read", because: "a numeric text level is a known level");
	}

	[TestCase("\"abc\"", "unknown (abc)")]
	[TestCase("{\"value\":1}", "unknown ({\"value\":1})")]
	[TestCase("null", "unknown (null)")]
	[TestCase("1.5", "unknown (1.5)")]
	[Description("A rightLevel that is present but not a number is kept as the service returned it and named unknown (that value), never unknown (no level), which is only for an absent level.")]
	public void GetObjectRights_ShouldKeepTheReturnedValue_WhenRightLevelIsNotANumber(string levelJson, string expected) {
		// Arrange
		string columns = "[" + Column(Amount, "Amount", Row(Employees, "All employees", 0, levelJson)) + "]";

		// Act
		ColumnRightsState state = Read(columns);

		// Assert
		ColumnRightRow row = state.Columns.Single().Rows.Single();
		row.Level.Should().BeNull(because: "a value that is not a number is no level");
		ColumnRightLevelNames.Of(row).Should().Be(expected, because: "the output shows what the service returned");
	}

	[Test]
	[Description("Entries the service lists for the same columnUId are one column: their rows are joined in stored position order, at the place of the first entry, with its caption.")]
	public void GetObjectRights_ShouldMergeEntriesOfOneColumn_WhenServiceRepeatsAColumnUId() {
		// Arrange
		string columns = "[" + Column(Amount, "Amount", Row(Employees, "All employees", 2, "0")) + ","
			+ Column(Description, "Description", Row(Employees, "All employees", 0, "1")) + ","
			+ Column(Amount, "Amount again", Row(Admins, "System administrators", 0, "2")) + "]";

		// Act
		ColumnRightsState state = Read(columns);

		// Assert
		state.Columns.Select(column => column.ColumnUId).Should().Equal(new Guid?[] { Amount, Description },
			because: "one entry per column, in the order of each column's first entry");
		ColumnRights amount = state.Columns[0];
		amount.ColumnCaption.Should().Be("Amount", because: "the first entry's caption is kept");
		amount.Rows.Should().BeEquivalentTo(new[] {
			new ColumnRightRow(Admins, "System administrators", 0, ColumnRightLevel.Edit),
			new ColumnRightRow(Employees, "All employees", 2, ColumnRightLevel.Denied)
		}, options => options.WithStrictOrdering(), because: "the rows of both entries are joined in stored position order");
	}

	[Test]
	[Description("A columnUId that is not a GUID is kept as the service returned it, and two such entries are never merged into one column (nor into Guid.Empty).")]
	public void GetObjectRights_ShouldKeepEachUnreadableColumnUIdApart_WhenUIdIsNotAGuid() {
		// Arrange
		string columns = "[{\"columnUId\":\"not-a-guid\",\"columnCaption\":\"First\",\"rights\":["
			+ Row(Employees, "All employees", 0, "0") + "]},"
			+ "{\"columnUId\":42,\"columnCaption\":\"Second\",\"rights\":[" + Row(Admins, "System administrators", 0, "1")
			+ "]},{\"columnCaption\":\"Third\",\"rights\":[" + Row(Admins, "System administrators", 0, "0") + "]}]";

		// Act
		ColumnRightsState state = Read(columns);

		// Assert
		state.Columns.Should().HaveCount(3, because: "entries whose UId is not readable are not one column");
		state.Columns.Select(column => column.ColumnUId).Should().AllSatisfy(uId => uId.Should().BeNull(
			because: "no GUID was returned, so none is invented"));
		state.Columns.Select(column => column.UnreadableUId).Should().Equal(new[] { "not-a-guid", "42", null },
			because: "the UId is kept as returned, and an absent one stays absent");
	}
}
