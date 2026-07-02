using System;
using System.Net.Http;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Common.Responses;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
internal class RemoteEntitySchemaDesignerClientTests
{
	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private RemoteEntitySchemaDesignerClient _client;

	[SetUp]
	public void Setup() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build("ServiceModel/EntitySchemaDesignerService.svc")
			.Returns("http://local/ServiceModel/EntitySchemaDesignerService.svc");
		_client = new RemoteEntitySchemaDesignerClient(_applicationClient, new JsonConverter(), _serviceUrlBuilder);
	}

	[Test]
	[Description("Deserializes designer responses directly when the service already returns valid JSON.")]
	public void GetSchemaDesignItem_DeserializesRawJsonResponse() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<int>())
			.Returns("{\"success\":true,\"schema\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrCodex0307\",\"columns\":[],\"inheritedColumns\":[],\"indexes\":[]}}");

		// Act
		Clio.Command.EntitySchemaDesigner.DesignerResponse<EntityDesignSchemaDto> response =
			_client.GetSchemaDesignItem(new GetSchemaDesignItemRequestDto {
			Name = "UsrCodex0307",
			PackageUId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
			UseFullHierarchy = true
		}, new RemoteCommandOptions());

		// Assert
		response.Should().NotBeNull(because: "a valid designer payload should deserialize without correction");
		response.Success.Should().BeTrue(because: "the response body marks the request as successful");
		response.Schema.Name.Should().Be("UsrCodex0307", because: "the schema payload should remain intact");
	}

	[Test]
	[Description("Falls back to corrected JSON when the response body is string-escaped.")]
	public void GetSchemaDesignItem_FallsBackToCorrectedJson_WhenRawResponseIsEscaped() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<int>())
			.Returns("\"{\\\"success\\\":true,\\\"schema\\\":{\\\"uId\\\":\\\"11111111-1111-1111-1111-111111111111\\\",\\\"name\\\":\\\"UsrCodex0307\\\",\\\"columns\\\":[],\\\"inheritedColumns\\\":[],\\\"indexes\\\":[]}}\"");

		// Act
		Clio.Command.EntitySchemaDesigner.DesignerResponse<EntityDesignSchemaDto> response =
			_client.GetSchemaDesignItem(new GetSchemaDesignItemRequestDto {
			Name = "UsrCodex0307",
			PackageUId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
			UseFullHierarchy = true
		}, new RemoteCommandOptions());

		// Assert
		response.Should().NotBeNull(because: "legacy escaped payloads should still be supported");
		response.Schema.Name.Should().Be("UsrCodex0307", because: "fallback correction should preserve the schema");
	}

	[Test]
	[Description("Surfaces the missing-dependency root cause and the add-package-dependency recovery when the server returns an HTML error page, so an agent reaches the one-call fix instead of burning workaround detours (ENG-91314).")]
	public void GetSchemaDesignItem_ShouldSurfaceDependencyRecovery_WhenServerReturnsHtmlErrorPage() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("<!DOCTYPE html><html><body>Server Error in '/' Application.</body></html>");

		// Act
		Action act = () => _client.GetSchemaDesignItem(new GetSchemaDesignItemRequestDto {
			Name = "Opportunity",
			PackageUId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
			UseFullHierarchy = true
		}, new RemoteCommandOptions());

		// Assert
		InvalidOperationException exception = act.Should().Throw<InvalidOperationException>(
				because: "an HTML error page is never a valid designer payload and must fail loudly")
			.Which;
		exception.Message.Should().Contain("add-package-dependency",
			because: "the missing-dependency cause is the most common one and the message must point the caller at the one-call fix");
		exception.Message.Should().Contain("MISSING A DEPENDENCY",
			because: "the message must name the missing-dependency root cause that misdirected agents previously missed");
		exception.Message.Should().Contain("find-entity-schema",
			because: "the stale-table cause must remain documented as the secondary check");
		exception.Message.Should().Contain("package-dependencies",
			because: "the message must actively point MCP agents at the package-dependencies guidance article");
	}

	[Test]
	[Description("Posts schema UIds to SchemaDesignerRequest so saved entity schemas can be materialized in the runtime database.")]
	public void SaveSchemaDbStructure_PostsSchemaDesignerRequest() {
		// Arrange
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SchemaDesignerRequest)
			.Returns("http://local/DataService/json/SyncReply/SchemaDesignerRequest");
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<int>())
			.Returns("{\"success\":true}");
		Guid schemaUId = Guid.Parse("11111111-1111-1111-1111-111111111111");

		// Act
		BaseResponse response = _client.SaveSchemaDbStructure(schemaUId, new RemoteCommandOptions());

		// Assert
		response.Success.Should().BeTrue();
		_applicationClient.Received(1).ExecutePostRequest(
			"http://local/DataService/json/SyncReply/SchemaDesignerRequest",
			Arg.Is<string>(body => body.Contains("saveSchemaDBStructure") && body.Contains(schemaUId.ToString())),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Description("Posts buildWorkspace and buildChangedConfiguration flags to SchemaDesignerRequest so saved schemas get published on every runtime generation (ENG-90403).")]
	public void PublishConfigurationChanges_PostsBuildFlagsToSchemaDesignerRequest() {
		// Arrange
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SchemaDesignerRequest)
			.Returns("http://local/DataService/json/SyncReply/SchemaDesignerRequest");
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<int>())
			.Returns("{\"success\":true}");

		// Act
		BaseResponse response = _client.PublishConfigurationChanges(new RemoteCommandOptions());

		// Assert
		response.Success.Should().BeTrue(because: "a successful publish response must surface to the caller");
		_applicationClient.Received(1).ExecutePostRequest(
			"http://local/DataService/json/SyncReply/SchemaDesignerRequest",
			Arg.Is<string>(body => ContainsJsonFlag(body, "buildWorkspace")
				&& ContainsJsonFlag(body, "buildChangedConfiguration")),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Description("Publish uses the build-class timeout and a single attempt (maxAttempts=1) so a slow-but-successful legacy BuildWorkspace is not mistaken for a failure and not re-issued (ENG-90403).")]
	public void PublishConfigurationChanges_UsesBuildClassTimeout_AndSingleAttempt() {
		// Arrange
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SchemaDesignerRequest)
			.Returns("http://local/DataService/json/SyncReply/SchemaDesignerRequest");
		int capturedTimeout = 0;
		int capturedMaxAttempts = -1;
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<int>())
			.Returns(callInfo => {
				capturedTimeout = callInfo.ArgAt<int>(2);
				capturedMaxAttempts = callInfo.ArgAt<int>(3);
				return "{\"success\":true}";
			});

		// Act
		_client.PublishConfigurationChanges(new RemoteCommandOptions());

		// Assert
		capturedTimeout.Should().Be(RemoteEntitySchemaDesignerClient.PublishConfigurationTimeoutMs,
			because: "a full server-side BuildWorkspace on a legacy instance can exceed 100s; publish must use the build-class timeout");
		capturedMaxAttempts.Should().Be(1,
			because: "publish must issue exactly one attempt and no retry — the build POST is non-idempotent and retrying a timed-out build may stack concurrent compiles");
	}

	private static bool ContainsJsonFlag(string body, string flagName) {
		string normalizedBody = body.Replace(" ", string.Empty)
			.Replace("\r", string.Empty)
			.Replace("\n", string.Empty);
		return normalizedBody.Contains($"\"{flagName}\":true", StringComparison.Ordinal);
	}

	[Test]
	[Description("Posts to WorkspaceExplorerService.svc/RunODataBuild so a freshly published schema is rebuilt into the OData entities assembly without a manual full compile (ENG-92048).")]
	public void RunODataBuild_ShouldPostToWorkspaceExplorerWithSingleAttempt_WhenInvoked() {
		// Arrange
		_serviceUrlBuilder.Build("ServiceModel/WorkspaceExplorerService.svc")
			.Returns("http://local/ServiceModel/WorkspaceExplorerService.svc");
		int capturedMaxAttempts = -1;
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<int>())
			.Returns(callInfo => {
				capturedMaxAttempts = callInfo.ArgAt<int>(3);
				return "{\"success\":true}";
			});

		// Act — seed a non-default MaxAttempts (default is 3) so the assertion below actively distinguishes
		// the hard-coded literal 1 from a regression that accidentally forwards options.MaxAttempts.
		BaseResponse response = _client.RunODataBuild(new RemoteCommandOptions { MaxAttempts = 5 });

		// Assert
		response.Success.Should().BeTrue(because: "a successful RunODataBuild response must surface to the caller");
		_applicationClient.Received(1).ExecutePostRequest(
			"http://local/ServiceModel/WorkspaceExplorerService.svc/RunODataBuild",
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
		capturedMaxAttempts.Should().Be(1,
			because: "triggering the OData build is non-idempotent — it must issue exactly one attempt with no retry so a timed-out trigger does not stack concurrent builds, regardless of the options value (seeded to 5 here)");
	}

	[Test]
	[Description("Throws an actionable error when RunODataBuild reports failure so the caller can decide how to react (the creator swallows it as a warning) (ENG-92048).")]
	public void RunODataBuild_ShouldThrow_WhenServiceReportsFailure() {
		// Arrange
		_serviceUrlBuilder.Build("ServiceModel/WorkspaceExplorerService.svc")
			.Returns("http://local/ServiceModel/WorkspaceExplorerService.svc");
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<int>())
			.Returns("{\"success\":false,\"errorInfo\":{\"message\":\"OData build refused.\"}}");

		// Act
		Action act = () => _client.RunODataBuild(new RemoteCommandOptions());

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*OData build refused.*",
				because: "an unsuccessful RunODataBuild response must surface the server error message");
	}

	[Test]
	[Description("Loads runtime entity schemas by UId so callers can verify DB-first availability after SaveSchemaDBStructure.")]
	public void GetRuntimeEntitySchema_PostsRuntimeSchemaRequest() {
		// Arrange
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest)
			.Returns("http://local/DataService/json/SyncReply/RuntimeEntitySchemaRequest");
		Guid schemaUId = Guid.Parse("11111111-1111-1111-1111-111111111111");
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<int>())
			.Returns("{\"success\":true,\"schema\":{\"uId\":\"11111111-1111-1111-1111-111111111111\",\"name\":\"UsrRuntimeVehicle\"}}");

		// Act
		RuntimeEntitySchemaResponse response = _client.GetRuntimeEntitySchema(schemaUId, new RemoteCommandOptions());

		// Assert
		response.Success.Should().BeTrue();
		response.Schema.Should().NotBeNull();
		response.Schema!.Name.Should().Be("UsrRuntimeVehicle");
		_applicationClient.Received(1).ExecutePostRequest(
			"http://local/DataService/json/SyncReply/RuntimeEntitySchemaRequest",
			Arg.Is<string>(body => body.Contains("\"uId\"") && body.Contains(schemaUId.ToString())),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Description("Reports Exists when the referenced-record SelectQuery returns a row.")]
	public void CheckRecordExists_ReturnsExists_WhenRowReturned() {
		// Arrange
		Guid recordId = Guid.Parse("d1a6ea58-6a88-4cb7-bfea-7a41caa0ae50");
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns("http://local/DataService/Select");
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns($"{{\"success\":true,\"rows\":[{{\"Id\":\"{recordId:D}\"}}]}}");

		// Act
		LookupRecordExistence result = _client.CheckRecordExists("UsrEng91318Color", recordId, new RemoteCommandOptions());

		// Assert
		result.Should().Be(LookupRecordExistence.Exists,
			because: "a returned row confirms the referenced record exists");
	}

	[Test]
	[Description("Reports NotFound when the referenced-record SelectQuery returns no rows.")]
	public void CheckRecordExists_ReturnsNotFound_WhenNoRows() {
		// Arrange
		Guid recordId = Guid.Parse("d1a6ea58-6a88-4cb7-bfea-7a41caa0ae50");
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns("http://local/DataService/Select");
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("{\"success\":true,\"rows\":[]}");

		// Act
		LookupRecordExistence result = _client.CheckRecordExists("UsrEng91318Color", recordId, new RemoteCommandOptions());

		// Assert
		result.Should().Be(LookupRecordExistence.NotFound,
			because: "an empty result means no record with that id exists in the referenced schema");
	}

	[Test]
	[Description("Reports Unknown when the existence query fails, so an unverifiable check never blocks a write.")]
	public void CheckRecordExists_ReturnsUnknown_WhenServiceReportsFailure() {
		// Arrange
		Guid recordId = Guid.Parse("d1a6ea58-6a88-4cb7-bfea-7a41caa0ae50");
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns("http://local/DataService/Select");
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("{\"success\":false,\"errorInfo\":{\"message\":\"Current user does not have permission\"}}");

		// Act
		LookupRecordExistence result = _client.CheckRecordExists("UsrEng91318Color", recordId, new RemoteCommandOptions());

		// Assert
		result.Should().Be(LookupRecordExistence.Unknown,
			because: "a failed existence query must degrade to Unknown rather than block the write");
	}

	[Test]
	[Description("Reports Unknown when the existence query throws a transport fault, so a write is never blocked on an unverifiable check.")]
	public void CheckRecordExists_ReturnsUnknown_WhenTransportFaultThrows() {
		// Arrange
		Guid recordId = Guid.Parse("d1a6ea58-6a88-4cb7-bfea-7a41caa0ae50");
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns("http://local/DataService/Select");
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(_ => throw new HttpRequestException("connection reset"));

		// Act
		LookupRecordExistence result = _client.CheckRecordExists("UsrEng91318Color", recordId, new RemoteCommandOptions());

		// Assert
		result.Should().Be(LookupRecordExistence.Unknown,
			because: "a transport fault must degrade to Unknown instead of aborting a previously-working column write");
	}

	[Test]
	[Description("Reports NotFound (no NullReferenceException) when the existence query returns a null rows array.")]
	public void CheckRecordExists_ReturnsNotFound_WhenRowsIsNull() {
		// Arrange
		Guid recordId = Guid.Parse("d1a6ea58-6a88-4cb7-bfea-7a41caa0ae50");
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns("http://local/DataService/Select");
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("{\"success\":true,\"rows\":null}");

		// Act
		LookupRecordExistence result = _client.CheckRecordExists("UsrEng91318Color", recordId, new RemoteCommandOptions());

		// Assert
		result.Should().Be(LookupRecordExistence.NotFound,
			because: "a null rows array must be treated as no record found, not throw a NullReferenceException");
	}
}
