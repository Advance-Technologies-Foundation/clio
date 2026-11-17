using System;
using System.Text.Json.Nodes;
using Clio.Common;
using Clio.Common.ObjectRights;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.ObjectRights;

/// <summary>
/// The <c>SaveAdministratedObject</c> payload of a column save (<see cref="IColumnRightsWriter"/>): the list exactly as
/// planned in the measured shape, <c>null</c> to leave the stored rows alone, and nothing sent for a row it could not
/// send as stored.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class RightManagementServiceClientColumnSaveTests {

	private const string SchemaUId = "35a9057f-800d-414b-acfb-c919c215857a";
	private const string SelectUrl = "http://host/0/DataService/json/SyncReply/SelectQuery";
	private const string GetUrl = "http://host/0/ServiceModel/RightManagementService.svc/GetAdministratedObject";
	private const string SaveUrl = "http://host/0/ServiceModel/RightManagementService.svc/SaveAdministratedObject";

	private static readonly Guid Admins = Guid.Parse("83a43ebc-f36b-1410-298d-001e8c82bcad");
	private static readonly Guid Role = Guid.Parse("11111111-2222-3333-4444-555555555555");
	private static readonly Guid Description = Guid.Parse("9e53fd7c-1111-4c5b-9c3a-2a7b0e7e0001");
	private static readonly Guid Amount = Guid.Parse("9e53fd7c-2222-4c5b-9c3a-2a7b0e7e0002");

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
		Post(GetUrl).Returns("{\"success\":true,\"administratedObject\":{\"name\":\"UsrHoliday\","
			+ "\"administratedByOperations\":true,\"entitySchemaOperationsRights\":[],\"administratedByRecords\":false,"
			+ "\"entitySchemaRecordDefRights\":[],\"administratedByColumns\":false,\"entitySchemaColumnsRights\":[]}}");
		Post(SaveUrl).Returns(callInfo => { _savedPayload = (string)callInfo[1]; return "{\"success\":true}"; });
		_client = new RightManagementServiceClient(_applicationClient, urlBuilder);
	}

	private string Post(string url) =>
		_applicationClient.ExecutePostRequest(url, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());

	private ObjectRightsSnapshot Snapshot() =>
		_client.GetObjectRights("UsrHoliday", new CreatioRequestOptions()).Snapshot;

	private JsonObject Saved() => JsonNode.Parse(_savedPayload)!["administratedObject"]!.AsObject();

	[Test]
	[Description("A list is sent exactly as planned, in the measured shape: each column with columnUId, columnCaption and rights of {position, rightLevel, sysAdminUnit {id, name}}; a column planned with no rows is sent with rights []; the other collections are null.")]
	public void Save_ShouldSendTheListInTheMeasuredShape_WhenColumnsAreGiven() {
		// Arrange
		ColumnRights[] list = [
			new ColumnRights(Amount, "Amount", [
				new ColumnRightRow(Admins, "System administrators", 0, ColumnRightLevel.Edit),
				new ColumnRightRow(Role, "Role", 1, ColumnRightLevel.Denied)
			]),
			new ColumnRights(Description, "Description", [])
		];
		JsonNode expected = JsonNode.Parse("[{\"columnUId\":\"" + Amount + "\",\"columnCaption\":\"Amount\",\"rights\":["
			+ "{\"position\":0,\"rightLevel\":2,\"sysAdminUnit\":{\"id\":\"" + Admins + "\",\"name\":\"System administrators\"}},"
			+ "{\"position\":1,\"rightLevel\":0,\"sysAdminUnit\":{\"id\":\"" + Role + "\",\"name\":\"Role\"}}]},"
			+ "{\"columnUId\":\"" + Description + "\",\"columnCaption\":\"Description\",\"rights\":[]}]");

		// Act
		ObjectRightsSaveResult result = _client.Save(Snapshot(), true, list, new CreatioRequestOptions());

		// Assert
		result.Succeeded.Should().BeTrue(because: "the service answered success");
		JsonObject saved = Saved();
		JsonNode.DeepEquals(saved["entitySchemaColumnsRights"], expected).Should().BeTrue(
			because: $"the list must be sent exactly as planned; sent {saved["entitySchemaColumnsRights"]}");
		saved["administratedByColumns"]!.GetValue<bool>().Should().BeTrue(because: "the planned switch is sent");
		saved["entitySchemaOperationsRights"].Should().BeNull(because: "operation rows are left alone");
		saved["entitySchemaRecordDefRights"].Should().BeNull(because: "record rules are left alone");
		saved["entityOperationGrantees"].Should().BeNull(because: "entity operations are left alone");
	}

	[Test]
	[Description("No list is sent as null, which leaves every stored row alone; an empty list is sent as [], which gives every column the default row.")]
	public void Save_ShouldSendNullOrEmpty_WhenTheListIsNullOrEmpty() {
		// Arrange
		ObjectRightsSnapshot snapshot = Snapshot();

		// Act
		_client.Save(snapshot, false, null, new CreatioRequestOptions());
		JsonObject disabled = Saved();
		_client.Save(snapshot, true, [], new CreatioRequestOptions());
		JsonObject enabled = Saved();

		// Assert
		disabled.ContainsKey("entitySchemaColumnsRights").Should().BeTrue(because: "the key is sent");
		disabled["entitySchemaColumnsRights"].Should().BeNull(because: "null leaves the stored rows alone");
		disabled["administratedByColumns"]!.GetValue<bool>().Should().BeFalse(because: "the switch goes off");
		JsonNode.DeepEquals(enabled["entitySchemaColumnsRights"], new JsonArray()).Should().BeTrue(
			because: "an empty list is sent as []");
	}

	[Test]
	[Description("A row without a position or a level is never sent: nothing goes out.")]
	public void Save_ShouldSendNothing_WhenARowCannotBeSentAsStored() {
		// Arrange
		ColumnRights[] list = [new ColumnRights(Amount, "Amount", [new ColumnRightRow(Role, "Role", null, ColumnRightLevel.Read)])];

		// Act
		ObjectRightsSaveResult result = _client.Save(Snapshot(), true, list, new CreatioRequestOptions());

		// Assert
		result.Succeeded.Should().BeFalse(because: "the row cannot be sent as stored");
		result.Error.Should().Contain("nothing was sent", because: "the refusal says nothing changed");
		_savedPayload.Should().BeNull(because: "no save request may go out");
	}
}

/// <summary>
/// <see cref="ColumnRightsStorageReader"/>: the stored rows from <c>VwSysEntitySchemaColumnRight</c> through DataService,
/// filtered by the object's schema UId, the level read as the lookup's Value, grouped per column in stored order.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class ColumnRightsStorageReaderTests {

	private const string SelectUrl = "http://host/0/DataService/json/SyncReply/SelectQuery";

	private static readonly Guid Schema = Guid.Parse("35a9057f-800d-414b-acfb-c919c215857a");
	private static readonly Guid Employees = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");
	private static readonly Guid Role = Guid.Parse("11111111-2222-3333-4444-555555555555");
	private static readonly Guid Description = Guid.Parse("9e53fd7c-1111-4c5b-9c3a-2a7b0e7e0001");
	private static readonly Guid Amount = Guid.Parse("9e53fd7c-2222-4c5b-9c3a-2a7b0e7e0002");

	private IApplicationClient _applicationClient;
	private ColumnRightsStorageReader _reader;
	private string _query;

	[SetUp]
	public void SetUp() {
		_query = null;
		_applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		_reader = new ColumnRightsStorageReader(_applicationClient, urlBuilder);
	}

	private void Answers(string body) =>
		_applicationClient.ExecutePostRequest(SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(call => { _query = (string)call[1]; return body; });

	private static string Row(Guid column, Guid grantee, string name, int position, string level) =>
		"{\"SubjectColumnUId\":\"" + column + "\",\"Position\":" + position + ",\"SysAdminUnit\":{\"value\":\"" + grantee
		+ "\",\"displayValue\":\"" + name + "\"},\"RightLevelValue\":" + level + "}";

	[Test]
	[Description("Queries the storage view by the object's schema UId, reading the level as RightLevel.Value (the lookup's ids differ per site).")]
	public void ReadStoredColumns_ShouldQueryTheStorageView_WhenCalled() {
		// Arrange
		Answers("{\"success\":true,\"rows\":[]}");

		// Act
		_reader.ReadStoredColumns(Schema, new CreatioRequestOptions());

		// Assert
		JsonObject query = JsonNode.Parse(_query)!.AsObject();
		query["rootSchemaName"]!.GetValue<string>().Should().Be("VwSysEntitySchemaColumnRight",
			because: "the rows are read from storage, not from the filtering service read");
		_query.Should().Contain("\"columnPath\":\"RightLevel.Value\"", because: "the level is the lookup's Value");
		_query.Should().Contain("\"columnPath\":\"SubjectSchemaUId\"", because: "the rows are filtered by the object");
		_query.Should().Contain(Schema.ToString(), because: "the filter carries the schema UId");
	}

	[Test]
	[Description("Groups the stored rows per column in stored position order, with the grantee's id and name and the level from its Value.")]
	public void ReadStoredColumns_ShouldGroupRowsPerColumn_WhenRowsAreStored() {
		// Arrange
		Answers("{\"success\":true,\"rows\":[" + Row(Amount, Role, "Role", 1, "0") + ","
			+ Row(Amount, Employees, "All employees", 0, "2") + "," + Row(Description, Role, "Role", 0, "1") + "]}");

		// Act
		var columns = _reader.ReadStoredColumns(Schema, new CreatioRequestOptions());

		// Assert
		columns.Should().BeEquivalentTo(new[] {
			new ColumnRights(Amount, null, [
				new ColumnRightRow(Employees, "All employees", 0, ColumnRightLevel.Edit),
				new ColumnRightRow(Role, "Role", 1, ColumnRightLevel.Denied)
			]),
			new ColumnRights(Description, null, [new ColumnRightRow(Role, "Role", 0, ColumnRightLevel.Read)])
		}, options => options.WithStrictOrdering(), because: "rows group per column, in stored position order");
	}

	[Test]
	[Description("A failed DataService answer is thrown, never read as no stored rows.")]
	public void ReadStoredColumns_ShouldThrow_WhenTheServiceFails() {
		// Arrange
		Answers("{\"success\":false,\"errorInfo\":{\"message\":\"Access denied\"}}");

		// Act
		Action act = () => _reader.ReadStoredColumns(Schema, new CreatioRequestOptions());

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "an empty list must never stand for a failed read")
			.WithMessage("*Access denied*");
	}
}
