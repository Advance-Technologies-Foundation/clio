using System;
using System.Linq;
using System.Text.Json;
using Clio.Common;
using Clio.Common.ObjectRights;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.ObjectRights;

/// <summary>
/// Direct unit tests for <see cref="RightManagementServiceClient"/> — the JSON read-modify-write over
/// GetAdministratedObject / SaveAdministratedObject that the command tests do not exercise (they mock the
/// interfaces). Feeds canned service responses through a substitute <see cref="IApplicationClient"/> and
/// asserts the exact save payload and the change/error semantics.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class RightManagementServiceClientTests {

	private const string SchemaUId = "35a9057f-800d-414b-acfb-c919c215857a";
	private static readonly Guid Grantee = Guid.Parse("720b771c-e7a7-4f31-9cfb-52cd21c3739f");

	private const string SelectUrl = "http://host/0/DataService/json/SyncReply/SelectQuery";
	private const string GetUrl = "http://host/0/ServiceModel/RightManagementService.svc/GetAdministratedObject";
	private const string SaveUrl = "http://host/0/ServiceModel/RightManagementService.svc/SaveAdministratedObject";

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

	private void GetReturns(string administratedObjectJson) =>
		Post(GetUrl).Returns(administratedObjectJson);

	[Test]
	[Description("A grant to a not-yet-present role writes the role's row with the requested operations, enables operation permissions, and sends the untouched rights collections as null.")]
	public void SetObjectRights_ShouldWriteRowEnableAndNullOtherCollections_WhenGrantingNewRole() {
		// Arrange
		GetReturns($"{{\"success\":true,\"administratedObject\":{{\"name\":\"UsrFoo\",\"administratedByOperations\":true,"
			+ "\"entitySchemaOperationsRights\":[],\"entitySchemaRecordDefRights\":[{\"id\":\"r\"}],"
			+ "\"entitySchemaColumnsRights\":[{\"id\":\"c\"}],\"entityOperationGrantees\":[{\"id\":\"g\"}]}}");

		// Act
		ObjectRightsChange result = _client.SetObjectRights("UsrFoo", Grantee,
			new[] { ObjectOperation.Read, ObjectOperation.Create, ObjectOperation.Edit }, revoke: false,
			disableOperationPermissions: false, new CreatioRequestOptions());

		// Assert
		result.Changed.Should().BeTrue(because: "granting a role that had no row is a change");
		_savedPayload.Should().Contain(Grantee.ToString(), because: "the new row carries the grantee id");
		_savedPayload.Should().Contain("\"canRead\":true", because: "read was granted");
		_savedPayload.Should().Contain("\"canDelete\":false", because: "delete was not requested");
		JsonElement saved = JsonDocument.Parse(_savedPayload).RootElement.GetProperty("administratedObject");
		foreach (string collection in new[] { "entitySchemaRecordDefRights", "entitySchemaColumnsRights", "entityOperationGrantees" }) {
			saved.GetProperty(collection).ValueKind.Should().Be(JsonValueKind.Null,
				because: $"the untouched collection '{collection}' is sent as null so the save leaves it alone");
		}
	}

	private void GetReturnsSingleRowObject() =>
		GetReturns($"{{\"success\":true,\"administratedObject\":{{\"name\":\"UsrFoo\",\"administratedByOperations\":true,"
			+ $"\"entitySchemaOperationsRights\":[{{\"id\":\"x\",\"position\":0,\"canRead\":true,\"canAppend\":false,"
			+ $"\"canEdit\":false,\"canDelete\":false,\"sysAdminUnit\":{{\"id\":\"{Grantee}\",\"name\":\"R\"}}}}]}}}}");

	[Test]
	[Description("Revoking the last grant with disableOperationPermissions removes the row and turns operation permissions off, reporting the resulting access widening.")]
	public void SetObjectRights_ShouldDisableOperationPermissions_WhenRevokingLastRowAndAsked() {
		// Arrange
		GetReturnsSingleRowObject();

		// Act
		ObjectRightsChange result = _client.SetObjectRights("UsrFoo", Grantee,
			new[] { ObjectOperation.Read, ObjectOperation.Create, ObjectOperation.Edit, ObjectOperation.Delete },
			revoke: true, disableOperationPermissions: true, new CreatioRequestOptions());

		// Assert
		result.Changed.Should().BeTrue(because: "the row was removed");
		result.OperationPermissionsDisabled.Should().BeTrue(
			because: "the caller must be told the object is now available to every internal user");
		_savedPayload.Should().Contain("\"administratedByOperations\":false",
			because: "the caller explicitly asked to return the object to available-to-all");
	}

	[Test]
	[Description("A revoke that would remove the object's last rights row writes nothing without disableOperationPermissions, so a revoke never widens access as a side effect.")]
	public void SetObjectRights_ShouldRefuseAndNotSave_WhenRevokingLastRowWithoutOptIn() {
		// Arrange
		GetReturnsSingleRowObject();

		// Act
		ObjectRightsChange result = _client.SetObjectRights("UsrFoo", Grantee,
			new[] { ObjectOperation.Read, ObjectOperation.Create, ObjectOperation.Edit, ObjectOperation.Delete },
			revoke: true, disableOperationPermissions: false, new CreatioRequestOptions());

		// Assert
		result.RefusedLastRowRemoval.Should().BeTrue(because: "the caller did not ask to turn operation permissions off");
		result.Changed.Should().BeFalse(because: "nothing was written");
		_applicationClient.DidNotReceive().ExecutePostRequest(SaveUrl, Arg.Any<string>(), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("A partial revoke that leaves the role some rights is applied even on the object's only row, because no row is removed.")]
	public void SetObjectRights_ShouldApplyPartialRevoke_WhenLastRowKeepsRights() {
		// Arrange
		GetReturns($"{{\"success\":true,\"administratedObject\":{{\"name\":\"UsrFoo\",\"administratedByOperations\":true,"
			+ $"\"entitySchemaOperationsRights\":[{{\"id\":\"x\",\"position\":0,\"canRead\":true,\"canAppend\":true,"
			+ $"\"canEdit\":false,\"canDelete\":false,\"sysAdminUnit\":{{\"id\":\"{Grantee}\",\"name\":\"R\"}}}}]}}}}");

		// Act
		ObjectRightsChange result = _client.SetObjectRights("UsrFoo", Grantee,
			new[] { ObjectOperation.Create }, revoke: true, disableOperationPermissions: false,
			new CreatioRequestOptions());

		// Assert
		result.RefusedLastRowRemoval.Should().BeFalse(because: "the row keeps canRead, so it is not removed");
		result.Changed.Should().BeTrue(because: "canAppend was cleared");
		_savedPayload.Should().Contain("\"administratedByOperations\":true",
			because: "a narrowing revoke never touches the object's administration flag");
	}

	[Test]
	[Description("Re-granting operations a role already holds writes nothing and reports no change.")]
	public void SetObjectRights_ShouldNotSave_WhenGrantIsNoOp() {
		// Arrange
		GetReturns($"{{\"success\":true,\"administratedObject\":{{\"name\":\"UsrFoo\",\"administratedByOperations\":true,"
			+ $"\"entitySchemaOperationsRights\":[{{\"id\":\"x\",\"position\":0,\"canRead\":true,\"canAppend\":true,"
			+ $"\"canEdit\":true,\"canDelete\":false,\"sysAdminUnit\":{{\"id\":\"{Grantee}\",\"name\":\"R\"}}}}]}}}}");

		// Act
		ObjectRightsChange result = _client.SetObjectRights("UsrFoo", Grantee,
			new[] { ObjectOperation.Read, ObjectOperation.Create, ObjectOperation.Edit }, revoke: false,
			disableOperationPermissions: false, new CreatioRequestOptions());

		// Assert
		result.Changed.Should().BeFalse(because: "the role already holds exactly these operations");
		_applicationClient.DidNotReceive().ExecutePostRequest(SaveUrl, Arg.Any<string>(), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("An in-band GetAdministratedObject success:false is surfaced as a read error, never as \"available to all\".")]
	public void GetObjectRights_ShouldReportReadError_WhenServiceReturnsInBandFailure() {
		// Arrange
		GetReturns("{\"success\":false,\"errorInfo\":{\"message\":\"permission denied\"}}");

		// Act
		ObjectRightsInfo info = _client.GetObjectRights("UsrFoo", new CreatioRequestOptions());

		// Assert
		info.ReadError.Should().Contain("permission denied", because: "a logical service failure must be surfaced");
		info.AdministratedByOperations.Should().BeFalse(because: "a failed read is not an authoritative 'available' answer");
	}

	// ---- Candidate resolution (RC-4): order, probe loop, dedup ----

	private const string BaseUId = "11111111-1111-1111-1111-111111111111";
	private const string LayerUId = "22222222-2222-2222-2222-222222222222";

	private const string InBandJsonFault = "{\"success\":false,\"errorInfo\":{\"message\":\"Request Error\"}}";

	private void SelectReturnsUIds(params string[] uIds) =>
		Post(SelectUrl).Returns("{\"success\":true,\"rows\":["
			+ string.Join(",", uIds.Select(uId => "{\"UId\":\"" + uId + "\"}")) + "]}");

	private void GetReturnsFor(string uId, string json) =>
		_applicationClient.ExecutePostRequest(GetUrl, Arg.Is<string>(body => body.Contains(uId)), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>()).Returns(json);

	private static string AdministeredObject(string caption) =>
		"{\"success\":true,\"administratedObject\":{\"name\":\"UsrFoo\",\"caption\":\"" + caption
		+ "\",\"administratedByOperations\":true,\"entitySchemaOperationsRights\":[]}}";

	private static string NotAdministeredObject(string rowsJson) =>
		"{\"success\":true,\"administratedObject\":{\"name\":\"UsrFoo\",\"administratedByOperations\":false,"
		+ "\"entitySchemaOperationsRights\":[" + rowsJson + "]}}";

	private static string Row(string id, int position, string granteeId, bool canRead) =>
		"{\"id\":\"" + id + "\",\"position\":" + position + ",\"canRead\":" + (canRead ? "true" : "false")
		+ ",\"canAppend\":false,\"canEdit\":false,\"canDelete\":false,\"sysAdminUnit\":{\"id\":\"" + granteeId + "\"}}";

	private string[] GetBodiesInCallOrder() =>
		_applicationClient.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(IApplicationClient.ExecutePostRequest)
				&& Equals(call.GetArguments()[0], GetUrl))
			.Select(call => (string)call.GetArguments()[1])
			.ToArray();

	[Test]
	[Description("The SysSchema candidate query orders ExtendParent ascending server-side (base row first) so a row cap can never cut off the administrable base row.")]
	public void GetObjectRights_ShouldOrderCandidatesByExtendParentServerSide_WhenResolvingUIds() {
		// Arrange
		GetReturns(AdministeredObject("base"));

		// Act
		_client.GetObjectRights("UsrFoo", new CreatioRequestOptions());

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
		ObjectRightsInfo info = _client.GetObjectRights("UsrFoo", new CreatioRequestOptions());

		// Assert
		info.Caption.Should().Be("base", because: "the base row answers, so it is the one used");
		string[] bodies = GetBodiesInCallOrder();
		bodies.Should().HaveCount(1, because: "a successful base probe must not be followed by probing the layers");
		bodies[0].Should().Contain(BaseUId, because: "the base row is probed first");
	}

	[Test]
	[Description("When the first candidate faults and the second answers, the save uses the SECOND candidate's node.")]
	public void SetObjectRights_ShouldSaveSecondCandidateNode_WhenFirstCandidateFaults() {
		// Arrange
		SelectReturnsUIds(BaseUId, LayerUId);
		GetReturnsFor(BaseUId, InBandJsonFault);
		GetReturnsFor(LayerUId, AdministeredObject("from-second"));

		// Act
		ObjectRightsChange result = _client.SetObjectRights("UsrFoo", Grantee, new[] { ObjectOperation.Read },
			revoke: false, disableOperationPermissions: false, new CreatioRequestOptions());

		// Assert
		result.Error.Should().BeNull(because: "the second candidate answered");
		result.Changed.Should().BeTrue(because: "the grant added a row");
		_savedPayload.Should().Contain("from-second", because: "the save must round-trip the node that actually answered");
	}

	[Test]
	[Description("When every candidate faults, the read reports a ReadError (never 'available') and the write reports an Error without saving.")]
	public void RightManagementServiceClient_ShouldReportError_WhenAllCandidatesFault() {
		// Arrange
		SelectReturnsUIds(BaseUId, LayerUId);
		GetReturnsFor(BaseUId, InBandJsonFault);
		GetReturnsFor(LayerUId, InBandJsonFault);

		// Act
		ObjectRightsInfo info = _client.GetObjectRights("UsrFoo", new CreatioRequestOptions());
		ObjectRightsChange change = _client.SetObjectRights("UsrFoo", Grantee, new[] { ObjectOperation.Read },
			revoke: false, disableOperationPermissions: false, new CreatioRequestOptions());

		// Assert
		info.Found.Should().BeTrue(because: "the schema exists; only its rights could not be read");
		info.ReadError.Should().Contain("Request Error", because: "a failed read must be surfaced, not reported as available");
		change.Error.Should().Contain("Request Error", because: "the write must report the read failure");
		_savedPayload.Should().BeNull(because: "nothing may be saved when no candidate could be read");
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
		_client.GetObjectRights("UsrFoo", new CreatioRequestOptions());

		// Assert
		string[] bodies = GetBodiesInCallOrder();
		bodies.Should().HaveCount(1, because: "the duplicate UId is probed once and the empty UId never");
		bodies[0].Should().Contain(BaseUId, because: "only the real candidate is probed");
	}

	// ---- Grant on a not-yet-administered object, positions, save failure (RC-9) ----

	[Test]
	[Description("A grant on an object that does not use operation permissions yet turns them ON, adds the row, and reports the enablement.")]
	public void SetObjectRights_ShouldEnableAndReportIt_WhenObjectNotAdministered() {
		// Arrange
		GetReturns(NotAdministeredObject(""));

		// Act
		ObjectRightsChange result = _client.SetObjectRights("UsrFoo", Grantee, new[] { ObjectOperation.Read },
			revoke: false, disableOperationPermissions: false, new CreatioRequestOptions());

		// Assert
		result.Changed.Should().BeTrue(because: "enabling operation permissions and adding a row is a change");
		result.OperationPermissionsEnabled.Should().BeTrue(
			because: "the flip narrows access for every other internal role, so it must be reported");
		_savedPayload.Should().Contain("\"administratedByOperations\":true", because: "the grant turns operation permissions on");
		_savedPayload.Should().Contain(Grantee.ToString(), because: "the grantee row is added");
	}

	[Test]
	[Description("On a not-administered object whose row already holds the requested flags, enabling still counts as a change and is saved.")]
	public void SetObjectRights_ShouldSaveEnablement_WhenRowAlreadyHoldsFlags() {
		// Arrange
		GetReturns(NotAdministeredObject(Row("x", 0, Grantee.ToString(), canRead: true)));

		// Act
		ObjectRightsChange result = _client.SetObjectRights("UsrFoo", Grantee, new[] { ObjectOperation.Read },
			revoke: false, disableOperationPermissions: false, new CreatioRequestOptions());

		// Assert
		result.Changed.Should().BeTrue(because: "turning operation permissions on is itself a change");
		result.OperationPermissionsEnabled.Should().BeTrue(because: "the object was not administered before");
		_savedPayload.Should().NotBeNull(because: "the enablement must be saved even though the row flags did not change");
	}

	[Test]
	[Description("A new row gets one past the highest existing position (positions may have gaps) and carries no id.")]
	public void SetObjectRights_ShouldPlaceNewRowAfterHighestPosition_WhenPositionsHaveGaps() {
		// Arrange
		GetReturns("{\"success\":true,\"administratedObject\":{\"name\":\"UsrFoo\",\"administratedByOperations\":true,"
			+ "\"entitySchemaOperationsRights\":[" + Row("a", 0, Guid.NewGuid().ToString(), true) + ","
			+ Row("b", 5, Guid.NewGuid().ToString(), true) + "]}}");

		// Act
		_client.SetObjectRights("UsrFoo", Grantee, new[] { ObjectOperation.Read },
			revoke: false, disableOperationPermissions: false, new CreatioRequestOptions());

		// Assert
		JsonElement newRow = JsonDocument.Parse(_savedPayload).RootElement
			.GetProperty("administratedObject").GetProperty("entitySchemaOperationsRights").EnumerateArray()
			.Single(row => row.GetProperty("sysAdminUnit").GetProperty("id").GetString() == Grantee.ToString());
		newRow.GetProperty("position").GetInt32().Should().Be(6, because: "a new row goes one past the highest position, not into a gap");
		newRow.TryGetProperty("id", out _).Should().BeFalse(because: "the server assigns the id of a new row");
	}

	[Test]
	[Description("A SaveAdministratedObject in-band failure is mapped to Error with Changed=false and no enablement flag.")]
	public void SetObjectRights_ShouldReportError_WhenSaveReportsFailure() {
		// Arrange
		GetReturns(NotAdministeredObject(""));
		Post(SaveUrl).Returns("{\"success\":false,\"errorInfo\":{\"message\":\"denied\"}}");

		// Act
		ObjectRightsChange result = _client.SetObjectRights("UsrFoo", Grantee, new[] { ObjectOperation.Read },
			revoke: false, disableOperationPermissions: false, new CreatioRequestOptions());

		// Assert
		result.Error.Should().Be("denied", because: "the service's failure message is surfaced");
		result.Changed.Should().BeFalse(because: "nothing was saved");
		result.OperationPermissionsEnabled.Should().BeFalse(because: "a failed save enabled nothing");
	}

	// ---- Save exception, multi-row revoke, projection (review round 4) ----

	private static readonly Guid Employees = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");

	private void GetReturnsTwoRowObject() =>
		GetReturns("{\"success\":true,\"administratedObject\":{\"name\":\"UsrFoo\",\"administratedByOperations\":true,"
			+ "\"entitySchemaOperationsRights\":["
			+ "{\"id\":\"x\",\"position\":0,\"canRead\":true,\"canAppend\":true,\"canEdit\":true,\"canDelete\":true,"
			+ "\"sysAdminUnit\":{\"id\":\"" + Grantee + "\",\"name\":\"All external users\"}},"
			+ "{\"id\":\"y\",\"position\":1,\"canRead\":true,\"canAppend\":true,\"canEdit\":true,\"canDelete\":false,"
			+ "\"sysAdminUnit\":{\"id\":\"" + Employees + "\",\"name\":\"All employees\"}}]}}");

	private JsonElement[] SavedRows() =>
		JsonDocument.Parse(_savedPayload).RootElement.GetProperty("administratedObject")
			.GetProperty("entitySchemaOperationsRights").EnumerateArray().ToArray();

	private static readonly ObjectOperation[] AllOperations =
		{ ObjectOperation.Read, ObjectOperation.Create, ObjectOperation.Edit, ObjectOperation.Delete };

	[Test]
	[Description("An exception thrown by the SaveAdministratedObject POST is reported as this object's Error instead of escaping, so a fan-out can name where it stopped and continue.")]
	public void SetObjectRights_ShouldReportError_WhenSaveThrows() {
		// Arrange
		GetReturns(NotAdministeredObject(""));
		Post(SaveUrl).Returns(_ => throw new InvalidOperationException("HTTP 500"));

		// Act
		ObjectRightsChange result = _client.SetObjectRights("UsrFoo", Grantee, new[] { ObjectOperation.Read },
			revoke: false, disableOperationPermissions: false, new CreatioRequestOptions());

		// Assert
		result.Error.Should().Contain("HTTP 500", because: "the transport failure is attributed to this object");
		result.Changed.Should().BeFalse(because: "nothing was saved");
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("A full revoke of one role while another row remains removes only that role's row and keeps operation permissions ON, whatever the disable opt-in says.")]
	public void SetObjectRights_ShouldKeepAdministered_WhenFullRevokeLeavesOtherRows(bool disable) {
		// Arrange
		GetReturnsTwoRowObject();

		// Act
		ObjectRightsChange result = _client.SetObjectRights("UsrFoo", Grantee, AllOperations,
			revoke: true, disableOperationPermissions: disable, new CreatioRequestOptions());

		// Assert
		result.Changed.Should().BeTrue(because: "the grantee's row was removed");
		result.RefusedLastRowRemoval.Should().BeFalse(because: "another row remains, so this is not the last row");
		result.OperationPermissionsDisabled.Should().BeFalse(because: "operation permissions stay on while any row remains");
		_savedPayload.Should().Contain("\"administratedByOperations\":true",
			because: "removing one role must never make the object available to every internal user");
		JsonElement[] rows = SavedRows();
		rows.Should().HaveCount(1, because: "only the grantee's row is removed");
		rows[0].GetProperty("sysAdminUnit").GetProperty("id").GetString().Should().Be(Employees.ToString(),
			because: "the other role's row is kept untouched");
	}

	[Test]
	[Description("A revoke for a grantee that holds no row is a no-op and does not call SaveAdministratedObject.")]
	public void SetObjectRights_ShouldNotSave_WhenRevokingGranteeWithoutRow() {
		// Arrange
		GetReturns(AdministeredObject("base"));

		// Act
		ObjectRightsChange result = _client.SetObjectRights("UsrFoo", Grantee, AllOperations,
			revoke: true, disableOperationPermissions: false, new CreatioRequestOptions());

		// Assert
		result.Changed.Should().BeFalse(because: "there was nothing to revoke");
		_applicationClient.DidNotReceive().ExecutePostRequest(SaveUrl, Arg.Any<string>(), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("The opt-in last-row revoke saves an empty rights array together with administratedByOperations=false.")]
	public void SetObjectRights_ShouldSaveEmptyRows_WhenLastRowRemovedWithOptIn() {
		// Arrange
		GetReturnsSingleRowObject();

		// Act
		_client.SetObjectRights("UsrFoo", Grantee, AllOperations,
			revoke: true, disableOperationPermissions: true, new CreatioRequestOptions());

		// Assert
		SavedRows().Should().BeEmpty(because: "the only row was removed");
	}

	[Test]
	[Description("A grant to a new role writes exactly the requested flags: canRead/canAppend/canEdit true, canDelete false.")]
	public void SetObjectRights_ShouldWriteExactFlags_WhenGrantingNewRole() {
		// Arrange
		GetReturns(AdministeredObject("base"));

		// Act
		_client.SetObjectRights("UsrFoo", Grantee,
			new[] { ObjectOperation.Read, ObjectOperation.Create, ObjectOperation.Edit }, revoke: false,
			disableOperationPermissions: false, new CreatioRequestOptions());

		// Assert
		JsonElement row = SavedRows().Single();
		row.GetProperty("canRead").GetBoolean().Should().BeTrue(because: "read was requested");
		row.GetProperty("canAppend").GetBoolean().Should().BeTrue(because: "create maps to canAppend");
		row.GetProperty("canEdit").GetBoolean().Should().BeTrue(because: "edit was requested");
		row.GetProperty("canDelete").GetBoolean().Should().BeFalse(because: "delete was not requested");
	}

	[Test]
	[Description("GetObjectRights projects every wire field onto RoleOperationRights: canAppend->CanCreate, the grantee id and name, and the administration flag.")]
	public void GetObjectRights_ShouldProjectEveryField_WhenRowsHaveDistinctFlags() {
		// Arrange
		GetReturns("{\"success\":true,\"administratedObject\":{\"name\":\"UsrFoo\",\"caption\":\"Foo\",\"administratedByOperations\":true,"
			+ "\"entitySchemaOperationsRights\":["
			+ "{\"canRead\":true,\"canAppend\":false,\"canEdit\":true,\"canDelete\":false,"
			+ "\"sysAdminUnit\":{\"id\":\"" + Grantee + "\",\"name\":\"All external users\"}},"
			+ "{\"canRead\":false,\"canAppend\":true,\"canEdit\":false,\"canDelete\":true,"
			+ "\"sysAdminUnit\":{\"id\":\"" + Employees + "\",\"name\":\"All employees\"}}]}}");

		// Act
		ObjectRightsInfo info = _client.GetObjectRights("UsrFoo", new CreatioRequestOptions());

		// Assert
		info.AdministratedByOperations.Should().BeTrue(because: "the flag is read from the node");
		info.Caption.Should().Be("Foo", because: "the caption is read from the node");
		info.Roles.Should().BeEquivalentTo(new[] {
			new RoleOperationRights(Grantee, "All external users", true, false, true, false),
			new RoleOperationRights(Employees, "All employees", false, true, false, true)
		}, options => options.WithStrictOrdering(),
			because: "a swapped field would give a false coverage verdict");
	}

	[TestCase("{\"id\":\"not-a-guid\"}")]
	[TestCase("{}")]
	[Description("A missing or non-GUID grantee id reads as Guid.Empty, a missing name as (unknown), and non-bool flags as false.")]
	public void GetObjectRights_ShouldFallBack_WhenRowFieldsAreMissingOrMalformed(string sysAdminUnit) {
		// Arrange
		GetReturns("{\"success\":true,\"administratedObject\":{\"name\":\"UsrFoo\",\"administratedByOperations\":true,"
			+ "\"entitySchemaOperationsRights\":[{\"canRead\":\"true\",\"canAppend\":1,\"canEdit\":null,"
			+ "\"sysAdminUnit\":" + sysAdminUnit + "}]}}");

		// Act
		ObjectRightsInfo info = _client.GetObjectRights("UsrFoo", new CreatioRequestOptions());

		// Assert
		RoleOperationRights role = info.Roles.Single();
		role.GranteeId.Should().Be(Guid.Empty, because: "an unparseable id never matches a real grantee");
		role.GranteeName.Should().Be("(unknown)", because: "a missing name falls back to a placeholder");
		(role.CanRead || role.CanCreate || role.CanEdit || role.CanDelete).Should().BeFalse(
			because: "a non-bool flag is not a grant");
	}

	// ---- Review round 5: real fault shapes in the probe loop, existing-row grants/revokes, non-administered revoke ----

	private const string HtmlRequestErrorPage =
		"<?xml version=\"1.0\" encoding=\"utf-8\"?><!DOCTYPE html><html><body>Request Error</body></html>";

	private void GetThrowsFor(string uId, Exception exception) =>
		_applicationClient.ExecutePostRequest(GetUrl, Arg.Is<string>(body => body.Contains(uId)), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>()).Returns(_ => throw exception);

	private void GetReturnsRows(bool administered, params string[] rows) =>
		GetReturns("{\"success\":true,\"administratedObject\":{\"name\":\"UsrFoo\",\"administratedByOperations\":"
			+ (administered ? "true" : "false") + ",\"entitySchemaOperationsRights\":[" + string.Join(",", rows) + "]}}");

	private static string FlagRow(Guid grantee, int position, bool read, bool append, bool edit, bool delete) =>
		"{\"id\":\"r" + position + "\",\"position\":" + position + ",\"canRead\":" + Lower(read) + ",\"canAppend\":"
		+ Lower(append) + ",\"canEdit\":" + Lower(edit) + ",\"canDelete\":" + Lower(delete)
		+ ",\"sysAdminUnit\":{\"id\":\"" + grantee + "\"}}";

	private static string Lower(bool value) => value ? "true" : "false";

	private void AssertNoSave(string because) =>
		_applicationClient.DidNotReceive().ExecutePostRequest(SaveUrl, Arg.Any<string>(), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>());

	[Test]
	[Description("A first candidate answering with the real non-JSON 'Request Error' page is skipped, and the save uses the second candidate's node.")]
	public void SetObjectRights_ShouldUseSecondCandidate_WhenFirstReturnsHtmlErrorPage() {
		// Arrange
		SelectReturnsUIds(BaseUId, LayerUId);
		GetReturnsFor(BaseUId, HtmlRequestErrorPage);
		GetReturnsFor(LayerUId, AdministeredObject("from-second"));

		// Act
		ObjectRightsChange result = _client.SetObjectRights("UsrFoo", Grantee, new[] { ObjectOperation.Read },
			revoke: false, disableOperationPermissions: false, new CreatioRequestOptions());

		// Assert
		result.Error.Should().BeNull(because: "the second candidate answered");
		_savedPayload.Should().Contain("from-second", because: "the node that actually answered is the one saved");
	}

	[Test]
	[Description("A first candidate whose request throws (HTTP 500) is skipped, and the read uses the second candidate.")]
	public void GetObjectRights_ShouldUseSecondCandidate_WhenFirstRequestThrows() {
		// Arrange
		SelectReturnsUIds(BaseUId, LayerUId);
		GetThrowsFor(BaseUId, new InvalidOperationException("HTTP 500"));
		GetReturnsFor(LayerUId, AdministeredObject("from-second"));

		// Act
		ObjectRightsInfo info = _client.GetObjectRights("UsrFoo", new CreatioRequestOptions());

		// Assert
		info.ReadError.Should().BeNull(because: "the second candidate answered");
		info.Caption.Should().Be("from-second", because: "a throwing candidate must not end the probe loop");
	}

	[Test]
	[Description("When every candidate throws or returns an HTML page, the read reports a ReadError and the write reports an Error without saving.")]
	public void RightManagementServiceClient_ShouldReportError_WhenEveryCandidateFailsWithRealFaults() {
		// Arrange
		SelectReturnsUIds(BaseUId, LayerUId);
		GetReturnsFor(BaseUId, HtmlRequestErrorPage);
		GetThrowsFor(LayerUId, new InvalidOperationException("HTTP 500"));

		// Act
		ObjectRightsInfo info = _client.GetObjectRights("UsrFoo", new CreatioRequestOptions());
		ObjectRightsChange change = _client.SetObjectRights("UsrFoo", Grantee, new[] { ObjectOperation.Read },
			revoke: false, disableOperationPermissions: false, new CreatioRequestOptions());

		// Assert
		info.ReadError.Should().NotBeNullOrEmpty(because: "a failed read is surfaced, never reported as available");
		change.Error.Should().NotBeNullOrEmpty(because: "the write reports the read failure");
		AssertNoSave("nothing may be saved when no candidate could be read");
	}

	[Test]
	[Description("A SysSchema SelectQuery that throws is reported as a read error / write error, not as an escaping exception.")]
	public void RightManagementServiceClient_ShouldReportError_WhenSelectQueryThrows() {
		// Arrange
		Post(SelectUrl).Returns(_ => throw new InvalidOperationException("select failed"));

		// Act
		ObjectRightsInfo info = _client.GetObjectRights("UsrFoo", new CreatioRequestOptions());
		ObjectRightsChange change = _client.SetObjectRights("UsrFoo", Grantee, new[] { ObjectOperation.Read },
			revoke: false, disableOperationPermissions: false, new CreatioRequestOptions());

		// Assert
		info.ReadError.Should().Contain("select failed", because: "the resolution failure is surfaced");
		change.Error.Should().Contain("select failed", because: "the write reports the resolution failure");
		AssertNoSave("nothing may be saved when the object could not be resolved");
	}

	[Test]
	[Description("Granting an extra operation to an existing row keeps every flag the row already held and adds only the requested one.")]
	public void SetObjectRights_ShouldKeepExistingFlags_WhenGrantingToExistingRow() {
		// Arrange
		GetReturnsRows(true, FlagRow(Grantee, 0, read: true, append: false, edit: false, delete: true));

		// Act
		ObjectRightsChange result = _client.SetObjectRights("UsrFoo", Grantee, new[] { ObjectOperation.Edit },
			revoke: false, disableOperationPermissions: false, new CreatioRequestOptions());

		// Assert
		result.Changed.Should().BeTrue(because: "edit was added");
		JsonElement row = SavedRows().Single();
		row.GetProperty("canRead").GetBoolean().Should().BeTrue(because: "read was already held and must survive");
		row.GetProperty("canDelete").GetBoolean().Should().BeTrue(because: "delete was not requested but was held");
		row.GetProperty("canEdit").GetBoolean().Should().BeTrue(because: "edit was requested");
		row.GetProperty("canAppend").GetBoolean().Should().BeFalse(because: "create was neither held nor requested");
	}

	[Test]
	[Description("Revoking an operation the grantee does not hold changes nothing and does not save, on a multi-row object.")]
	public void SetObjectRights_ShouldNotSave_WhenRevokingOperationNotHeld() {
		// Arrange
		GetReturnsRows(true,
			FlagRow(Grantee, 0, read: true, append: false, edit: false, delete: false),
			FlagRow(Employees, 1, read: true, append: true, edit: true, delete: true));

		// Act
		ObjectRightsChange result = _client.SetObjectRights("UsrFoo", Grantee, new[] { ObjectOperation.Delete },
			revoke: true, disableOperationPermissions: false, new CreatioRequestOptions());

		// Assert
		result.Changed.Should().BeFalse(because: "the grantee never held delete");
		AssertNoSave("a no-op revoke must not save");
	}

	[Test]
	[Description("Re-running a partial revoke on the object's only row is a no-op, not a last-row refusal.")]
	public void SetObjectRights_ShouldReportNoChange_WhenPartialRevokeIsRerunOnOnlyRow() {
		// Arrange — the first run already cleared create, so the row keeps read only.
		GetReturnsRows(true, FlagRow(Grantee, 0, read: true, append: false, edit: false, delete: false));

		// Act
		ObjectRightsChange result = _client.SetObjectRights("UsrFoo", Grantee, new[] { ObjectOperation.Create },
			revoke: true, disableOperationPermissions: false, new CreatioRequestOptions());

		// Assert
		result.Changed.Should().BeFalse(because: "create is already gone");
		result.RefusedLastRowRemoval.Should().BeFalse(because: "nothing would be removed, so there is nothing to refuse");
		AssertNoSave("a re-run that changes nothing must not save");
	}

	[Test]
	[Description("Revoking on an object's only row whose flags are already all false is a no-op, not a last-row refusal.")]
	public void SetObjectRights_ShouldReportNoChange_WhenOnlyRowAlreadyHoldsNothing() {
		// Arrange
		GetReturnsRows(true, FlagRow(Grantee, 0, read: false, append: false, edit: false, delete: false));

		// Act
		ObjectRightsChange result = _client.SetObjectRights("UsrFoo", Grantee, AllOperations,
			revoke: true, disableOperationPermissions: false, new CreatioRequestOptions());

		// Assert
		result.Changed.Should().BeFalse(because: "the grantee holds nothing to revoke");
		result.RefusedLastRowRemoval.Should().BeFalse(because: "a revoke that changes nothing is never refused");
		AssertNoSave("nothing changed");
	}

	[TestCase(false, false)]
	[TestCase(true, false)]
	[TestCase(true, true)]
	[Description("A revoke on an object that is not administered by operation permissions restricts nothing: it reports RevokeOnNotAdministered, never a refusal or a change, and does not save — with or without a stale row, and whatever the disable opt-in says.")]
	public void SetObjectRights_ShouldReportRevokeOnNotAdministered_WhenObjectNotAdministered(bool staleRow, bool disable) {
		// Arrange
		if (staleRow) {
			GetReturnsRows(false, FlagRow(Grantee, 0, read: true, append: false, edit: false, delete: false));
		} else {
			GetReturnsRows(false);
		}

		// Act
		ObjectRightsChange result = _client.SetObjectRights("UsrFoo", Grantee, new[] { ObjectOperation.Read },
			revoke: true, disableOperationPermissions: disable, new CreatioRequestOptions());

		// Assert
		result.RevokeOnNotAdministered.Should().BeTrue(because: "every internal user reaches a non-administered object");
		result.Changed.Should().BeFalse(because: "nothing was written");
		result.RefusedLastRowRemoval.Should().BeFalse(because: "the object is already open to internal users");
		AssertNoSave("no restriction can be written to a non-administered object");
	}
}
