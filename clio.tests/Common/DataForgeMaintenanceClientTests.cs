using Clio.Common;
using Clio.Common.DataForge;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
public sealed class DataForgeMaintenanceClientTests {
	private const string StatusRoute = "rest/DataForgeMaintenanceService/GetServiceStatus";
	private const string StatusUrl = "http://localhost/WebApp780/0/rest/DataForgeMaintenanceService/GetServiceStatus";

	[Test]
	[Category("Unit")]
	[Description("GetStatus should call the REST-based DataForge maintenance route with a bounded timeout and deserialize a ready wrapped status payload.")]
	public void GetStatus_Should_Use_Rest_Route_Timeout_And_Parse_Response() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IDataForgePlatformVersionGuard versionGuard = Substitute.For<IDataForgePlatformVersionGuard>();
		serviceUrlBuilder.Build(StatusRoute).Returns(StatusUrl);
		applicationClient.ExecutePostRequest(StatusUrl, "{}", DataForgeMaintenanceClient.RequestTimeoutMs, 1, 1)
			.Returns("""{"GetServiceStatusResult":{"IsOnline":true,"Liveness":{"HttpStatusCode":200,"Message":"Healthy"},"Readiness":{"HttpStatusCode":200,"Message":"Healthy"}}}""");
		DataForgeMaintenanceClient client = new(applicationClient, serviceUrlBuilder, versionGuard);

		// Act
		DataForgeMaintenanceStatusResult result = client.GetStatus();

		// Assert
		result.Success.Should().BeTrue(because: "a live and ready DataForge service should map to success");
		result.Status.Should().Be("Ready", because: "HTTP 200 readiness should map to the Ready status");
		result.Error.Should().BeNull(because: "ready service responses should not expose an error message");
		versionGuard.Received(1).EnsureSupported();
		serviceUrlBuilder.Received(1).Build(StatusRoute);
		applicationClient.Received(1).ExecutePostRequest(StatusUrl, "{}", DataForgeMaintenanceClient.RequestTimeoutMs, 1, 1);
	}

	[Test]
	[Category("Unit")]
	[Description("GetFullStatus should map one service status response into both health and maintenance status payloads.")]
	public void GetFullStatus_Should_Map_Health_And_Status_From_Single_Response() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IDataForgePlatformVersionGuard versionGuard = Substitute.For<IDataForgePlatformVersionGuard>();
		serviceUrlBuilder.Build(StatusRoute).Returns(StatusUrl);
		applicationClient.ExecutePostRequest(StatusUrl, "{}", DataForgeMaintenanceClient.RequestTimeoutMs, 1, 1)
			.Returns("""{"GetServiceStatusResult":{"IsOnline":true,"Readiness":{"HttpStatusCode":200,"Message":"Ready"},"DataStructureReadiness":"ready","LookupsReadinessInfo":"ready"}}""");
		DataForgeMaintenanceClient client = new(applicationClient, serviceUrlBuilder, versionGuard);

		// Act
		(DataForgeHealthResult health, DataForgeMaintenanceStatusResult status) = client.GetFullStatus();

		// Assert
		health.Liveness.Should().BeTrue(because: "online service status should set liveness");
		health.Readiness.Should().BeTrue(because: "HTTP 200 readiness should set readiness");
		health.DataStructureReadiness.Should().BeTrue(because: "non-error data-structure readiness text should be accepted");
		health.LookupsReadiness.Should().BeTrue(because: "non-error lookup readiness text should be accepted");
		status.Success.Should().BeTrue(because: "ready health should map to a successful maintenance status");
		status.Status.Should().Be("Ready", because: "ready health should map to the Ready status");
		applicationClient.Received(1).ExecutePostRequest(StatusUrl, "{}", DataForgeMaintenanceClient.RequestTimeoutMs, 1, 1);
	}

	[Test]
	[Category("Unit")]
	[Description("GetFullStatus should map offline service responses to offline health and status without throwing.")]
	public void GetFullStatus_Should_Map_Offline_Response() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IDataForgePlatformVersionGuard versionGuard = Substitute.For<IDataForgePlatformVersionGuard>();
		serviceUrlBuilder.Build(StatusRoute).Returns(StatusUrl);
		applicationClient.ExecutePostRequest(StatusUrl, "{}", DataForgeMaintenanceClient.RequestTimeoutMs, 1, 1)
			.Returns("""{"GetServiceStatusResult":{"IsOnline":false,"Liveness":{"HttpStatusCode":503,"Message":"Unavailable"},"Readiness":{"HttpStatusCode":503,"Message":"Not ready"}}}""");
		DataForgeMaintenanceClient client = new(applicationClient, serviceUrlBuilder, versionGuard);

		// Act
		(DataForgeHealthResult health, DataForgeMaintenanceStatusResult status) = client.GetFullStatus();

		// Assert
		health.Liveness.Should().BeFalse(because: "offline service status should clear liveness");
		health.Readiness.Should().BeFalse(because: "offline service status should clear readiness");
		status.Success.Should().BeFalse(because: "offline service status should not be successful");
		status.Status.Should().Be("Offline", because: "offline service status should map to the Offline status");
		status.Error.Should().Be("Unavailable", because: "liveness details should be preserved as the maintenance error");
	}

	[Test]
	[Category("Unit")]
	[Description("GetHealthDetails should mark readiness subcomponents false when their readiness messages contain errors.")]
	public void GetHealthDetails_Should_Map_Component_Readiness_Errors() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IDataForgePlatformVersionGuard versionGuard = Substitute.For<IDataForgePlatformVersionGuard>();
		serviceUrlBuilder.Build(StatusRoute).Returns(StatusUrl);
		applicationClient.ExecutePostRequest(StatusUrl, "{}", DataForgeMaintenanceClient.RequestTimeoutMs, 1, 1)
			.Returns("""{"GetServiceStatusResult":{"IsOnline":true,"Readiness":{"HttpStatusCode":200},"DataStructureReadiness":"error: missing index","LookupsReadinessInfo":"ready"}}""");
		DataForgeMaintenanceClient client = new(applicationClient, serviceUrlBuilder, versionGuard);

		// Act
		DataForgeHealthResult result = client.GetHealthDetails();

		// Assert
		result.Liveness.Should().BeTrue(because: "online service status should set liveness");
		result.Readiness.Should().BeTrue(because: "HTTP 200 readiness should set readiness");
		result.DataStructureReadiness.Should().BeFalse(because: "error text in data-structure readiness should be reported as not ready");
		result.LookupsReadiness.Should().BeTrue(because: "non-error lookup readiness should remain ready");
	}

	[Test]
	[Category("Unit")]
	[Description("Initialize should verify the platform version and schedule maintenance work through the REST-based DataForge route.")]
	public void Initialize_Should_Use_Rest_Route() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IDataForgePlatformVersionGuard versionGuard = Substitute.For<IDataForgePlatformVersionGuard>();
		const string route = "rest/DataForgeMaintenanceService/InitializeDataStructuresAndLookups";
		const string url = "http://localhost/WebApp780/0/rest/DataForgeMaintenanceService/InitializeDataStructuresAndLookups";
		serviceUrlBuilder.Build(route).Returns(url);
		DataForgeMaintenanceClient client = new(applicationClient, serviceUrlBuilder, versionGuard);

		// Act
		DataForgeMaintenanceStatusResult result = client.Initialize();

		// Assert
		result.Success.Should().BeTrue(because: "accepted maintenance scheduling should produce a successful result");
		result.Status.Should().Be("Scheduled", because: "initialize only schedules work through the proxy service");
		versionGuard.Received(1).EnsureSupported();
		applicationClient.Received(1).ExecutePostRequest(url, "{}", DataForgeMaintenanceClient.RequestTimeoutMs, 1, 1);
	}

	[Test]
	[Category("Unit")]
	[Description("GetFullStatus should return Unavailable status rather than throw when the proxy returns an HTML error page (e.g. CrtDataForge is not installed).")]
	public void GetFullStatus_Should_Return_Unavailable_When_Proxy_Returns_Html() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IDataForgePlatformVersionGuard versionGuard = Substitute.For<IDataForgePlatformVersionGuard>();
		serviceUrlBuilder.Build(StatusRoute).Returns(StatusUrl);
		applicationClient.ExecutePostRequest(StatusUrl, "{}", DataForgeMaintenanceClient.RequestTimeoutMs, 1, 1)
			.Returns("<!DOCTYPE html><html><body>404 Not Found</body></html>");
		DataForgeMaintenanceClient client = new(applicationClient, serviceUrlBuilder, versionGuard);

		// Act
		(DataForgeHealthResult health, DataForgeMaintenanceStatusResult status) = client.GetFullStatus();

		// Assert
		health.Liveness.Should().BeFalse(
			because: "an HTML error response means the proxy endpoint does not exist and the service is not reachable");
		status.Success.Should().BeFalse(
			because: "an HTML error response should produce a failed status rather than a JSON parse exception");
		status.Status.Should().Be("Unavailable",
			because: "callers should receive a meaningful status when CrtDataForge is not installed on the target environment");
	}
}
