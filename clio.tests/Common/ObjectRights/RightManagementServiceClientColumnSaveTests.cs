using System;
using System.Linq;
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
/// filtered by the object's schema UId, the level read as the lookup's Value, grouped per column in stored order — and
/// counted first, so a read the server cut short is refused instead of planned on.
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
	private string _rowsQuery;
	private string _countQuery;

	[SetUp]
	public void SetUp() {
		_rowsQuery = null;
		_countQuery = null;
		_applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		_reader = new ColumnRightsStorageReader(_applicationClient, urlBuilder);
	}

	// The count query (an aggregation) gets the count; the rows query gets the rows.
	private void Answers(int? count, string rowsBody) =>
		_applicationClient.ExecutePostRequest(SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(call => {
				string body = (string)call[1];
				if (body.Contains("\"aggregationType\"")) {
					_countQuery = body;
					return "{\"success\":true,\"rows\":[{\"Count\":" + (count?.ToString() ?? "null") + "}]}";
				}
				_rowsQuery = body;
				return rowsBody;
			});

	private static string Rows(params string[] rows) => "{\"success\":true,\"rows\":[" + string.Join(",", rows) + "]}";

	private static string Row(Guid column, Guid grantee, string name, int position, string level) =>
		"{\"SubjectColumnUId\":\"" + column + "\",\"Position\":" + position + ",\"SysAdminUnit\":{\"value\":\"" + grantee
		+ "\",\"displayValue\":\"" + name + "\"},\"RightLevelValue\":" + level + "}";

	[Test]
	[Description("Counts and then queries the storage view by the object's schema UId, reading the level as RightLevel.Value (the lookup's ids differ per site) and asking for one row more than the cap.")]
	public void ReadStoredColumns_ShouldQueryTheStorageView_WhenCalled() {
		// Arrange
		Answers(0, Rows());

		// Act
		_reader.ReadStoredColumns(Schema, new CreatioRequestOptions());

		// Assert
		JsonObject rows = JsonNode.Parse(_rowsQuery)!.AsObject();
		rows["rootSchemaName"]!.GetValue<string>().Should().Be("VwSysEntitySchemaColumnRight",
			because: "the rows are read from storage, not from the filtering service read");
		rows["rowCount"]!.GetValue<int>().Should().Be(ColumnRightsStorageReader.RowCap + 1,
			because: "an answer at the cap is told from one cut off by it");
		_rowsQuery.Should().Contain("\"columnPath\":\"RightLevel.Value\"", because: "the level is the lookup's Value");
		_rowsQuery.Should().Contain(Schema.ToString(), because: "the rows are filtered by the object");
		_countQuery.Should().Contain("VwSysEntitySchemaColumnRight", because: "the count is over the same view")
			.And.Contain(Schema.ToString(), because: "and the same filter");
	}

	[Test]
	[Description("Groups the stored rows per column in stored position order, with the grantee's id and name and the level from its Value.")]
	public void ReadStoredColumns_ShouldGroupRowsPerColumn_WhenRowsAreStored() {
		// Arrange
		Answers(3, Rows(Row(Amount, Role, "Role", 1, "0"), Row(Amount, Employees, "All employees", 0, "2"),
			Row(Description, Role, "Role", 0, "1")));

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
	[Description("An answer with fewer rows than the count — a cap the server applies below the one asked for — is refused, never planned on.")]
	public void ReadStoredColumns_ShouldThrow_WhenTheServerReturnsFewerRowsThanItCounts() {
		// Arrange
		Answers(2, Rows(Row(Amount, Role, "Role", 0, "0")));

		// Act
		Action act = () => _reader.ReadStoredColumns(Schema, new CreatioRequestOptions());

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a short read would send the missed columns with no rows")
			.WithMessage("*returned 1 of the 2 rows*");
	}

	[Test]
	[Description("A count past the cap is refused before the rows are read.")]
	public void ReadStoredColumns_ShouldThrow_WhenTheCountIsPastTheCap() {
		// Arrange
		Answers(ColumnRightsStorageReader.RowCap + 5, Rows());

		// Act
		Action act = () => _reader.ReadStoredColumns(Schema, new CreatioRequestOptions());

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "the rows could not all be read").WithMessage("*more than*");
		_rowsQuery.Should().BeNull(because: "the rows are not read once the count is past the cap");
	}

	[Test]
	[Description("A read-back may skip the count: then only the rows are queried.")]
	public void ReadStoredColumns_ShouldNotCount_WhenVerifyCountIsFalse() {
		// Arrange
		Answers(null, Rows(Row(Amount, Role, "Role", 0, "0")));

		// Act
		var columns = _reader.ReadStoredColumns(Schema, new CreatioRequestOptions(), verifyCount: false);

		// Assert
		columns.Should().ContainSingle(because: "the stored row is read");
		_countQuery.Should().BeNull(because: "no count is asked for");
	}

	[Test]
	[Description("The read by column UId filters on SubjectColumnUId only (no schema filter) and returns each row with its schema, so other objects' rows for inherited columns are told apart.")]
	public void ReadStoredRowsByColumns_ShouldReadByColumnAndKeepTheSchema_WhenCalled() {
		// Arrange
		Guid other = Guid.Parse("41af89e9-750b-4ebb-8cac-ff39b64841ec");
		Answers(null, Rows(
			"{\"SubjectSchemaUId\":\"" + Schema + "\"," + Row(Amount, Role, "Role", 0, "0").Substring(1),
			"{\"SubjectSchemaUId\":\"" + other + "\"," + Row(Amount, Role, "Role", 0, "1").Substring(1)));

		// Act
		var rows = _reader.ReadStoredRowsByColumns([Amount], new CreatioRequestOptions());

		// Assert
		rows.Select(entry => entry.SubjectSchemaUId).Should().BeEquivalentTo(new[] { Schema, other },
			because: "each row keeps the object it is stored for");
		JsonObject filters = JsonNode.Parse(_rowsQuery)!["filters"]!["items"]!.AsObject();
		filters.Select(filter => filter.Value!["leftExpression"]!["columnPath"]!.GetValue<string>()).Should()
			.Equal(new[] { "SubjectColumnUId" }, because: "the read is keyed by column UId only, with no schema filter");
		_rowsQuery.Should().Contain(Amount.ToString(), because: "the filter carries the column UId");
		_countQuery.Should().BeNull(because: "the second read is compared with a counted first read");
	}

	[Test]
	[Description("A failed DataService answer is thrown, never read as no stored rows.")]
	public void ReadStoredColumns_ShouldThrow_WhenTheServiceFails() {
		// Arrange
		Answers(0, "{\"success\":false,\"errorInfo\":{\"message\":\"Access denied\"}}");

		// Act
		Action act = () => _reader.ReadStoredColumns(Schema, new CreatioRequestOptions());

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "an empty list must never stand for a failed read")
			.WithMessage("*Access denied*");
	}
}

/// <summary>
/// <see cref="AdministratedObjectWriteLease"/>: one holder per environment and object across processes — the environment
/// by its resolved, normalised URL — refused at once while held, free again once disposed. Uses a lock folder of its own
/// under the temp directory.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class AdministratedObjectWriteLeaseTests {

	private string _root;

	[SetUp]
	public void SetUp() => _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"clio-object-lease-{Guid.NewGuid():N}");

	[TearDown]
	public void TearDown() {
		if (System.IO.Directory.Exists(_root)) {
			System.IO.Directory.Delete(_root, true);
		}
		if (System.IO.File.Exists(_root)) {
			System.IO.File.Delete(_root);
		}
	}

	private AdministratedObjectWriteLease Lease(string uri) => new(new EnvironmentSettings { Uri = uri }, _root);

	[Test]
	[Description("A second lease on the same object is refused while the first is held — also through another spelling of the same site's URL — and granted once it is disposed; another object or another site is not blocked.")]
	public void Acquire_ShouldRefuseASecondHolder_WhileTheFirstHoldsTheLease() {
		// Arrange
		IDisposable first = Lease("https://dev.example.com").Acquire("UsrHoliday");

		// Act
		Action sameSite = () => Lease("HTTPS://Dev.Example.com/").Acquire("usrholiday").Dispose();
		Action otherObject = () => Lease("https://dev.example.com").Acquire("UsrOrder").Dispose();
		Action otherSite = () => Lease("https://qa.example.com").Acquire("UsrHoliday").Dispose();

		// Assert
		sameSite.Should().Throw<InvalidOperationException>(because: "two writes of one object would revert each other")
			.WithMessage("*must not run in parallel*");
		otherObject.Should().NotThrow(because: "a lease covers one object");
		otherSite.Should().NotThrow(because: "a lease covers one environment");
		first.Dispose();
		Action afterRelease = () => Lease("https://dev.example.com").Acquire("UsrHoliday").Dispose();
		afterRelease.Should().NotThrow(because: "a disposed lease is free again");
	}

	[Test]
	[Description("An IO failure that is not another holder — here, a lock folder that cannot be created — is reported as it is, never as another call running.")]
	public void Acquire_ShouldReportTheIoFailure_WhenTheLockFolderCannotBeCreated() {
		// Arrange
		System.IO.File.WriteAllText(_root, "a file where the lock folder should be");
		AdministratedObjectWriteLease lease = new(new EnvironmentSettings { Uri = "https://dev.example.com" }, _root);

		// Act
		Action act = () => lease.Acquire("UsrHoliday").Dispose();

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "the lock cannot be taken")
			.Where(ex => ex.Message.Contains("cannot take the permissions lock") && !ex.Message.Contains("another call"),
				because: "a failure that is not contention must not be reported as another call");
	}

	[TestCase("https://dev.example.com/", "HTTPS://DEV.EXAMPLE.COM")]
	[TestCase("  https://Dev.Example.com  ", "HTTPS://DEV.EXAMPLE.COM")]
	[TestCase("https://dev.example.com/0/", "HTTPS://DEV.EXAMPLE.COM/0")]
	[Description("The environment key is the resolved URL normalised: case, surrounding spaces and a trailing slash do not make another environment.")]
	public void EnvironmentKey_ShouldNormaliseTheResolvedUrl_WhenWrittenDifferently(string uri, string expected) {
		// Arrange
		EnvironmentSettings settings = new() { Uri = uri };

		// Act
		string key = AdministratedObjectWriteLease.EnvironmentKey(settings);

		// Assert
		key.Should().Be(expected, because: "one site is one environment however its URL is written");
	}
}
