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

	private ColumnRightsState Read(string columnsJson, bool administratedByColumns = true) {
		Post(GetUrl).Returns("{\"success\":true,\"administratedObject\":{\"name\":\"UsrHoliday\","
			+ "\"administratedByOperations\":false,\"entitySchemaOperationsRights\":[],"
			+ "\"administratedByColumns\":" + (administratedByColumns ? "true" : "false")
			+ (columnsJson is null ? "" : ",\"entitySchemaColumnsRights\":" + columnsJson) + "}}");
		ObjectRightsInfo info = _client.GetObjectRights("UsrHoliday", new CreatioRequestOptions());
		info.IsRead.Should().BeTrue(because: "the service answered the read");
		return info.ColumnState;
	}

	private static string Column(Guid uId, string caption, params string[] rows) =>
		"{\"columnUId\":\"" + uId + "\",\"columnCaption\":\"" + caption + "\",\"rights\":[" + string.Join(",", rows)
		+ "]}";

	private static string Row(Guid grantee, string name, int position, string level) =>
		"{\"position\":" + position + (level is null ? "" : ",\"rightLevel\":" + level)
		+ ",\"sysAdminUnit\":{\"id\":\"" + grantee + "\",\"name\":\"" + name + "\"}}";

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
	[Description("Rows come back in priority order (lowest position first) whatever order the service lists them in, with every level the lookup knows.")]
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
		}, options => options.WithStrictOrdering(), because: "rows are listed in priority order and 0/1/2 are denied/read/edit");
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
		ColumnRightsState state = Read(columns, administratedByColumns: false);

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
	[Description("A failed read carries no column state: an object whose read failed is never reported with column permissions OFF.")]
	public void GetObjectRights_ShouldReportReadError_WhenServiceFails() {
		// Arrange
		Post(GetUrl).Returns("{\"success\":false,\"errorInfo\":{\"message\":\"Access denied\"}}");

		// Act
		ObjectRightsInfo info = _client.GetObjectRights("UsrHoliday", new CreatioRequestOptions());

		// Assert
		info.IsRead.Should().BeFalse(because: "the service reported a failure");
		info.ReadError.Should().Contain("Access denied", because: "the service's reason is kept");
	}
}
