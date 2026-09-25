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
		_savedPayload.Should().Contain("\"entitySchemaRecordDefRights\":null", because: "unchanged collections are nulled");
		_savedPayload.Should().Contain("\"entitySchemaColumnsRights\":null", because: "unchanged collections are nulled");
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

	private const string InBandFault = "{\"success\":false,\"errorInfo\":{\"message\":\"Request Error\"}}";

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
		GetReturnsFor(BaseUId, InBandFault);
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
		GetReturnsFor(BaseUId, InBandFault);
		GetReturnsFor(LayerUId, InBandFault);

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
		GetReturnsFor(BaseUId, InBandFault);

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
}
