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
		response.Status.Should().Be("invalid-request");
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
		response.Status.Should().Be("not-found");
		response.EnvironmentName.Should().Be("sandbox");
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
		response.Success.Should().BeTrue();
		response.Status.Should().Be("running", because: "the operation has not finished yet");
		response.OperationId.Should().Be(begun.OperationId);
		response.PackageName.Should().Be("MyPackage");
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
		response.Status.Should().Be("succeeded");
		response.ExitCode.Should().Be(0);
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
		response.Status.Should().Be("not-found");
	}
}
