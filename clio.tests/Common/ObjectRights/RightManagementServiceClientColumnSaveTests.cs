using System;
using System.Text.Json.Nodes;
using Clio.Common;
using Clio.Common.ObjectRights;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.ObjectRights;

/// <summary>
/// The <c>SaveAdministratedObject</c> payload of a column save (<see cref="IColumnRightsWriter"/>). Measured: a column
/// list REPLACES every column's rows, <c>[]</c> clears them all and <c>null</c> leaves them alone — so a change to one
/// column sends the whole list with only that column changed, and a switch-only save sends null.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class RightManagementServiceClientColumnSaveTests {

	private const string SchemaUId = "35a9057f-800d-414b-acfb-c919c215857a";
	private const string SelectUrl = "http://host/0/DataService/json/SyncReply/SelectQuery";
	private const string GetUrl = "http://host/0/ServiceModel/RightManagementService.svc/GetAdministratedObject";
	private const string SaveUrl = "http://host/0/ServiceModel/RightManagementService.svc/SaveAdministratedObject";

	private static readonly Guid Employees = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");
	private static readonly Guid Admins = Guid.Parse("83a43ebc-f36b-1410-298d-001e8c82bcad");
	private static readonly Guid Role = Guid.Parse("11111111-2222-3333-4444-555555555555");
	private static readonly Guid Description = Guid.Parse("9e53fd7c-1111-4c5b-9c3a-2a7b0e7e0001");
	private static readonly Guid Amount = Guid.Parse("9e53fd7c-2222-4c5b-9c3a-2a7b0e7e0002");

	// The read: Description has one row carrying a field the projection does not know; Amount has two rows.
	private static readonly string DescriptionNode = "{\"columnUId\":\"" + Description + "\",\"columnCaption\":\"Description\","
		+ "\"rights\":[{\"position\":0,\"rightLevel\":1,\"sysAdminUnit\":{\"id\":\"" + Employees
		+ "\",\"name\":\"All employees\"},\"extra\":\"kept\"}]}";

	private static readonly string AmountNode = "{\"columnUId\":\"" + Amount + "\",\"columnCaption\":\"Amount\","
		+ "\"rights\":[{\"position\":0,\"rightLevel\":2,\"sysAdminUnit\":{\"id\":\"" + Admins
		+ "\",\"name\":\"System administrators\"}},{\"position\":1,\"rightLevel\":0,\"sysAdminUnit\":{\"id\":\""
		+ Employees + "\",\"name\":\"All employees\"}}]}";

	private IApplicationClient _applicationClient;
	private RightManagementServiceClient _client;
	private string _savedPayload;

	[SetUp]
	public void SetUp() {
		_savedPayload = null;
		_applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetAdministratedObject).Returns(GetUrl);
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveAdministratedObject).Returns(SaveUrl);
		Post(SelectUrl).Returns($"{{\"success\":true,\"rows\":[{{\"UId\":\"{SchemaUId}\"}}]}}");
		Post(SaveUrl).Returns(callInfo => { _savedPayload = (string)callInfo[1]; return "{\"success\":true}"; });
		_client = new RightManagementServiceClient(_applicationClient, urlBuilder);
	}

	private string Post(string url) =>
		_applicationClient.ExecutePostRequest(url, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());

	private ObjectRightsInfo Read(string columnsJson) {
		Post(GetUrl).Returns("{\"success\":true,\"administratedObject\":{\"name\":\"UsrHoliday\","
			+ "\"administratedByOperations\":true,\"entitySchemaOperationsRights\":[],\"administratedByRecords\":false,"
			+ "\"entitySchemaRecordDefRights\":[],\"administratedByColumns\":true,\"entitySchemaColumnsRights\":"
			+ columnsJson + "}}");
		return _client.GetObjectRights("UsrHoliday", new CreatioRequestOptions());
	}

	private JsonObject Saved() => JsonNode.Parse(_savedPayload)!["administratedObject"]!.AsObject();

	private static ColumnRightRow Row(Guid grantee, string name, int position, ColumnRightLevel level) =>
		new(grantee, name, position, level);

	[Test]
	[Description("A change to one column sends the WHOLE list: the other column exactly as read (an unknown field included) and the changed column with its planned rows — never a partial list, which would wipe the others.")]
	public void Save_ShouldSendTheWholeListWithOnlyTheChangedColumn_WhenOneColumnChanges() {
		// Arrange
		ObjectRightsInfo read = Read($"[{DescriptionNode},{AmountNode}]");
		ColumnRightsState after = new(true, [
			read.ColumnState.Columns[0],
			new ColumnRights(Amount, "Amount", [
				Row(Admins, "System administrators", 0, ColumnRightLevel.Edit),
				Row(Employees, "All employees", 1, ColumnRightLevel.Denied),
				Row(Role, "Role", 2, ColumnRightLevel.Read)
			])
		]);
		JsonNode expected = JsonNode.Parse($"[{DescriptionNode},"
			+ "{\"columnUId\":\"" + Amount + "\",\"columnCaption\":\"Amount\",\"rights\":["
			+ "{\"position\":0,\"rightLevel\":2,\"sysAdminUnit\":{\"id\":\"" + Admins + "\",\"name\":\"System administrators\"}},"
			+ "{\"position\":1,\"rightLevel\":0,\"sysAdminUnit\":{\"id\":\"" + Employees + "\",\"name\":\"All employees\"}},"
			+ "{\"sysAdminUnit\":{\"id\":\"" + Role + "\",\"name\":\"Role\"},\"position\":2,\"rightLevel\":1}]}]");

		// Act
		ObjectRightsSaveResult result = _client.Save(read.Snapshot, after, Amount, new CreatioRequestOptions());

		// Assert
		result.Succeeded.Should().BeTrue(because: "the service answered success");
		JsonObject saved = Saved();
		JsonNode.DeepEquals(saved["entitySchemaColumnsRights"], expected).Should().BeTrue(
			because: $"the list must be the read Description column unchanged plus Amount with the new row; sent {saved["entitySchemaColumnsRights"]}");
		saved["administratedByColumns"]!.GetValue<bool>().Should().BeTrue(because: "the planned switch is sent");
		saved["entitySchemaOperationsRights"].Should().BeNull(because: "operation rows are left alone");
		saved["entitySchemaRecordDefRights"].Should().BeNull(because: "record rules are left alone");
		saved["entityOperationGrantees"].Should().BeNull(because: "entity operations are left alone");
	}

	[Test]
	[Description("A switch-only save sends the column list as null, which leaves every stored row alone.")]
	public void Save_ShouldSendNullColumns_WhenTheCallOnlyTurnsTheSwitch() {
		// Arrange
		ObjectRightsInfo read = Read($"[{AmountNode}]");

		// Act
		_client.Save(read.Snapshot, read.ColumnState with { AdministratedByColumns = false }, null,
			new CreatioRequestOptions());

		// Assert
		JsonObject saved = Saved();
		saved.ContainsKey("entitySchemaColumnsRights").Should().BeTrue(because: "the key is sent");
		saved["entitySchemaColumnsRights"].Should().BeNull(because: "null leaves the stored column rows alone");
		saved["administratedByColumns"]!.GetValue<bool>().Should().BeFalse(because: "the switch goes off");
	}

	[Test]
	[Description("A column the plan empties is left out of the list; when no column is left the list is [] (clear all).")]
	public void Save_ShouldLeaveAnEmptiedColumnOut_WhenItsLastRowIsRemoved() {
		// Arrange
		ObjectRightsInfo read = Read($"[{AmountNode}]");

		// Act
		_client.Save(read.Snapshot, new ColumnRightsState(true, []), Amount, new CreatioRequestOptions());

		// Assert
		JsonNode.DeepEquals(Saved()["entitySchemaColumnsRights"], new JsonArray()).Should().BeTrue(
			because: "the only column has no rows left, so the list is empty and clears it");
	}

	[Test]
	[Description("A column that had no rows is added after the columns read, in the measured row shape.")]
	public void Save_ShouldAddTheColumn_WhenItHadNoRows() {
		// Arrange
		ObjectRightsInfo read = Read($"[{DescriptionNode}]");
		ColumnRightsState after = new(true, [
			read.ColumnState.Columns[0],
			new ColumnRights(Amount, "Amount", [Row(Role, "Role", 0, ColumnRightLevel.Denied)])
		]);

		// Act
		_client.Save(read.Snapshot, after, Amount, new CreatioRequestOptions());

		// Assert
		JsonNode expected = JsonNode.Parse($"[{DescriptionNode},"
			+ "{\"columnUId\":\"" + Amount + "\",\"columnCaption\":\"Amount\",\"rights\":["
			+ "{\"sysAdminUnit\":{\"id\":\"" + Role + "\",\"name\":\"Role\"},\"position\":0,\"rightLevel\":0}]}]");
		JsonNode.DeepEquals(Saved()["entitySchemaColumnsRights"], expected).Should().BeTrue(
			because: "the new column carries columnUId, columnCaption and rights of {position, rightLevel, sysAdminUnit}");
	}

	[Test]
	[Description("Columns the call does not change go back as their RAW read nodes: a repeated entry stays repeated (never merged), an entry whose UId is not a GUID and a row without a position or with an unreadable level stay exactly as read.")]
	public void Save_ShouldSendUnchangedColumnsAsRawNodes_WhenTheReadIsIrregular() {
		// Arrange
		string duplicate = "{\"columnUId\":\"" + Description + "\",\"columnCaption\":\"Description\",\"rights\":["
			+ "{\"rightLevel\":\"x\",\"sysAdminUnit\":{\"id\":\"" + Admins + "\",\"name\":\"System administrators\"}}]}";
		string unreadableUId = "{\"columnUId\":\"not-a-guid\",\"columnCaption\":\"Odd\",\"rights\":[]}";
		ObjectRightsInfo read = Read($"[{DescriptionNode},{unreadableUId},{duplicate}]");
		ColumnRightsState after = new(true, [
			.. read.ColumnState.Columns,
			new ColumnRights(Amount, "Amount", [Row(Role, "Role", 0, ColumnRightLevel.Denied)])
		]);

		// Act
		_client.Save(read.Snapshot, after, Amount, new CreatioRequestOptions());

		// Assert
		JsonNode expected = JsonNode.Parse($"[{DescriptionNode},{unreadableUId},{duplicate},"
			+ "{\"columnUId\":\"" + Amount + "\",\"columnCaption\":\"Amount\",\"rights\":["
			+ "{\"sysAdminUnit\":{\"id\":\"" + Role + "\",\"name\":\"Role\"},\"position\":0,\"rightLevel\":0}]}]");
		JsonNode.DeepEquals(Saved()["entitySchemaColumnsRights"], expected).Should().BeTrue(
			because: $"every unchanged entry must go back exactly as read, in its order; sent {Saved()["entitySchemaColumnsRights"]}");
	}

	[Test]
	[Description("When the service lists the changed column twice, nothing is sent: which entry's rows to replace is ambiguous.")]
	public void Save_ShouldSendNothing_WhenTheChangedColumnIsListedTwice() {
		// Arrange
		ObjectRightsInfo read = Read($"[{AmountNode},{AmountNode}]");
		ColumnRightsState after = new(true, [
			new ColumnRights(Amount, "Amount", [Row(Role, "Role", 0, ColumnRightLevel.Denied)])
		]);

		// Act
		ObjectRightsSaveResult result = _client.Save(read.Snapshot, after, Amount, new CreatioRequestOptions());

		// Assert
		result.Succeeded.Should().BeFalse(because: "the rows to replace are ambiguous");
		result.Error.Should().Contain("nothing was sent", because: "the refusal says nothing changed");
		result.OutcomeUnknown.Should().BeFalse(because: "a save that was never sent has a known outcome");
		_savedPayload.Should().BeNull(because: "no save request may go out");
	}
}
