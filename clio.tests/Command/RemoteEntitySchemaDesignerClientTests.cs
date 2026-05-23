using System;
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
}
