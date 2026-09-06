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
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetEntitySchemaDesignItem)
			.Returns("http://local/ServiceModel/EntitySchemaDesignerService.svc/GetSchemaDesignItem");
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
		_serviceUrlBuilder.Received(1).Build(ServiceUrlBuilder.KnownRoute.GetEntitySchemaDesignItem);
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
	[Description("Reports an HTML designer response as a classified, authoritative error naming the method and endpoint, and asserts no cause it has no evidence for - in particular not the stale-database-table claim the previous text made on every HTML body (issue #722).")]
	public void GetSchemaDesignItem_ShouldReportObservedFactsOnly_WhenServerReturnsHtmlErrorPage() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("<!DOCTYPE html><html><body>Server Error in '/' Application. secret-body-marker</body></html>");

		// Act
		Action act = () => _client.GetSchemaDesignItem(new GetSchemaDesignItemRequestDto {
			Name = "Opportunity",
			PackageUId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
			UseFullHierarchy = true
		}, new RemoteCommandOptions());

		// Assert
		// Asserted on the exact type, not on the InvalidOperationException base it derives from: the base
		// assertion passes either way, so reverting the throw would silently drop the
		// IAuthoritativeErrorMessage marker and let the MCP boundary unwrap this classification back to the
		// raw parser text (ENG-93365).
		Clio.Package.NonJsonServiceResponseException exception =
			act.Should().Throw<Clio.Package.NonJsonServiceResponseException>(
					because: "an HTML error page is never a valid designer payload and must fail loudly as the classified non-JSON type")
				.Which;
		exception.Should().BeAssignableTo<IAuthoritativeErrorMessage>(
			because: "the marker is what stops the MCP unwrap from replacing this classification with the parser message");
		exception.Should().BeAssignableTo<InvalidOperationException>(
			because: "existing catch clauses on InvalidOperationException must keep working");
		exception.Message.Should().Contain("GetSchemaDesignItem",
			because: "the caller must be told which designer method produced the unusable body");
		exception.Message.Should().Contain(
			"http://local/ServiceModel/EntitySchemaDesignerService.svc/GetSchemaDesignItem",
			because: "the endpoint URL is what lets the caller tell which of several requests failed");
		exception.Message.Should().NotContain("secret-body-marker",
			because: "an error or sign-in page can carry session tokens, so the HTML body must never be echoed");
		exception.Message.Should().NotContain("stale database table",
			because: "no check anywhere produces evidence for a stale table, so the message must not assert it (issue #722)");
		exception.Message.Should().NotContain("previously deleted package",
			because: "a deleted package is a cause this class cannot observe and must not claim");
		exception.Message.Should().NotContain("MISSING A DEPENDENCY",
			because: "this class has not looked up any package, so the missing-dependency diagnosis belongs to the caller that did");
	}

	[Test]
	[Description("Classifies the Creatio sign-in response as an authentication failure with its own exception type, so a caller cannot attach a missing-dependency diagnosis to what is really an expired session (issue #722).")]
	public void GetSchemaDesignItem_ShouldReportSessionExpiry_WhenServerReturnsLoginPage() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("<!DOCTYPE html><html><body><form action=\"/Login/NuiLogin.aspx\">login-body-marker</form></body></html>");

		// Act
		Action act = () => _client.GetSchemaDesignItem(new GetSchemaDesignItemRequestDto {
			Name = "Opportunity",
			PackageUId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
			UseFullHierarchy = true
		}, new RemoteCommandOptions());

		// Assert
		Clio.Package.SessionExpiredServiceResponseException exception =
			act.Should().Throw<Clio.Package.SessionExpiredServiceResponseException>(
					because: "the sign-in response is an authentication failure, and its own type is what lets callers skip it when enriching an error")
				.Which;
		exception.Should().BeAssignableTo<Clio.Package.NonJsonServiceResponseException>(
			because: "a sign-in page is still a non-JSON service response, so existing catch clauses keep working");
		exception.Message.Should().Contain("credentials",
			because: "the recovery for an expired session is a credential check, not a package change");
		exception.Message.Should().NotContain("login-body-marker",
			because: "a sign-in page can carry session tokens, so its body must never be echoed");
		exception.Message.Should().NotContain("add-package-dependency",
			because: "this response says nothing about packages, so it must not steer the caller into changing one");
		exception.Message.Should().Contain("do not read it as a missing dependency",
			because: "the misreading this exception type exists to prevent must be stated where the caller sees it");
	}

	[Test]
	[Description("Treats a bare markup fragment such as an IIS or proxy '<div>' body as markup, not as generic garbage whose body would be previewed back to the caller (issue #722).")]
	public void GetSchemaDesignItem_ShouldTreatMarkupFragmentAsMarkup_WhenBodyIsNotAFullHtmlDocument() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("<div>Request blocked. fragment-body-marker</div>");

		// Act
		Action act = () => _client.GetSchemaDesignItem(new GetSchemaDesignItemRequestDto {
			Name = "Opportunity",
			PackageUId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
			UseFullHierarchy = true
		}, new RemoteCommandOptions());

		// Assert
		Clio.Package.NonJsonServiceResponseException exception =
			act.Should().Throw<Clio.Package.NonJsonServiceResponseException>(
					because: "a markup fragment is not a designer payload either and must fail as the classified type")
				.Which;
		exception.Message.Should().Contain("HTML/XML page instead of JSON",
			because: "the narrow doctype/html/xml prefix test used to miss fragments and send them down the body-previewing branch");
		exception.Message.Should().NotContain("fragment-body-marker",
			because: "the markup branch must never echo the body, whatever shape the markup takes");
	}

	[Test]
	[Description("Reports an empty designer response as its own case, so an accepted-but-silent request is not reported as unparseable content (issue #722).")]
	public void GetSchemaDesignItem_ShouldReportEmptyBody_WhenServerReturnsNothing() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("   ");

		// Act
		Action act = () => _client.GetSchemaDesignItem(new GetSchemaDesignItemRequestDto {
			Name = "Opportunity",
			PackageUId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
			UseFullHierarchy = true
		}, new RemoteCommandOptions());

		// Assert
		act.Should().Throw<Clio.Package.NonJsonServiceResponseException>(
				because: "an empty body is a distinct failure whose recovery is a retry, not a package change")
			.WithMessage("*empty response*",
				because: "the caller must be able to tell an empty body from an unparseable one");
	}

	[Test]
	[Description("Caps and redacts the preview of a non-markup unparseable body instead of copying up to a kilobyte of raw response into the message, which is what the removed local Truncate path did (issue #722).")]
	public void GetSchemaDesignItem_ShouldBoundAndRedactPreview_WhenBodyIsUnparseableAndNotMarkup() {
		// Arrange — a bearer token followed by enough filler to exceed the guard's 200-character preview cap.
		string body = "authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl "
			+ new string('x', 600) + " trailing-body-marker";
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(body);

		// Act
		Action act = () => _client.GetSchemaDesignItem(new GetSchemaDesignItemRequestDto {
			Name = "Opportunity",
			PackageUId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
			UseFullHierarchy = true
		}, new RemoteCommandOptions());

		// Assert
		Clio.Package.NonJsonServiceResponseException exception =
			act.Should().Throw<Clio.Package.NonJsonServiceResponseException>(
					because: "an unparseable non-markup body must still fail as the classified type")
				.Which;
		exception.Message.Should().NotContain("eyJzdWIiOiIxIn0",
			because: "the shared guard redacts the preview, which the removed local Truncate path did not");
		exception.Message.Should().NotContain("trailing-body-marker",
			because: "the preview is capped at 200 characters, so the tail of a long body never reaches the message");
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
	[Description("Issues exactly one attempt for the IsODataBuildRunning probe, regardless of the command-level MaxAttempts, so a failing poll does not burn three attempts plus backoff before the gate gives up.")]
	public void TryGetIsODataBuildRunning_ShouldIssueSingleAttempt_RegardlessOfCommandMaxAttempts() {
		// Arrange
		_serviceUrlBuilder.Build("ServiceModel/WorkspaceExplorerService.svc")
			.Returns("http://local/ServiceModel/WorkspaceExplorerService.svc");
		int capturedMaxAttempts = -1;
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(callInfo => {
				capturedMaxAttempts = callInfo.ArgAt<int>(3);
				return "{\"success\":true,\"value\":false}";
			});

		// Act — seed a non-default MaxAttempts (default is 3) so the assertion distinguishes the hard-coded 1
		// from a regression that forwards options.MaxAttempts.
		_client.TryGetIsODataBuildRunning(new RemoteCommandOptions { MaxAttempts = 5 });

		// Assert
		capturedMaxAttempts.Should().Be(1,
			because: "the probe is a status read whose answer is stale the moment it arrives, and the gate polls again anyway — retrying it only makes a faulted poll cost three attempts plus backoff before the publish can proceed");
	}

	[Test]
	[Description("Returns the reported running state when the server answers IsODataBuildRunning with JSON.")]
	public void TryGetIsODataBuildRunning_ReturnsValue_WhenServerRespondsWithJson() {
		// Arrange
		_serviceUrlBuilder.Build("ServiceModel/WorkspaceExplorerService.svc")
			.Returns("http://local/ServiceModel/WorkspaceExplorerService.svc");
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<int>())
			.Returns("{\"success\":true,\"value\":true}");

		// Act
		bool? isRunning = _client.TryGetIsODataBuildRunning(new RemoteCommandOptions());

		// Assert
		isRunning.Should().Be(true,
			because: "a JSON response must surface the server-reported running state");
		_applicationClient.Received(1).ExecutePostRequest(
			"http://local/ServiceModel/WorkspaceExplorerService.svc/IsODataBuildRunning",
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
		// because: the status probe must hit the same WorkspaceExplorerService.svc method RunODataBuild starts
	}

	[Test]
	[Description("Returns null when the server has no IsODataBuildRunning method and answers with an HTML error page, so a caller can tell 'unknown' apart from 'not running'.")]
	public void TryGetIsODataBuildRunning_ReturnsNull_WhenServerReturnsHtmlErrorPage() {
		// Arrange
		_serviceUrlBuilder.Build("ServiceModel/WorkspaceExplorerService.svc")
			.Returns("http://local/ServiceModel/WorkspaceExplorerService.svc");
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<int>())
			.Returns("<!DOCTYPE html><html><body>Server Error in '/' Application.</body></html>");

		// Act
		bool? isRunning = _client.TryGetIsODataBuildRunning(new RemoteCommandOptions());

		// Assert
		isRunning.Should().BeNull(
			because: "an HTML error page means the server has no such method, which must read as unknown rather than 'not running'");
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
