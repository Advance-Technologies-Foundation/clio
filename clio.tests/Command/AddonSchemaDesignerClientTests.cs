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
	private AddonSchemaDesignerClient _client = null!;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build("ServiceModel/AddonSchemaDesignerService.svc")
			.Returns("http://local/ServiceModel/AddonSchemaDesignerService.svc");
		_serviceUrlBuilder.Build("/rest/WorkplaceService/ResetScriptCache")
			.Returns("http://local/rest/WorkplaceService/ResetScriptCache");
		_client = new AddonSchemaDesignerClient(_applicationClient, new JsonConverter(), _serviceUrlBuilder);
	}

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
		_client.ResetClientScriptCache();

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			"http://local/rest/WorkplaceService/ResetScriptCache",
			string.Empty,
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}
}
