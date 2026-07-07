using System;
using System.Linq;
using System.Text.Json;
using Clio.Command.IdentityServiceDeployment;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.IdentityServiceDeployment;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class IdentityServiceCreatioClientTests
{
	[Test]
	[Description("CreateClioClient sends the OAuthConfigService DTO fields required by Creatio: systemUserId and allowedGrantTypes.")]
	public void CreateClioClient_Should_Send_SystemUserId_And_AllowedGrantTypes()
	{
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.OAuthConfigCreateTechnicalUser).Returns("create-user");
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.OAuthConfigAddClient).Returns("add-client");
		Guid systemUserId = Guid.NewGuid();
		applicationClient.ExecutePostRequest("create-user", Arg.Any<string>())
			.Returns($$"""{"systemUserId":"{{systemUserId}}"}""");
		string createTechnicalUserPayload = string.Empty;
		applicationClient.ExecutePostRequest("create-user", Arg.Do<string>(payload => createTechnicalUserPayload = payload))
			.Returns($$"""{"systemUserId":"{{systemUserId}}"}""");
		string addClientPayload = string.Empty;
		applicationClient.ExecutePostRequest("add-client", Arg.Do<string>(payload => addClientPayload = payload))
			.Returns("""{"clientId":"client-id","clientSecret":"client-secret"}""");
		IdentityServiceCreatioClient client = new(applicationClient, serviceUrlBuilder);
		DeployIdentityOptions options = new() {
			ClientName = "clio cli",
			ClientApplicationUrl = "https://example.invalid/clio",
			ClientDescription = "integration for clio cli"
		};

		// Act
		string actualSystemUserId = client.CreateTechnicalUser("Supervisor");
		OAuthClientCredentials credentials = client.CreateClioClient(options, actualSystemUserId);

		// Assert
		actualSystemUserId.Should().Be(systemUserId.ToString(),
			because: "CreateClioClient should be bound to the technical user returned by Creatio");
		credentials.ClientId.Should().Be("client-id",
			because: "the client id returned by Creatio should be surfaced to the deploy service");
		using JsonDocument createUserDocument = JsonDocument.Parse(createTechnicalUserPayload);
		createUserDocument.RootElement.GetProperty("createTechnicalUserRequest").GetProperty("name").GetString()
			.Should().Be("Supervisor",
				because: "OAuthConfigService/CreateTechnicalUser uses WCF wrapped request body style");
		using JsonDocument document = JsonDocument.Parse(addClientPayload);
		JsonElement root = document.RootElement.GetProperty("addClientRequest");
		root.GetProperty("systemUserId").GetString().Should().Be(systemUserId.ToString(),
			because: "OAuthConfigService/AddClient expects a systemUserId GUID, not the user display name");
		root.GetProperty("grantType").GetString().Should().Be("client_credentials",
			because: "OAuthConfigService validates grantType against case-sensitive grant-type codes");
		root.GetProperty("allowedGrantTypes").EnumerateArray()
			.Select(item => item.GetString())
			.Should().BeEquivalentTo(["client_credentials"],
				because: "the client_credentials grant must be explicitly allowed for token issuance");
		root.TryGetProperty("systemUser", out _).Should().BeFalse(
			because: "the display-name field is not part of the AddClient DTO");
		root.TryGetProperty("grantTypes", out _).Should().BeFalse(
			because: "the DTO field is allowedGrantTypes");
		serviceUrlBuilder.Received(1).Build(ServiceUrlBuilder.KnownRoute.OAuthConfigCreateTechnicalUser);
		serviceUrlBuilder.Received(1).Build(ServiceUrlBuilder.KnownRoute.OAuthConfigAddClient);
	}
}
