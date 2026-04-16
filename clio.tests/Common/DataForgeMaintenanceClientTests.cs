using Clio.Common;
using Clio.Common.DataForge;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
public sealed class DataForgeMaintenanceClientTests {
	[Test]
	[Category("Unit")]
	[Description("GetStatus should call the rest-based DataForge maintenance route and deserialize the wrapped service status payload.")]
	public void GetStatus_Should_Use_Rest_Route_And_Parse_Response() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build("rest/DataForgeMaintenanceService/GetServiceStatus")
			.Returns("http://localhost/WebApp780/0/rest/DataForgeMaintenanceService/GetServiceStatus");
		applicationClient.ExecutePostRequest(
			"http://localhost/WebApp780/0/rest/DataForgeMaintenanceService/GetServiceStatus",
			"{}")
			.Returns("""{"GetServiceStatusResult":{"IsOnline":true,"Liveness":{"HttpStatusCode":200,"Message":"Healthy"},"Readiness":{"HttpStatusCode":200,"Message":"Healthy"}}}""");
		DataForgeMaintenanceClient client = new(applicationClient, serviceUrlBuilder);

		// Act
		DataForgeMaintenanceStatusResult result = client.GetStatus();

		// Assert
		result.Success.Should().BeTrue();
		result.Status.Should().Be("Ready");
		result.Error.Should().BeNull();
		serviceUrlBuilder.Received(1).Build("rest/DataForgeMaintenanceService/GetServiceStatus");
	}

	[Test]
	[Category("Unit")]
	[Description("Initialize should schedule maintenance work through the rest-based DataForge route.")]
	public void Initialize_Should_Use_Rest_Route() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build("rest/DataForgeMaintenanceService/InitializeDataStructuresAndLookups")
			.Returns("http://localhost/WebApp780/0/rest/DataForgeMaintenanceService/InitializeDataStructuresAndLookups");
		DataForgeMaintenanceClient client = new(applicationClient, serviceUrlBuilder);

		// Act
		DataForgeMaintenanceStatusResult result = client.Initialize();

		// Assert
		result.Success.Should().BeTrue();
		result.Status.Should().Be("Scheduled");
		applicationClient.Received(1).ExecutePostRequest(
			"http://localhost/WebApp780/0/rest/DataForgeMaintenanceService/InitializeDataStructuresAndLookups",
			"{}");
	}
}
