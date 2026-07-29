using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class CompileStatusToolTests {

	private static IToolCommandResolver CreateResolver(string tenantKey) {
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns(tenantKey);
		return resolver;
	}

	[Test]
	[Description("Rejects an empty environment-name before touching the registry.")]
	public void GetStatus_Should_ReturnInvalidRequest_WhenEnvironmentNameEmpty() {
		// Arrange
		CompileOperationRegistry registry = new();
		CompileStatusTool tool = new(registry, CreateResolver("tenant-a"));

		// Act
		CompileStatusResponse response = tool.GetStatus(new CompileStatusArgs("  ", null));

		// Assert
		response.Success.Should().BeFalse(because: "an empty environment-name is not a valid request");
		response.Status.Should().Be("invalid-request", because: "an empty environment-name maps to the invalid-request status");
		response.Note.Should().Contain("environment-name", because: "the note must explain what was missing");
	}

	[Test]
	[Description("Reports not-found (not an error) when no compile-creatio operation has ever run for the environment.")]
	public void GetStatus_Should_ReturnNotFound_WhenNoOperationTrackedForEnvironment() {
		// Arrange
		CompileOperationRegistry registry = new();
		CompileStatusTool tool = new(registry, CreateResolver("tenant-a"));

		// Act
		CompileStatusResponse response = tool.GetStatus(new CompileStatusArgs("sandbox", null));

		// Assert
		response.Success.Should().BeTrue(because: "an empty history is a legitimate state, not a tool error");
		response.Status.Should().Be("not-found", because: "no operation was ever tracked for this environment");
		response.EnvironmentName.Should().Be("sandbox", because: "the response must echo the queried environment name");
	}

	[Test]
	[Description("Returns the latest tracked operation for the environment when operation-id is omitted.")]
	public void GetStatus_Should_ReturnLatestOperation_WhenOperationIdOmitted() {
		// Arrange
		CompileOperationRegistry registry = new();
		CompileOperationRecord begun = registry.Begin("tenant-a", "sandbox", "MyPackage");
		IToolCommandResolver resolver = CreateResolver("tenant-a");
		CompileStatusTool tool = new(registry, resolver);

		// Act
		CompileStatusResponse response = tool.GetStatus(new CompileStatusArgs("sandbox", null));

		// Assert
		response.Success.Should().BeTrue(because: "returning a tracked operation is a successful lookup");
		response.Status.Should().Be("running", because: "the operation has not finished yet");
		response.OperationId.Should().Be(begun.OperationId, because: "the latest operation for the tenant must be returned when no id is supplied");
		response.PackageName.Should().Be("MyPackage", because: "the response must surface the package recorded for the tracked operation");
	}

	[Test]
	[Description("Returns the finished status and exit code for a specific operation-id, regardless of what is currently latest for the tenant.")]
	public void GetStatus_Should_ReturnById_WhenOperationIdProvided() {
		// Arrange
		CompileOperationRegistry registry = new();
		CompileOperationRecord older = registry.Begin("tenant-a", "sandbox", null);
		registry.Finish(older.OperationId, 0, []);
		registry.Begin("tenant-a", "sandbox", null); // becomes the new latest, but we query the OLDER one explicitly
		CompileStatusTool tool = new(registry, CreateResolver("tenant-a"));

		// Act
		CompileStatusResponse response = tool.GetStatus(new CompileStatusArgs("sandbox", older.OperationId));

		// Assert
		response.OperationId.Should().Be(older.OperationId,
			because: "an explicit operation-id must be looked up directly, not resolved to the tenant's latest");
		response.Status.Should().Be("succeeded", because: "the explicitly queried operation had finished with exit code 0");
		response.ExitCode.Should().Be(0, because: "the finished operation's exit code must be surfaced");
	}

	[Test]
	[Description("Refuses to expose another tenant's operation record when its global operation-id is supplied by a different caller.")]
	public void GetStatus_Should_ReturnNotFound_WhenOperationIdBelongsToAnotherTenant() {
		// Arrange
		CompileOperationRegistry registry = new();
		CompileOperationRecord othersOperation = registry.Begin("tenant-a", "victim-env", "SecretPackage");
		registry.Finish(othersOperation.OperationId, 0, []);
		// The caller resolves to a DIFFERENT tenant but knows/guesses tenant-a's global operation-id.
		CompileStatusTool tool = new(registry, CreateResolver("tenant-b"));

		// Act
		CompileStatusResponse response = tool.GetStatus(new CompileStatusArgs("attacker-env", othersOperation.OperationId));

		// Assert
		response.Status.Should().Be("not-found",
			because: "on a shared MCP server a caller must not read another tenant's operation via a leaked/guessed operation-id");
		response.PackageName.Should().BeNull(because: "the other tenant's package name must not leak");
		response.EnvironmentName.Should().NotBe("victim-env", because: "the other tenant's environment name must not leak");
	}

	[Test]
	[Description("Reports not-found for an operation-id that does not exist in the registry.")]
	public void GetStatus_Should_ReturnNotFound_WhenOperationIdUnknown() {
		// Arrange
		CompileOperationRegistry registry = new();
		CompileStatusTool tool = new(registry, CreateResolver("tenant-a"));

		// Act
		CompileStatusResponse response = tool.GetStatus(new CompileStatusArgs("sandbox", "no-such-id"));

		// Assert
		response.Success.Should().BeTrue(because: "an unknown operation-id is a legitimate not-found result, not a tool error");
		response.Status.Should().Be("not-found", because: "an operation-id absent from the registry maps to the not-found status");
	}
}
