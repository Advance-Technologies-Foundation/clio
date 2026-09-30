using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.ObjectRights;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.ObjectRights;

/// <summary>
/// Direct unit tests for <see cref="RightManagementServiceClient"/> — reading an object's rows in priority order
/// through GetAdministratedObject (candidate UIds, faults) and saving a planned state through
/// SaveAdministratedObject. The client holds no policy: what to write is the planner's job, so these tests assert
/// the read projection and the exact save payload.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class RightManagementServiceClientTests {

	private const string SchemaUId = "35a9057f-800d-414b-acfb-c919c215857a";
	private static readonly Guid Grantee = Guid.Parse("720b771c-e7a7-4f31-9cfb-52cd21c3739f");
	private static readonly Guid Employees = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");

	private const string SelectUrl = "http://host/0/DataService/json/SyncReply/SelectQuery";
	private const string GetUrl = "http://host/0/ServiceModel/RightManagementService.svc/GetAdministratedObject";
	private const string SaveUrl = "http://host/0/ServiceModel/RightManagementService.svc/SaveAdministratedObject";

	private const string BaseUId = "11111111-1111-1111-1111-111111111111";
	private const string LayerUId = "22222222-2222-2222-2222-222222222222";
	private const string InBandJsonFault = "{\"success\":false,\"errorInfo\":{\"message\":\"Request Error\"}}";
	private const string HtmlRequestErrorPage =
		"<?xml version=\"1.0\" encoding=\"utf-8\"?><!DOCTYPE html><html><body>Request Error</body></html>";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _urlBuilder;
	private RightManagementServiceClient _client;
	private string _savedPayload;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_urlBuilder = Substitute.For<IServiceUrlBuilder>();
		_urlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
		_urlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetAdministratedObject).Returns(GetUrl);
		_urlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveAdministratedObject).Returns(SaveUrl);
		// The schema name resolves to a single UId.
		Post(SelectUrl).Returns($"{{\"success\":true,\"rows\":[{{\"UId\":\"{SchemaUId}\"}}]}}");
		_savedPayload = null;
		// SaveAdministratedObject: capture the exact payload the client sent, then acknowledge success.
		Post(SaveUrl).Returns(callInfo => { _savedPayload = (string)callInfo[1]; return "{\"success\":true}"; });
		_client = new RightManagementServiceClient(_applicationClient, _urlBuilder);
	}

	private string Post(string url) =>
		_applicationClient.ExecutePostRequest(url, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());

	private void GetReturns(string administratedObjectJson) => Post(GetUrl).Returns(administratedObjectJson);

	private void SelectReturnsUIds(params string[] uIds) =>
		Post(SelectUrl).Returns("{\"success\":true,\"rows\":["
			+ string.Join(",", uIds.Select(uId => "{\"UId\":\"" + uId + "\"}")) + "]}");

	private void GetReturnsFor(string uId, string json) =>
		_applicationClient.ExecutePostRequest(GetUrl, Arg.Is<string>(body => body.Contains(uId)), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>()).Returns(json);

	private void GetThrowsFor(string uId, Exception exception) =>
		_applicationClient.ExecutePostRequest(GetUrl, Arg.Is<string>(body => body.Contains(uId)), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>()).Returns(_ => throw exception);

	private static string AdministeredObject(string caption) =>
		"{\"success\":true,\"administratedObject\":{\"name\":\"UsrFoo\",\"caption\":\"" + caption
		+ "\",\"administratedByOperations\":true,\"entitySchemaOperationsRights\":[]}}";

	private static string ObjectWithRows(bool administered, params string[] rows) =>
		"{\"success\":true,\"administratedObject\":{\"name\":\"UsrFoo\",\"administratedByOperations\":"
		+ Lower(administered) + ",\"entitySchemaOperationsRights\":[" + string.Join(",", rows) + "],"
		+ "\"entitySchemaRecordDefRights\":[{\"id\":\"r\"}],\"entitySchemaColumnsRights\":[{\"id\":\"c\"}],"
		+ "\"entityOperationGrantees\":[{\"id\":\"g\"}]}}";

	private static string FlagRow(Guid grantee, int position, bool read, bool append, bool edit, bool delete,
		string name = null) =>
		"{\"id\":\"r" + position + "\",\"position\":" + position + ",\"canRead\":" + Lower(read) + ",\"canAppend\":"
		+ Lower(append) + ",\"canEdit\":" + Lower(edit) + ",\"canDelete\":" + Lower(delete)
		+ ",\"sysAdminUnit\":{\"id\":\"" + grantee + "\"" + (name is null ? "" : ",\"name\":\"" + name + "\"") + "}}";

	private static string Lower(bool value) => value ? "true" : "false";

	private string[] GetBodiesInCallOrder() =>
		_applicationClient.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(IApplicationClient.ExecutePostRequest)
				&& Equals(call.GetArguments()[0], GetUrl))
			.Select(call => (string)call.GetArguments()[1])
			.ToArray();

	private JsonElement SavedObject() =>
		JsonDocument.Parse(_savedPayload).RootElement.GetProperty("administratedObject");

	private JsonElement[] SavedRows() =>
		SavedObject().GetProperty("entitySchemaOperationsRights").EnumerateArray().ToArray();

	private static bool IsRowOf(JsonElement row, Guid grantee) =>
		Guid.Parse(row.GetProperty("sysAdminUnit").GetProperty("id").GetString()!) == grantee;

	private ObjectRightsInfo Read() => _client.GetObjectRights("UsrFoo", new CreatioRequestOptions());

	// ---- Read: projection and priority order ----

	[Test]
	[Description("GetObjectRights projects every wire field onto RoleOperationRights — canAppend->CanCreate, the grantee id and name, the position — and the switch.")]
	public void GetObjectRights_ShouldProjectEveryField_WhenRowsHaveDistinctFlags() {
		// Arrange
		GetReturns(ObjectWithRows(true,
			FlagRow(Grantee, 0, read: true, append: false, edit: true, delete: false, name: "All external users"),
			FlagRow(Employees, 1, read: false, append: true, edit: false, delete: true, name: "All employees")));

		// Act
		ObjectRightsInfo info = Read();

		// Assert
		info.AdministratedByOperations.Should().BeTrue(because: "the switch is read from the node");
		info.Roles.Should().BeEquivalentTo(new[] {
			new RoleOperationRights(Grantee, "All external users", 0, true, false, true, false),
			new RoleOperationRights(Employees, "All employees", 1, false, true, false, true)
		}, options => options.WithStrictOrdering(), because: "a swapped field would misreport who can do what");
	}

	[Test]
	[Description("Rows are returned in priority order (lowest position first) whatever order the service lists them in, because the highest matching row decides.")]
	public void GetObjectRights_ShouldReturnRowsInPriorityOrder_WhenServiceListsThemOutOfOrder() {
		// Arrange
		GetReturns(ObjectWithRows(true,
			FlagRow(Employees, 3, true, true, true, true),
			FlagRow(Grantee, 1, true, false, false, false)));

		// Act
		ObjectRightsInfo info = Read();

		// Assert
		info.Roles.Select(role => role.Position).Should().Equal(new[] { 1, 3 },
			because: "the priority order is what decides access, so it is the order a caller sees");
	}

	[TestCase("{\"id\":\"not-a-guid\"}")]
	[TestCase("{}")]
	[Description("A missing or non-GUID grantee id reads as Guid.Empty, a missing name as (unknown), non-bool flags as false, and a missing position as the row's place in the list.")]
	public void GetObjectRights_ShouldFallBack_WhenRowFieldsAreMissingOrMalformed(string sysAdminUnit) {
		// Arrange
		GetReturns("{\"success\":true,\"administratedObject\":{\"name\":\"UsrFoo\",\"administratedByOperations\":true,"
			+ "\"entitySchemaOperationsRights\":[{\"canRead\":\"true\",\"canAppend\":1,\"canEdit\":null,"
			+ "\"sysAdminUnit\":" + sysAdminUnit + "}]}}");

		// Act
		RoleOperationRights role = Read().Roles.Single();

		// Assert
		role.GranteeId.Should().Be(Guid.Empty, because: "an unparseable id never matches a real grantee");
		role.GranteeName.Should().Be("(unknown)", because: "a missing name falls back to a placeholder");
		role.Position.Should().Be(0, because: "a row without a position keeps its place in the list");
		role.HasAnyOperation.Should().BeFalse(because: "a non-bool flag is not a grant");
	}

	[Test]
	[Description("A clean read carries a snapshot for the save; a failed read carries none.")]
	public void GetObjectRights_ShouldCarrySnapshotOnlyOnSuccess_WhenReadsSucceedAndFail() {
		// Arrange
		Post(GetUrl).Returns(AdministeredObject("ok"), InBandJsonFault);

		// Act
		ObjectRightsInfo ok = Read();
		ObjectRightsInfo failed = Read();

		// Assert
		ok.Snapshot.Should().NotBeNull(because: "a save writes back the object as it was read");
		failed.Snapshot.Should().BeNull(because: "there is nothing to write back after a failed read");
	}

	[Test]
	[Description("An in-band GetAdministratedObject success:false is surfaced as a read error, never as \"available to all\".")]
	public void GetObjectRights_ShouldReportReadError_WhenServiceReturnsInBandFailure() {
		// Arrange
		GetReturns("{\"success\":false,\"errorInfo\":{\"message\":\"permission denied\"}}");

		// Act
		ObjectRightsInfo info = Read();

		// Assert
		info.ReadError.Should().Contain("permission denied", because: "a logical service failure must be surfaced");
		info.AdministratedByOperations.Should().BeFalse(because: "a failed read is not an authoritative 'available' answer");
	}

	// ---- Read: candidate UIds ----

	[Test]
	[Description("The SysSchema candidate query orders ExtendParent ascending server-side (base row first) so a row cap can never cut off the administrable base row.")]
	public void GetObjectRights_ShouldOrderCandidatesByExtendParentServerSide_WhenResolvingUIds() {
		// Arrange
		GetReturns(AdministeredObject("base"));

		// Act
		Read();

		// Assert
		string selectBody = (string)_applicationClient.ReceivedCalls()
			.First(call => call.GetMethodInfo().Name == nameof(IApplicationClient.ExecutePostRequest)
				&& Equals(call.GetArguments()[0], SelectUrl)).GetArguments()[1];
		JsonElement extendParent = JsonDocument.Parse(selectBody).RootElement
			.GetProperty("columns").GetProperty("items").GetProperty("ExtendParent");
		extendParent.GetProperty("orderDirection").GetInt32().Should().Be(1,
			because: "ascending puts ExtendParent=false (the base row) first, before the row cap applies");
		extendParent.GetProperty("orderPosition").GetInt32().Should().Be(0,
			because: "ExtendParent must be the primary sort key");
	}

	[Test]
	[Description("Candidates are probed in the server's order (base row first) — the replacing layers are only tried when the base does not answer.")]
	public void GetObjectRights_ShouldProbeBaseRowFirst_WhenSeveralCandidatesResolve() {
		// Arrange
		SelectReturnsUIds(BaseUId, LayerUId);
		GetReturnsFor(BaseUId, AdministeredObject("base"));
		GetReturnsFor(LayerUId, AdministeredObject("layer"));

		// Act
		ObjectRightsInfo info = Read();

		// Assert
		info.Caption.Should().Be("base", because: "the base row answers, so it is the one used");
		string[] bodies = GetBodiesInCallOrder();
		bodies.Should().HaveCount(1, because: "a successful base probe must not be followed by probing the layers");
		bodies[0].Should().Contain(BaseUId, because: "the base row is probed first");
	}

	[TestCase(InBandJsonFault, TestName = "GetObjectRights_ShouldUseSecondCandidate_WhenFirstReturnsJsonFault")]
	[TestCase(HtmlRequestErrorPage, TestName = "GetObjectRights_ShouldUseSecondCandidate_WhenFirstReturnsHtmlErrorPage")]
	[Description("A first candidate answering with a fault — the in-band success:false or the real non-JSON 'Request Error' page — is skipped, and the second candidate's node is the one read (and later saved).")]
	public void GetObjectRights_ShouldUseSecondCandidate_WhenFirstFaults(string firstAnswer) {
		// Arrange
		SelectReturnsUIds(BaseUId, LayerUId);
		GetReturnsFor(BaseUId, firstAnswer);
		GetReturnsFor(LayerUId, AdministeredObject("from-second"));

		// Act
		ObjectRightsInfo info = Read();
		string error = _client.Save(info.Snapshot, info.State, new CreatioRequestOptions());

		// Assert
		info.ReadError.Should().BeNull(because: "the second candidate answered");
		info.Caption.Should().Be("from-second", because: "a faulting candidate must not end the probe loop");
		error.Should().BeNull(because: "the save succeeded");
		_savedPayload.Should().Contain("from-second", because: "the save round-trips the node that actually answered");
	}

	[Test]
	[Description("A first candidate whose request throws (HTTP 500) is skipped, and the read uses the second candidate.")]
	public void GetObjectRights_ShouldUseSecondCandidate_WhenFirstRequestThrows() {
		// Arrange
		SelectReturnsUIds(BaseUId, LayerUId);
		GetThrowsFor(BaseUId, new InvalidOperationException("HTTP 500"));
		GetReturnsFor(LayerUId, AdministeredObject("from-second"));

		// Act
		ObjectRightsInfo info = Read();

		// Assert
		info.ReadError.Should().BeNull(because: "the second candidate answered");
		info.Caption.Should().Be("from-second", because: "a throwing candidate must not end the probe loop");
	}

	[Test]
	[Description("When every candidate faults, the read reports a ReadError — never 'available' — and keeps the FIRST candidate's error.")]
	public void GetObjectRights_ShouldReportFirstCandidateError_WhenEveryCandidateFails() {
		// Arrange
		SelectReturnsUIds(BaseUId, LayerUId);
		GetThrowsFor(BaseUId, new InvalidOperationException("base failed"));
		GetReturnsFor(LayerUId, HtmlRequestErrorPage);

		// Act
		ObjectRightsInfo info = Read();

		// Assert
		info.Found.Should().BeTrue(because: "the schema exists; only its rights could not be read");
		info.ReadError.Should().Contain("base failed", because: "the base row's real cause is what the caller needs");
	}

	[Test]
	[Description("A timeout on the first candidate UId stops the probe: the next replacing-layer candidate would only hang as long again.")]
	public void GetObjectRights_ShouldStopProbing_WhenFirstCandidateTimesOut() {
		// Arrange
		SelectReturnsUIds(BaseUId, LayerUId);
		GetThrowsFor(BaseUId, new TaskCanceledException("timed out"));
		GetReturnsFor(LayerUId, AdministeredObject("from-second"));

		// Act
		ObjectRightsInfo info = Read();

		// Assert
		info.ReadError.Should().Contain("timed out", because: "the timeout is reported against the object");
		info.TimedOut.Should().BeTrue(because: "a caller reading several objects stops after a hang");
		GetBodiesInCallOrder().Should().HaveCount(1, because: "no further candidate is probed after a hang");
	}

	[Test]
	[Description("A read that fails with a fault the service answered is not marked as timed out: a caller reading several objects goes on with the next one.")]
	public void GetObjectRights_ShouldNotMarkATimeout_WhenTheServiceAnswersWithAFault() {
		// Arrange
		SelectReturnsUIds(BaseUId);
		GetThrowsFor(BaseUId, new HttpRequestException("503"));

		// Act
		ObjectRightsInfo info = Read();

		// Assert
		info.ReadError.Should().Contain("503", because: "the fault is reported against the object");
		info.TimedOut.Should().BeFalse(because: "the server answered; the next object may well be readable");
	}

	[Test]
	[Description("A schema name with no SysSchema rows is reported as not found, and no GetAdministratedObject call is made.")]
	public void GetObjectRights_ShouldReportNotFound_WhenNoCandidateRows() {
		// Arrange
		SelectReturnsUIds();

		// Act
		ObjectRightsInfo info = _client.GetObjectRights("UsrNope", new CreatioRequestOptions());

		// Assert
		info.Found.Should().BeFalse(because: "no candidate UId resolved");
		info.ReadError.Should().BeNull(because: "not-found is not a read failure");
		GetBodiesInCallOrder().Should().BeEmpty(because: "there is nothing to probe");
	}

	[Test]
	[Description("Duplicate and empty candidate UIds are dropped before probing.")]
	public void GetObjectRights_ShouldDeduplicateAndDropEmptyUIds_WhenRowsRepeat() {
		// Arrange
		SelectReturnsUIds(BaseUId, BaseUId, "00000000-0000-0000-0000-000000000000");
		GetReturnsFor(BaseUId, InBandJsonFault);

		// Act
		Read();

		// Assert
		string[] bodies = GetBodiesInCallOrder();
		bodies.Should().HaveCount(1, because: "the duplicate UId is probed once and the empty UId never");
		bodies[0].Should().Contain(BaseUId, because: "only the real candidate is probed");
	}

	[Test]
	[Description("A SysSchema SelectQuery that throws is reported as a read error, not as an escaping exception.")]
	public void GetObjectRights_ShouldReportError_WhenSelectQueryThrows() {
		// Arrange
		Post(SelectUrl).Returns(_ => throw new InvalidOperationException("select failed"));

		// Act
		ObjectRightsInfo info = Read();

		// Assert
		info.ReadError.Should().Contain("select failed", because: "the resolution failure is surfaced");
	}

	[Test]
	[Description("The schema name is resolved to its candidate UIds once per client: the read-back after a save reuses it.")]
	public void GetObjectRights_ShouldResolveSchemaUIdsOnce_WhenReadTwice() {
		// Arrange
		GetReturns(AdministeredObject("x"));

		// Act
		Read();
		Read();

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(SelectUrl,
			Arg.Is<string>(body => body.Contains("SysSchema")), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	private static IEnumerable<TestCaseData> ServiceFailures() {
		yield return new TestCaseData(new HttpRequestException("503"), false).SetName("ObjectRights_ShouldReportFailure_WhenTheServiceFailsWithHttpRequest");
		yield return new TestCaseData(new IOException("reset"), false).SetName("ObjectRights_ShouldReportFailure_WhenTheServiceFailsWithIO");
		yield return new TestCaseData(new JsonException("not json"), false).SetName("ObjectRights_ShouldReportFailure_WhenTheServiceFailsWithJson");
		yield return new TestCaseData(new UnauthorizedAccessException("401"), false).SetName("ObjectRights_ShouldReportFailure_WhenTheServiceFailsWithUnauthorized");
		yield return new TestCaseData(new TimeoutException("timeout"), true).SetName("ObjectRights_ShouldReportFailure_WhenTheServiceFailsWithTimeout");
		yield return new TestCaseData(new TaskCanceledException("HTTP timeout"), true).SetName("ObjectRights_ShouldReportFailure_WhenTheServiceFailsWithTaskCanceled");
		// Creatio's client runs the request through Task.Result: this is how a hang and a transport fault really arrive.
		yield return new TestCaseData(new AggregateException(new TaskCanceledException("HTTP timeout")), true)
			.SetName("ObjectRights_ShouldReportFailure_WhenATimeoutArrivesWrapped");
		yield return new TestCaseData(new AggregateException(new HttpRequestException("503")), false)
			.SetName("ObjectRights_ShouldReportFailure_WhenATransportFaultArrivesWrapped");
	}

	[TestCaseSource(nameof(ServiceFailures))]
	[Description("A failure of the Creatio service (transport, timeout, non-JSON body, authentication — bare or wrapped the way Creatio's client throws it) is attributed to the object: the read reports a ReadError, marked as timed out only for a hang, and the save returns the failure so the caller reads the object back, instead of ending the run.")]
	public void ObjectRights_ShouldReportFailure_WhenServiceFails(Exception failure, bool timedOut) {
		// Arrange
		GetReturns(AdministeredObject("x"));
		ObjectRightsInfo read = Read();
		Post(GetUrl).Returns(_ => throw failure);
		Post(SaveUrl).Returns(_ => throw failure);

		// Act
		ObjectRightsInfo info = Read();
		string saveError = _client.Save(read.Snapshot, read.State, new CreatioRequestOptions());

		// Assert
		info.ReadError.Should().NotBeNull(because: "a service failure is reported on the object it happened to");
		info.ReadError.Should().NotContain("One or more errors occurred",
			because: "a wrapper around one fault is reported by that fault");
		info.TimedOut.Should().Be(timedOut, because: "only a hang stops a caller that reads several objects");
		saveError.Should().NotBeNullOrEmpty(because: "a failed save is reported, never taken for success");
	}

	[Test]
	[Description("When an earlier candidate answered with a fault and a later one hangs, the read reports both: a listing that stops on the hang must not read as stopping on the earlier fault.")]
	public void GetObjectRights_ShouldNameTheHang_WhenALaterCandidateTimesOutAfterAFault() {
		// Arrange
		SelectReturnsUIds(BaseUId, LayerUId);
		GetThrowsFor(BaseUId, new InvalidOperationException("base failed"));
		GetThrowsFor(LayerUId, new AggregateException(new TaskCanceledException("timed out")));

		// Act
		ObjectRightsInfo info = Read();

		// Assert
		info.TimedOut.Should().BeTrue(because: "the read ended on a hang");
		info.ReadError.Should().Contain("base failed", because: "the base row's cause is still what the caller needs");
		info.ReadError.Should().Contain("timed out", because: "the hang the listing stops on is named too");
	}

	[Test]
	[Description("A programming error (not a service failure) is not swallowed as an object's read error: it escapes, so a bug is not reported as 'could not read'.")]
	public void GetObjectRights_ShouldThrow_WhenProgrammingErrorOccurs() {
		// Arrange
		Post(GetUrl).Returns(_ => throw new NullReferenceException("bug"));

		// Act
		Action read = () => Read();

		// Assert
		read.Should().Throw<NullReferenceException>(because: "only service failures are attributed to the object");
	}

	// ---- Save: the planned state, written faithfully ----

	[Test]
	[Description("Save writes each planned row's flags onto the matching row, keeps its position and every other field, sets the switch, and sends the untouched rights collections as null.")]
	public void Save_ShouldWritePlannedFlagsAndNullOtherCollections_WhenRowsExist() {
		// Arrange
		GetReturns(ObjectWithRows(true,
			FlagRow(Grantee, 0, read: true, append: false, edit: false, delete: true),
			FlagRow(Employees, 1, read: true, append: true, edit: true, delete: true)));
		ObjectRightsInfo read = Read();
		ObjectRightsState after = new(true, new[] {
			read.Roles[0] with { CanEdit = true, CanDelete = false },
			read.Roles[1]
		});

		// Act
		string error = _client.Save(read.Snapshot, after, new CreatioRequestOptions());

		// Assert
		error.Should().BeNull(because: "the service acknowledged the save");
		JsonElement granteeRow = SavedRows().Single(row => IsRowOf(row, Grantee));
		granteeRow.GetProperty("canRead").GetBoolean().Should().BeTrue(because: "read was planned and kept");
		granteeRow.GetProperty("canEdit").GetBoolean().Should().BeTrue(because: "edit was planned");
		granteeRow.GetProperty("canDelete").GetBoolean().Should().BeFalse(because: "delete was planned off");
		granteeRow.GetProperty("id").GetString().Should().Be("r0", because: "the existing row is updated in place, not replaced");
		granteeRow.GetProperty("position").GetInt32().Should().Be(0, because: "a row is never moved");
		SavedObject().GetProperty("administratedByOperations").GetBoolean().Should().BeTrue(
			because: "the planned switch is written");
		foreach (string collection in new[] { "entitySchemaRecordDefRights", "entitySchemaColumnsRights", "entityOperationGrantees" }) {
			SavedObject().GetProperty(collection).ValueKind.Should().Be(JsonValueKind.Null,
				because: $"the untouched collection '{collection}' is sent as null so the save leaves it alone");
		}
	}

	[Test]
	[Description("A planned row the object does not have is added with the grantee id, its planned position and exactly the planned flags.")]
	public void Save_ShouldAddNewRowAtPlannedPosition_WhenGranteeHasNoRow() {
		// Arrange
		GetReturns(ObjectWithRows(true, FlagRow(Employees, 0, true, true, true, true)));
		ObjectRightsInfo read = Read();
		ObjectRightsState after = new(true, new[] {
			read.Roles[0],
			new RoleOperationRights(Grantee, "All external users", 1, true, true, false, false)
		});

		// Act
		_client.Save(read.Snapshot, after, new CreatioRequestOptions());

		// Assert
		JsonElement added = SavedRows().Single(row => IsRowOf(row, Grantee));
		added.GetProperty("position").GetInt32().Should().Be(1, because: "the planner put the new row at the lowest priority");
		added.GetProperty("canRead").GetBoolean().Should().BeTrue(because: "read was planned");
		added.GetProperty("canAppend").GetBoolean().Should().BeTrue(because: "create maps to the platform's canAppend");
		added.GetProperty("canEdit").GetBoolean().Should().BeFalse(because: "edit was not planned");
		added.GetProperty("canDelete").GetBoolean().Should().BeFalse(because: "delete was not planned");
		SavedRows().Should().HaveCount(2, because: "the existing row is kept next to the new one");
	}

	[Test]
	[Description("Save never removes a row: a row of the object that the plan does not list is written back unchanged.")]
	public void Save_ShouldKeepRowNotInPlan_WhenPlanListsFewerRows() {
		// Arrange
		GetReturns(ObjectWithRows(true,
			FlagRow(Grantee, 0, true, false, false, false),
			FlagRow(Employees, 1, true, true, true, true)));
		ObjectRightsInfo read = Read();
		ObjectRightsState after = new(true, new[] { read.Roles[0] with { CanEdit = true } });

		// Act
		_client.Save(read.Snapshot, after, new CreatioRequestOptions());

		// Assert
		SavedRows().Should().Contain(row => IsRowOf(row, Employees), because: "the writer never removes a row");
	}

	[Test]
	[Description("Saving an object with no stored rows persists the synthesized All employees row the read returned, so turning operation permissions on keeps internal users' access.")]
	public void Save_ShouldPersistSynthesizedRow_WhenEnablingObjectWithoutStoredRows() {
		// Arrange
		GetReturns(ObjectWithRows(false, FlagRow(Employees, 0, true, true, true, true, name: "All employees")));
		ObjectRightsInfo read = Read();
		ObjectRightsState after = new(true, read.Roles);

		// Act
		_client.Save(read.Snapshot, after, new CreatioRequestOptions());

		// Assert
		SavedObject().GetProperty("administratedByOperations").GetBoolean().Should().BeTrue(
			because: "the plan turned operation permissions on");
		SavedRows().Should().ContainSingle(row => IsRowOf(row, Employees),
			because: "the server adds nothing on save, so the row must be in the payload");
	}

	[Test]
	[Description("Save writes the switch OFF when the plan turns operation permissions off, keeping the rows.")]
	public void Save_ShouldTurnSwitchOffAndKeepRows_WhenPlanDisables() {
		// Arrange
		GetReturns(ObjectWithRows(true, FlagRow(Grantee, 0, true, false, false, false)));
		ObjectRightsInfo read = Read();
		ObjectRightsState after = new(false, new[] { read.Roles[0] with { CanRead = false } });

		// Act
		_client.Save(read.Snapshot, after, new CreatioRequestOptions());

		// Assert
		SavedObject().GetProperty("administratedByOperations").GetBoolean().Should().BeFalse(
			because: "the plan turned operation permissions off");
		SavedRows().Should().ContainSingle(because: "the rows are kept and apply again if the switch is turned back on");
	}

	[Test]
	[Description("An in-band success:false on SaveAdministratedObject is returned as the save error.")]
	public void Save_ShouldReturnServiceMessage_WhenSaveReportsFailure() {
		// Arrange
		GetReturns(AdministeredObject("x"));
		ObjectRightsInfo read = Read();
		Post(SaveUrl).Returns("{\"success\":false,\"errorInfo\":{\"message\":\"no rights to save\"}}");

		// Act
		string error = _client.Save(read.Snapshot, read.State, new CreatioRequestOptions());

		// Assert
		error.Should().Contain("no rights to save", because: "a failed save is reported with the service's reason");
	}

	[TestCase("{\"success\":false,\"errorInfo\":{\"message\":\"\"}}", TestName = "ObjectRights_ShouldReportFallback_WhenFailureMessageIsEmpty")]
	[TestCase("{\"success\":false}", TestName = "ObjectRights_ShouldReportFallback_WhenFailureHasNoErrorInfo")]
	[Description("A service failure without a message is still reported with a non-empty reason, on the save and on the read: an empty string would read as 'no error'.")]
	public void ObjectRights_ShouldReportFallbackMessage_WhenServiceFailsWithoutOne(string failure) {
		// Arrange
		GetReturns(AdministeredObject("x"));
		ObjectRightsInfo read = Read();
		Post(SaveUrl).Returns(failure);
		Post(GetUrl).Returns(failure);

		// Act
		string saveError = _client.Save(read.Snapshot, read.State, new CreatioRequestOptions());
		ObjectRightsInfo reread = Read();

		// Assert
		saveError.Should().NotBeNullOrWhiteSpace(because: "a failed save must never look like a successful one");
		reread.ReadError.Should().NotBeNullOrWhiteSpace(because: "a failed read must never look like a clean one");
	}

	[Test]
	[Description("Save does not change the snapshot it was given: a second save from the same read starts from the object as read.")]
	public void Save_ShouldNotMutateSnapshot_WhenCalled() {
		// Arrange
		GetReturns(ObjectWithRows(true, FlagRow(Grantee, 0, true, false, false, false)));
		ObjectRightsInfo read = Read();
		string before = read.Snapshot.Node.ToJsonString();

		// Act
		_client.Save(read.Snapshot, new ObjectRightsState(false, new[] { read.Roles[0] with { CanRead = false } }),
			new CreatioRequestOptions());

		// Assert
		read.Snapshot.Node.ToJsonString().Should().Be(before, because: "the payload is built from a copy of the snapshot");
	}

	[Test]
	[Description("Another role's two rows are left exactly as read when the plan changes only the grantee's row: rows are matched by grantee AND position, so the last planned row of that role never overwrites the first read one.")]
	public void Save_ShouldLeaveAnotherRolesDuplicateRowsAsRead_WhenOnlyTheGranteeRowChanges() {
		// Arrange
		Guid sales = Guid.NewGuid();
		GetReturns(ObjectWithRows(true,
			FlagRow(sales, 0, read: true, append: false, edit: false, delete: false),
			FlagRow(Employees, 1, true, true, true, true),
			FlagRow(sales, 2, true, true, true, true),
			FlagRow(Grantee, 3, true, true, true, false)));
		ObjectRightsInfo read = Read();
		ObjectRightsState after = new(true, read.Roles
			.Select(row => row.GranteeId == Grantee ? row with { CanCreate = false } : row)
			.ToArray());

		// Act
		string error = _client.Save(read.Snapshot, after, new CreatioRequestOptions());

		// Assert
		error.Should().BeNull(because: "the service acknowledged the save");
		JsonElement[] salesRows = SavedRows().Where(row => IsRowOf(row, sales)).ToArray();
		salesRows.Should().HaveCount(2, because: "both of Sales' rows are sent");
		salesRows.Single(row => row.GetProperty("position").GetInt32() == 0).GetProperty("canAppend").GetBoolean()
			.Should().BeFalse(because: "Sales' row at position 0 keeps its read-only flags; nothing named it");
		salesRows.Single(row => row.GetProperty("position").GetInt32() == 2).GetProperty("canDelete").GetBoolean()
			.Should().BeTrue(because: "Sales' row at position 2 keeps its own flags");
		SavedRows().Single(row => IsRowOf(row, Grantee)).GetProperty("canAppend").GetBoolean().Should().BeFalse(
			because: "the grantee's row gets the planned change");
	}

	[Test]
	[Description("Another role's two rows at the SAME position with different operations are both left as read when only the grantee's row changes: each is matched as it was read, never merged.")]
	public void Save_ShouldLeaveAnotherRolesRowsAtOnePositionAsRead_WhenOnlyTheGranteeRowChanges() {
		// Arrange
		Guid sales = Guid.NewGuid();
		GetReturns(ObjectWithRows(true,
			FlagRow(sales, 0, read: true, append: false, edit: false, delete: false),
			FlagRow(sales, 0, read: true, append: true, edit: true, delete: true),
			FlagRow(Grantee, 1, true, false, false, false)));
		ObjectRightsInfo read = Read();
		ObjectRightsState after = new(true, read.Roles
			.Select(row => row.GranteeId == Grantee ? row with { CanEdit = true } : row)
			.ToArray());

		// Act
		string error = _client.Save(read.Snapshot, after, new CreatioRequestOptions());

		// Assert
		error.Should().BeNull(because: "the only changed row has one candidate");
		JsonElement[] salesRows = SavedRows().Where(row => IsRowOf(row, sales)).ToArray();
		salesRows.Select(row => row.GetProperty("canAppend").GetBoolean()).Should().Equal(new[] { false, true },
			because: "each of Sales' rows keeps its own flags, in the order it was read");
		SavedRows().Single(row => IsRowOf(row, Grantee)).GetProperty("canEdit").GetBoolean().Should().BeTrue(
			because: "the grantee's row gets the planned change");
	}

	[Test]
	[Description("An enable that meets stale rows without All employees appends the All employees row and then the grantee's row at the positions the plan gave them, leaving the stale row as read.")]
	public void Save_ShouldAppendTheAllEmployeesAndGranteeRows_WhenAnEnableAddsBoth() {
		// Arrange
		Guid stale = Guid.NewGuid();
		GetReturns(ObjectWithRows(false, FlagRow(stale, 0, true, false, false, false)));
		ObjectRightsInfo read = Read();
		ObjectRightsState after = new(true, new[] {
			read.Roles[0],
			new RoleOperationRights(Employees, "All employees", 1, true, true, true, true),
			new RoleOperationRights(Grantee, "Grantee", 2, true, false, false, false)
		});

		// Act
		_client.Save(read.Snapshot, after, new CreatioRequestOptions());

		// Assert
		SavedRows().Should().HaveCount(3, because: "both planned rows are added next to the stale one");
		JsonElement staleRow = SavedRows().Single(row => IsRowOf(row, stale));
		staleRow.GetProperty("canRead").GetBoolean().Should().BeTrue(because: "the stale row is sent as read");
		staleRow.GetProperty("canAppend").GetBoolean().Should().BeFalse(because: "the stale row gets no new operation");
		SavedRows().Single(row => IsRowOf(row, Employees)).GetProperty("position").GetInt32().Should().Be(1,
			because: "the All employees row goes right below the stale row");
		SavedRows().Single(row => IsRowOf(row, Employees)).GetProperty("canDelete").GetBoolean().Should().BeTrue(
			because: "the All employees row carries every operation");
		SavedRows().Single(row => IsRowOf(row, Grantee)).GetProperty("position").GetInt32().Should().Be(2,
			because: "the grantee's row goes at the lowest priority");
		SavedObject().GetProperty("administratedByOperations").GetBoolean().Should().BeTrue(
			because: "the plan turned operation permissions on");
	}

	[Test]
	[Description("A row with no position is projected at its index in the array, and the save finds it again by that projection: it is changed in place, not duplicated.")]
	public void Save_ShouldChangeARowWithoutPositionInPlace_WhenThePlanChangesIt() {
		// Arrange
		string noPosition = "{\"id\":\"np\",\"canRead\":true,\"canAppend\":false,\"canEdit\":false,\"canDelete\":false,"
			+ "\"sysAdminUnit\":{\"id\":\"" + Grantee + "\"}}";
		GetReturns(ObjectWithRows(true, FlagRow(Employees, 0, true, true, true, true), noPosition));
		ObjectRightsInfo read = Read();
		RoleOperationRights granteeRow = read.Roles.Single(row => row.GranteeId == Grantee);
		ObjectRightsState after = new(true, read.Roles
			.Select(row => row == granteeRow ? row with { CanEdit = true } : row)
			.ToArray());

		// Act
		string error = _client.Save(read.Snapshot, after, new CreatioRequestOptions());

		// Assert
		error.Should().BeNull(because: "the row is found again by its projected position");
		granteeRow.Position.Should().Be(1, because: "a row without a position is placed at its index in the array");
		SavedRows().Should().HaveCount(2, because: "the row is changed in place, not added again");
		JsonElement saved = SavedRows().Single(row => IsRowOf(row, Grantee));
		saved.GetProperty("id").GetString().Should().Be("np", because: "the read row is the one changed");
		saved.GetProperty("canEdit").GetBoolean().Should().BeTrue(because: "the planned change is written");
	}

	[Test]
	[Description("A row the plan does not change is sent exactly as read — including a value the projection cannot represent and a field it does not know — so the save never rewrites it from the projection.")]
	public void Save_ShouldSendAnUnchangedRowExactlyAsRead_WhenItHasFieldsTheProjectionDoesNotModel() {
		// Arrange
		const string oddRow = """
			{"id":"odd","position":0,"canRead":"true","canAppend":false,"canEdit":false,"canDelete":false,"custom":"kept","sysAdminUnit":{"id":"a29a3ba5-4b0d-de11-9a51-005056c00008"}}
			""";
		GetReturns(ObjectWithRows(true, oddRow, FlagRow(Grantee, 1, true, false, false, false)));
		ObjectRightsInfo read = Read();
		ObjectRightsState after = new(true, new[] { read.Roles[0], read.Roles[1] with { CanEdit = true } });

		// Act
		_client.Save(read.Snapshot, after, new CreatioRequestOptions());

		// Assert
		JsonElement unchanged = SavedRows().Single(row => IsRowOf(row, Employees));
		unchanged.GetProperty("canRead").ValueKind.Should().Be(JsonValueKind.String,
			because: "a row the plan leaves alone is not rewritten from its projection");
		unchanged.GetProperty("custom").GetString().Should().Be("kept", because: "every field of an unchanged row round-trips");
	}

	[Test]
	[Description("When two rows of one role sit at the same position and the plan changes them, the row to change is ambiguous: nothing is sent and the reason is returned.")]
	public void Save_ShouldSendNothing_WhenTheRowToChangeIsAmbiguous() {
		// Arrange
		GetReturns(ObjectWithRows(true,
			FlagRow(Grantee, 0, true, false, false, false),
			FlagRow(Grantee, 0, true, false, false, false)));
		ObjectRightsInfo read = Read();
		ObjectRightsState after = new(true, new[] {
			read.Roles[0] with { CanEdit = true },
			read.Roles[1] with { CanCreate = true }
		});

		// Act
		string error = _client.Save(read.Snapshot, after, new CreatioRequestOptions());

		// Assert
		error.Should().Contain("ambiguous", because: "the writer does not guess which row a change belongs to");
		_savedPayload.Should().BeNull(because: "nothing is sent when the row to change is ambiguous");
	}

	[Test]
	[Description("A read whose every candidate answers with an HTML page reports the failure without the page body: it can carry cookies, tokens and stack traces.")]
	public void GetObjectRights_ShouldNotPreviewTheHtmlPage_WhenTheServiceAnswersWithOne() {
		// Arrange
		Post(GetUrl).Returns(HtmlRequestErrorPage);

		// Act
		ObjectRightsInfo read = Read();

		// Assert
		read.ReadError.Should().Contain("HTML page", because: "the read error says what came back");
		read.ReadError.Should().NotContain("<html>", because: "no markup reaches the error");
	}

	// ---- Grantee lookup ----

	[Test]
	[Description("ResolveGranteeName returns the SysAdminUnit name, or null when the id does not exist.")]
	public void ResolveGranteeName_ShouldReturnNameOrNull_WhenTheIdExistsOrNot() {
		// Arrange
		Post(SelectUrl).Returns("{\"success\":true,\"rows\":[{\"Name\":\"All external users\"}]}", "{\"success\":true,\"rows\":[]}");

		// Act
		string found = _client.ResolveGranteeName(Grantee, new CreatioRequestOptions());
		string missing = _client.ResolveGranteeName(Guid.NewGuid(), new CreatioRequestOptions());

		// Assert
		found.Should().Be("All external users", because: "an existing SysAdminUnit resolves to its name");
		missing.Should().BeNull(because: "a missing id resolves to nothing");
		_applicationClient.Received().ExecutePostRequest(SelectUrl,
			Arg.Is<string>(body => body.Contains("SysAdminUnit") && body.Contains(Grantee.ToString())),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}
}
