using System;
using Clio.Command.AddonSchemaDesigner;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class AddonSchemaDesignerClientTests {
	private IApplicationClient _applicationClient = null!;
	private IServiceUrlBuilder _serviceUrlBuilder = null!;
	private ILogger _logger = null!;
	private AddonSchemaDesignerClient _client = null!;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build("ServiceModel/AddonSchemaDesignerService.svc")
			.Returns("http://local/ServiceModel/AddonSchemaDesignerService.svc");
		_serviceUrlBuilder.Build("/rest/WorkplaceService/ResetScriptCache")
			.Returns("http://local/rest/WorkplaceService/ResetScriptCache");
		_serviceUrlBuilder.Build("ServiceModel/WorkspaceExplorerService.svc/BuildConfiguration")
			.Returns("http://local/ServiceModel/WorkspaceExplorerService.svc/BuildConfiguration");
		_logger = Substitute.For<ILogger>();
		_client = new AddonSchemaDesignerClient(_applicationClient, new JsonConverter(), _serviceUrlBuilder, _logger);
	}

	private void StubResponse(string json) =>
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(json);

	private static AddonGetRequestDto SampleGetRequest() =>
		new() {
			AddonName = "RelatedPage",
			TargetSchemaUId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
			TargetParentSchemaUId = Guid.Empty,
			TargetPackageUId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
			TargetSchemaManagerName = "EntitySchemaManager",
			UseFullHierarchy = true
		};

	[Test]
	[Description("Deserializes raw add-on designer responses directly when the service returns valid JSON.")]
	public void GetSchema_DeserializesRawJsonResponse() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("{\"success\":true,\"schema\":{\"metaData\":\"{}\",\"resources\":[]}}");

		// Act
		AddonSchemaDto response = _client.GetSchema(new AddonGetRequestDto {
			AddonName = "BusinessRule",
			TargetSchemaUId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
			TargetParentSchemaUId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
			TargetPackageUId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
			TargetSchemaManagerName = "EntitySchemaManager",
			UseFullHierarchy = true
		});

		// Assert
		response.Should().NotBeNull(because: "a valid add-on payload should deserialize without correction");
		response.Resources.Should().BeEmpty(because: "the resource list should be preserved from the response");
	}

	[Test]
	[Description("Falls back to corrected JSON when the add-on designer response body is string-escaped.")]
	public void GetSchema_FallsBackToCorrectedJson_WhenRawResponseIsEscaped() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("\"{\\\"success\\\":true,\\\"schema\\\":{\\\"metaData\\\":\\\"{}\\\",\\\"resources\\\":[]}}\"");

		// Act
		AddonSchemaDto response = _client.GetSchema(new AddonGetRequestDto {
			AddonName = "BusinessRule",
			TargetSchemaUId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
			TargetParentSchemaUId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
			TargetPackageUId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
			TargetSchemaManagerName = "EntitySchemaManager",
			UseFullHierarchy = true
		});

		// Assert
		response.Should().NotBeNull(because: "legacy escaped payloads should still be supported");
		response.MetaData.Should().Be("{}", because: "fallback correction should preserve the schema body");
	}

	[Test]
	[Description("Posts the serialized add-on schema payload to SaveSchema and accepts successful responses.")]
	public void SaveSchema_PostsSerializedSchemaPayload() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("{\"success\":true,\"value\":true}");
		AddonSchemaDto schema = new() {
			MetaData = "{\"rules\":[]}",
			Resources = []
		};

		// Act
		Action act = () => _client.SaveSchema(schema);

		// Assert
		act.Should().NotThrow(because: "successful add-on saves should complete without extra wrapping exceptions");
		_applicationClient.Received(1).ExecutePostRequest(
			"http://local/ServiceModel/AddonSchemaDesignerService.svc/SaveSchema",
			Arg.Is<string>(body => body.Contains("\"metaData\"") && body.Contains("rules")),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Description("Resets the client script cache after schema changes through the standard workplace endpoint.")]
	public void ResetClientScriptCache_PostsEmptyBody() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("{\"success\":true}");

		// Act
		string warning = _client.ResetClientScriptCache();

		// Assert
		warning.Should().BeNull(because: "a successful cache reset returns no warning");
		_applicationClient.Received(1).ExecutePostRequest(
			"http://local/rest/WorkplaceService/ResetScriptCache",
			string.Empty,
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Description("ResetClientScriptCache is best-effort: it runs AFTER the schema is already saved, so a POST failure is logged as a warning and swallowed (not thrown) — a transient cache-reset failure must not fail an already-committed operation.")]
	public void ResetClientScriptCache_ShouldWarnNotThrow_WhenPostFails() {
		// Arrange — the reset POST fails (e.g. a transient / expired-session error after the save committed).
		_applicationClient.ExecutePostRequest("http://local/rest/WorkplaceService/ResetScriptCache",
				string.Empty, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => throw new InvalidOperationException("reset boom"));

		// Act
		Action act = () => _client.ResetClientScriptCache();

		// Assert
		act.Should().NotThrow(
			because: "a post-save cache-reset failure must not fail an already-committed operation");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message => message.Contains("reset boom")));
	}

	[Test]
	[Description("Triggers the static-content rebuild and completes when the server reports a successful build.")]
	public void BuildConfiguration_PostsAndAcceptsSuccessfulRebuild() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("{\"errorInfo\":null,\"success\":true}");

		// Act
		string warning = null;
		Action act = () => warning = _client.BuildConfiguration();

		// Assert
		act.Should().NotThrow(because: "a successful rebuild response should complete without throwing");
		warning.Should().BeNull(because: "a successful rebuild returns no warning");
		_applicationClient.Received(1).ExecutePostRequest(
			"http://local/ServiceModel/WorkspaceExplorerService.svc/BuildConfiguration",
			string.Empty,
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Description("Warns (does not throw) on an explicit rebuild failure: BuildConfiguration runs AFTER the schema is already saved, so throwing would falsely report an already-committed operation as failed — the failure is logged as a warning instead.")]
	public void BuildConfiguration_ShouldWarnNotThrow_WhenRebuildReportsFailure() {
		// Arrange
		StubResponse("{\"success\":false,\"errorInfo\":{\"message\":\"static build failed\"}}");

		// Act
		string warning = null;
		Action act = () => warning = _client.BuildConfiguration();

		// Assert
		act.Should().NotThrow(
			because: "the schema is already saved when the rebuild runs, so a post-save rebuild failure must not fail the operation");
		warning.Should().Contain("static build failed",
			because: "an explicit rebuild failure is RETURNED (not only logged) so Create can surface it in result.Warning");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message => message.Contains("static build failed")));
	}

	[Test]
	[Description("Tolerates an empty rebuild response: BuildConfiguration is a shared fire-and-forget refresh (the business-rule path calls it after saving), so an empty body is not a failure and must not throw.")]
	public void BuildConfiguration_ShouldNotThrow_WhenResponseIsEmpty() {
		// Arrange
		StubResponse(string.Empty);

		// Act
		Action act = () => _client.BuildConfiguration();

		// Assert
		act.Should().NotThrow(
			because: "an empty rebuild body carries no explicit failure signal; throwing would regress the shared business-rule creation path");
	}

	[Test]
	[Description("Tolerates a rebuild response with no success flag: only an explicit success:false is a failure, so a body that omits success must not throw.")]
	public void BuildConfiguration_ShouldNotThrow_WhenSuccessFlagIsAbsent() {
		// Arrange
		StubResponse("{\"errorInfo\":null}");

		// Act
		Action act = () => _client.BuildConfiguration();

		// Assert
		act.Should().NotThrow(
			because: "a missing success flag is non-committal, not an explicit failure — the business-rule path relies on this tolerance");
	}

	[Test]
	[Description("Tolerates a non-JSON rebuild response (e.g. an HTML/redirect page): it carries no explicit failure signal, so BuildConfiguration must not throw.")]
	public void BuildConfiguration_ShouldNotThrow_WhenResponseIsNonJson() {
		// Arrange
		StubResponse("<!DOCTYPE html><html><body>redirect</body></html>");

		// Act
		Action act = () => _client.BuildConfiguration();

		// Assert
		act.Should().NotThrow(
			because: "a non-JSON body is a non-committal response, not an explicit success:false — throwing would regress business-rule creation");
	}

	[Test]
	[Description("BuildConfiguration is best-effort like ResetClientScriptCache: it runs AFTER the schema is already saved, so a THROWN rebuild POST (e.g. an expired-session redirect between the save and the rebuild) is caught, logged as a warning, and RETURNED — never propagated — so an already-committed operation is not reported as failed.")]
	public void BuildConfiguration_ShouldWarnNotThrowAndReturnWarning_WhenPostThrows() {
		// Arrange — the rebuild POST itself throws (a transport / expired-session failure after the save committed).
		_applicationClient.ExecutePostRequest("http://local/ServiceModel/WorkspaceExplorerService.svc/BuildConfiguration",
				string.Empty, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(_ => throw new InvalidOperationException("rebuild boom"));

		// Act
		string warning = null;
		Action act = () => warning = _client.BuildConfiguration();

		// Assert
		act.Should().NotThrow(
			because: "a thrown post-save rebuild POST must not fail an already-committed operation (symmetric with ResetClientScriptCache)");
		warning.Should().Contain("rebuild boom",
			because: "the caught rebuild failure is returned so the caller can surface it, not silently swallowed");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message => message.Contains("rebuild boom")));
	}

	[Test]
	[Description("Surfaces the server's rejection message when SaveSchema reports success:false.")]
	public void SaveSchema_ShouldThrowWithServerMessage_WhenSuccessIsFalse() {
		// Arrange
		StubResponse("{\"success\":false,\"errorInfo\":{\"message\":\"save rejected\"}}");

		// Act
		Action act = () => _client.SaveSchema(new AddonSchemaDto { MetaData = "{}", Resources = [] });

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*save rejected*",
			because: "a rejected save must surface the server message rather than silently succeeding");
	}

	[Test]
	[Description("Rejects a SaveSchema response whose value flag is explicitly false, even when success is true.")]
	public void SaveSchema_ShouldThrow_WhenValueIsFalse() {
		// Arrange
		StubResponse("{\"success\":true,\"value\":false}");

		// Act
		Action act = () => _client.SaveSchema(new AddonSchemaDto { MetaData = "{}", Resources = [] });

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "value:false is an explicit save-failure signal even when success is true");
	}

	[Test]
	[Description("Surfaces the server's message when GetSchema reports success:false.")]
	public void GetSchema_ShouldThrowWithServerMessage_WhenSuccessIsFalse() {
		// Arrange
		StubResponse("{\"success\":false,\"errorInfo\":{\"message\":\"get rejected\"}}");

		// Act
		Action act = () => _client.GetSchema(SampleGetRequest());

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*get rejected*",
			because: "a failed GetSchema must surface the server message");
	}

	[Test]
	[Description("Throws when GetSchema succeeds but carries no schema payload.")]
	public void GetSchema_ShouldThrow_WhenSchemaPayloadIsMissing() {
		// Arrange
		StubResponse("{\"success\":true}");

		// Act
		Action act = () => _client.GetSchema(SampleGetRequest());

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*schema payload*",
			because: "a success response with no schema is a contract violation, not an empty add-on");
	}

	[Test]
	[Description("Throws a clear empty-response error when the designer service returns an empty body.")]
	public void GetSchema_ShouldThrow_WhenResponseBodyIsEmpty() {
		// Arrange
		StubResponse(string.Empty);

		// Act
		Action act = () => _client.GetSchema(SampleGetRequest());

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*empty response*",
			because: "an empty body must be surfaced as a clear error rather than a null dereference");
	}
}
