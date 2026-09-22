using System;
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
}
