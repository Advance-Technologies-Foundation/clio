using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class RestartStatusToolTests {

	private static IToolCommandResolver CreateResolver(string tenantKey) {
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns(tenantKey);
		return resolver;
	}

	[Test]
	[Description("Rejects an empty environment-name before touching the registry.")]
	public void GetStatus_Should_ReturnInvalidRequest_WhenEnvironmentNameEmpty() {
		// Arrange
		RestartOperationRegistry registry = new();
		RestartStatusTool tool = new(registry, CreateResolver("tenant-a"));

		// Act
		RestartStatusResponse response = tool.GetStatus(new RestartStatusArgs("  ", null));

		// Assert
		response.Success.Should().BeFalse(because: "an empty environment-name is not a valid request");
		response.Status.Should().Be("invalid-request", because: "an empty environment-name maps to the invalid-request status");
		response.Note.Should().Contain("environment-name", because: "the note must explain what was missing");
	}

	[Test]
	[Description("Reports not-found (not an error) when no restart operation has ever run for the environment.")]
	public void GetStatus_Should_ReturnNotFound_WhenNoOperationTrackedForEnvironment() {
		// Arrange
		RestartOperationRegistry registry = new();
		RestartStatusTool tool = new(registry, CreateResolver("tenant-a"));

		// Act
		RestartStatusResponse response = tool.GetStatus(new RestartStatusArgs("sandbox", null));

		// Assert
		response.Success.Should().BeTrue(because: "an empty history is a legitimate state, not a tool error");
		response.Status.Should().Be("not-found", because: "no operation was ever tracked for this environment");
		response.EnvironmentName.Should().Be("sandbox", because: "the response must echo the queried environment name");
	}

	[Test]
	[Description("Returns the latest tracked operation for the environment (still running) when operation-id is omitted.")]
	public void GetStatus_Should_ReturnLatestRunningOperation_WhenOperationIdOmitted() {
		// Arrange
		RestartOperationRegistry registry = new();
		RestartOperationRecord begun = registry.Begin("tenant-a", "sandbox");
		RestartStatusTool tool = new(registry, CreateResolver("tenant-a"));

		// Act
		RestartStatusResponse response = tool.GetStatus(new RestartStatusArgs("sandbox", null));

		// Assert
		response.Success.Should().BeTrue(because: "returning a tracked operation is a successful lookup");
		response.Status.Should().Be("running", because: "the readiness wait has not finished yet");
		response.OperationId.Should().Be(begun.OperationId, because: "the latest operation for the tenant must be returned when no id is supplied");
		response.ExitCode.Should().BeNull(because: "a running operation has no exit code yet");
	}

	[Test]
	[Description("Surfaces a ready (exit 0) terminal status for a finished readiness wait.")]
	public void GetStatus_Should_ReturnReady_WhenReadinessSucceeded() {
		// Arrange
		RestartOperationRegistry registry = new();
		RestartOperationRecord begun = registry.Begin("tenant-a", "sandbox");
		registry.Finish(begun.OperationId, 0);
		RestartStatusTool tool = new(registry, CreateResolver("tenant-a"));

		// Act
		RestartStatusResponse response = tool.GetStatus(new RestartStatusArgs("sandbox", null));

		// Assert
		response.Status.Should().Be("ready", because: "the instance answered its health-check with exit code 0");
		response.ExitCode.Should().Be(0, because: "a ready readiness wait finished with exit code 0");
	}

	[Test]
	[Description("Surfaces a timedout terminal status for a readiness wait that never became ready.")]
	public void GetStatus_Should_ReturnTimedOut_WhenReadinessTimedOut() {
		// Arrange
		RestartOperationRegistry registry = new();
		RestartOperationRecord begun = registry.Begin("tenant-a", "sandbox");
		registry.Finish(begun.OperationId, 1);
		RestartStatusTool tool = new(registry, CreateResolver("tenant-a"));

		// Act
		RestartStatusResponse response = tool.GetStatus(new RestartStatusArgs("sandbox", null));

		// Assert
		response.Status.Should().Be("timedout", because: "the readiness wait ended without the instance answering its health-check");
		response.ExitCode.Should().Be(1, because: "a timed-out readiness wait finished with a non-zero exit code");
	}

	[Test]
	[Description("Returns the finished status for a specific operation-id, regardless of what is currently latest for the tenant.")]
	public void GetStatus_Should_ReturnById_WhenOperationIdProvided() {
		// Arrange
		RestartOperationRegistry registry = new();
		RestartOperationRecord older = registry.Begin("tenant-a", "sandbox");
		registry.Finish(older.OperationId, 0);
		registry.Begin("tenant-a", "sandbox"); // becomes the new latest, but we query the OLDER one explicitly
		RestartStatusTool tool = new(registry, CreateResolver("tenant-a"));

		// Act
		RestartStatusResponse response = tool.GetStatus(new RestartStatusArgs("sandbox", older.OperationId));

		// Assert
		response.OperationId.Should().Be(older.OperationId,
			because: "an explicit operation-id must be looked up directly, not resolved to the tenant's latest");
		response.Status.Should().Be("ready", because: "the explicitly queried operation had finished ready with exit code 0");
		response.ExitCode.Should().Be(0, because: "the finished operation's exit code must be surfaced");
	}

	[Test]
	[Description("Refuses to expose another tenant's operation record when its global operation-id is supplied by a different caller.")]
	public void GetStatus_Should_ReturnNotFound_WhenOperationIdBelongsToAnotherTenant() {
		// Arrange
		RestartOperationRegistry registry = new();
		RestartOperationRecord othersOperation = registry.Begin("tenant-a", "victim-env");
		registry.Finish(othersOperation.OperationId, 0);
		// The caller resolves to a DIFFERENT tenant but knows/guesses tenant-a's global operation-id.
		RestartStatusTool tool = new(registry, CreateResolver("tenant-b"));

		// Act
		RestartStatusResponse response = tool.GetStatus(new RestartStatusArgs("attacker-env", othersOperation.OperationId));

		// Assert
		response.Status.Should().Be("not-found",
			because: "on a shared MCP server a caller must not read another tenant's operation via a leaked/guessed operation-id");
		response.EnvironmentName.Should().NotBe("victim-env", because: "the other tenant's environment name must not leak");
	}

	[Test]
	[Description("Reports not-found for an operation-id that does not exist in the registry.")]
	public void GetStatus_Should_ReturnNotFound_WhenOperationIdUnknown() {
		// Arrange
		RestartOperationRegistry registry = new();
		RestartStatusTool tool = new(registry, CreateResolver("tenant-a"));

		// Act
		RestartStatusResponse response = tool.GetStatus(new RestartStatusArgs("sandbox", "no-such-id"));

		// Assert
		response.Success.Should().BeTrue(because: "an unknown operation-id is a legitimate not-found result, not a tool error");
		response.Status.Should().Be("not-found", because: "an operation-id absent from the registry maps to the not-found status");
	}
}
