using Clio.Common.DataForge;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>
/// Unit tests for the pure <see cref="DataForgeReadinessGate.IsIndexReady"/> status→ready decision.
/// They construct <see cref="DataForgeStatusResponse"/> DTOs in-memory (no MCP server, no stand,
/// no network I/O), so they validate the similarity-index readiness contract locally and are
/// categorized <c>Unit</c> rather than <c>McpE2E.Sandbox</c>.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class DataForgeReadinessGateTests {
	private static DataForgeStatusResponse StatusResponse(
		bool success,
		string maintenanceStatus,
		bool maintenanceSuccess,
		bool dataStructureReadiness,
		bool lookupsReadiness) {
		DataForgeHealthResult health = new(
			Liveness: true,
			Readiness: true,
			DataStructureReadiness: dataStructureReadiness,
			LookupsReadiness: lookupsReadiness,
			CorrelationId: string.Empty);
		DataForgeMaintenanceStatusResult status = new(maintenanceSuccess, maintenanceStatus, null);
		return new DataForgeStatusResponse(success, "test", string.Empty, [], null, health, status);
	}

	[Test]
	[Description("Reports the index as ready when the status call succeeds, maintenance status is Ready, and both readiness health flags are true.")]
	public void IsIndexReady_ShouldReturnTrue_WhenStatusReadyAndBothReadinessFlagsTrue() {
		// Arrange
		DataForgeStatusResponse response = StatusResponse(
			success: true,
			maintenanceStatus: "Ready",
			maintenanceSuccess: true,
			dataStructureReadiness: true,
			lookupsReadiness: true);

		// Act
		bool result = DataForgeReadinessGate.IsIndexReady(response);

		// Assert
		result.Should().BeTrue(
			because: "a fully-built similarity index reports Ready with both data-structure and lookups readiness true");
	}

	[Test]
	[Description("Reports the index as not ready when the data-structure readiness flag is false even though maintenance status is Ready.")]
	public void IsIndexReady_ShouldReturnFalse_WhenDataStructureReadinessIsFalse() {
		// Arrange
		DataForgeStatusResponse response = StatusResponse(
			success: true,
			maintenanceStatus: "Ready",
			maintenanceSuccess: true,
			dataStructureReadiness: false,
			lookupsReadiness: true);

		// Act
		bool result = DataForgeReadinessGate.IsIndexReady(response);

		// Assert
		result.Should().BeFalse(
			because: "table and relation similarity reads cannot succeed until the data-structure index is built");
	}

	[Test]
	[Description("Reports the index as not ready when the lookups readiness flag is false even though maintenance status is Ready.")]
	public void IsIndexReady_ShouldReturnFalse_WhenLookupsReadinessIsFalse() {
		// Arrange
		DataForgeStatusResponse response = StatusResponse(
			success: true,
			maintenanceStatus: "Ready",
			maintenanceSuccess: true,
			dataStructureReadiness: true,
			lookupsReadiness: false);

		// Act
		bool result = DataForgeReadinessGate.IsIndexReady(response);

		// Assert
		result.Should().BeFalse(
			because: "lookup similarity reads cannot succeed until the lookups index is built");
	}

	[Test]
	[Description("Reports the index as not ready when the maintenance status is NotReady even though both readiness flags are true.")]
	public void IsIndexReady_ShouldReturnFalse_WhenMaintenanceStatusIsNotReady() {
		// Arrange
		DataForgeStatusResponse response = StatusResponse(
			success: true,
			maintenanceStatus: "NotReady",
			maintenanceSuccess: false,
			dataStructureReadiness: true,
			lookupsReadiness: true);

		// Act
		bool result = DataForgeReadinessGate.IsIndexReady(response);

		// Assert
		result.Should().BeFalse(
			because: "a NotReady maintenance status means the service has not finished warming up the index");
	}

	[Test]
	[Description("Reports the index as not ready when the structured status call itself failed.")]
	public void IsIndexReady_ShouldReturnFalse_WhenStatusResponseIsUnsuccessful() {
		// Arrange
		DataForgeStatusResponse response = StatusResponse(
			success: false,
			maintenanceStatus: "Ready",
			maintenanceSuccess: true,
			dataStructureReadiness: true,
			lookupsReadiness: true);

		// Act
		bool result = DataForgeReadinessGate.IsIndexReady(response);

		// Assert
		result.Should().BeFalse(
			because: "a failed status call cannot prove the similarity index is ready regardless of the embedded payload");
	}

	[Test]
	[Description("Reports the index as not ready when no status response payload was returned.")]
	public void IsIndexReady_ShouldReturnFalse_WhenResponseIsNull() {
		// Arrange
		DataForgeStatusResponse? response = null;

		// Act
		bool result = DataForgeReadinessGate.IsIndexReady(response);

		// Assert
		result.Should().BeFalse(
			because: "the absence of a status payload must be treated as not-ready rather than assuming readiness");
	}

	[Test]
	[Description("Treats the Ready maintenance status case-insensitively so a differently-cased service response still counts as ready.")]
	public void IsIndexReady_ShouldReturnTrue_WhenMaintenanceStatusIsReadyInDifferentCase() {
		// Arrange
		DataForgeStatusResponse response = StatusResponse(
			success: true,
			maintenanceStatus: "ready",
			maintenanceSuccess: true,
			dataStructureReadiness: true,
			lookupsReadiness: true);

		// Act
		bool result = DataForgeReadinessGate.IsIndexReady(response);

		// Assert
		result.Should().BeTrue(
			because: "the readiness decision should not be brittle to the casing of the service's status string");
	}

	[Test]
	[Description("Keeps polling when the elapsed wall-clock time is still strictly below the overall deadline.")]
	public void OverallDeadlineReached_ShouldReturnFalse_WhenElapsedIsBelowDeadline() {
		// Arrange
		TimeSpan elapsed = TimeSpan.FromMinutes(2);
		TimeSpan overallDeadline = TimeSpan.FromMinutes(6);

		// Act
		bool result = DataForgeReadinessGate.OverallDeadlineReached(elapsed, overallDeadline);

		// Assert
		result.Should().BeFalse(
			because: "the gate must keep polling while there is still budget left before its overall wall-clock deadline");
	}

	[Test]
	[Description("Stops polling when the elapsed wall-clock time has reached the overall deadline exactly.")]
	public void OverallDeadlineReached_ShouldReturnTrue_WhenElapsedEqualsDeadline() {
		// Arrange
		TimeSpan elapsed = TimeSpan.FromMinutes(6);
		TimeSpan overallDeadline = TimeSpan.FromMinutes(6);

		// Act
		bool result = DataForgeReadinessGate.OverallDeadlineReached(elapsed, overallDeadline);

		// Assert
		result.Should().BeTrue(
			because: "reaching the deadline exactly must bound the gate so the suite can never hang past it");
	}

	[Test]
	[Description("Stops polling when the elapsed wall-clock time has exceeded the overall deadline.")]
	public void OverallDeadlineReached_ShouldReturnTrue_WhenElapsedExceedsDeadline() {
		// Arrange
		TimeSpan elapsed = TimeSpan.FromMinutes(7);
		TimeSpan overallDeadline = TimeSpan.FromMinutes(6);

		// Act
		bool result = DataForgeReadinessGate.OverallDeadlineReached(elapsed, overallDeadline);

		// Assert
		result.Should().BeTrue(
			because: "once the deadline is passed the gate must fail the DataForge tests gracefully rather than continue polling");
	}

	private static DataForgeMaintenanceResponse MaintenanceResponse(bool success) =>
		new(success, "test", string.Empty, [], null, new DataForgeMaintenanceStatusResult(success, "Scheduled", null));

	[Test]
	[Description("Reports initialization as accepted when dataforge-initialize returned a successful structured maintenance payload, so the readiness poll may run.")]
	public void WasInitializationAccepted_ShouldReturnTrue_WhenInitializeSucceeded() {
		// Arrange
		DataForgeMaintenanceResponse response = MaintenanceResponse(success: true);

		// Act
		bool result = DataForgeReadinessGate.WasInitializationAccepted(response);

		// Assert
		result.Should().BeTrue(
			because: "an accepted (Success=true) initialize schedules maintenance work, so the gate should proceed to poll for readiness");
	}

	[Test]
	[Description("Reports initialization as not accepted when dataforge-initialize returned a structured Success=false payload, so the gate should not poll and the per-read guard decides skip-vs-fail.")]
	public void WasInitializationAccepted_ShouldReturnFalse_WhenInitializeReportedFailure() {
		// Arrange
		DataForgeMaintenanceResponse response = MaintenanceResponse(success: false);

		// Act
		bool result = DataForgeReadinessGate.WasInitializationAccepted(response);

		// Assert
		result.Should().BeFalse(
			because: "a Success=false initialize means the stand is not wired to a DataForge tier, an environment precondition rather than an accepted warm-up");
	}

	[Test]
	[Description("Reports initialization as not accepted when no dataforge-initialize payload was returned (unreadable or timed-out call).")]
	public void WasInitializationAccepted_ShouldReturnFalse_WhenResponseIsNull() {
		// Arrange
		DataForgeMaintenanceResponse? response = null;

		// Act
		bool result = DataForgeReadinessGate.WasInitializationAccepted(response);

		// Assert
		result.Should().BeFalse(
			because: "the absence of an initialize payload must be treated as not-accepted rather than assuming the warm-up started");
	}
}
