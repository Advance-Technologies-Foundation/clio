using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit coverage for the <c>create-package</c> MCP tool: its safety flags, argument mapping onto the
/// environment-scoped command, argument validation, and redaction of failure text.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public class CreatePackageToolTests {

	private const string EnvironmentName = "dev";

	[Test]
	[Description("Publishes create-package as a destructive, non-idempotent write so the host confirms it and a retry is not assumed safe.")]
	public void CreatePackageTool_ShouldDeclareDestructiveNonIdempotentFlags() {
		// Arrange & Act
		McpServerToolAttribute attribute = typeof(CreatePackageTool)
			.GetMethod(nameof(CreatePackageTool.CreatePackage))!
			.GetCustomAttribute<McpServerToolAttribute>()!;

		// Assert
		attribute.Name.Should().Be("create-package", because: "the tool name matches the CLI verb");
		attribute.ReadOnly.Should().BeFalse(because: "the tool writes a package into the environment");
		attribute.Destructive.Should().BeTrue(because: "it changes a shared environment, like create-app");
		attribute.Idempotent.Should().BeFalse(because: "a second call with the same name is refused as a duplicate");
	}

	[Test]
	[Description("Resolves the environment-scoped command and forwards every argument to it.")]
	public void CreatePackage_ShouldForwardArgumentsToTheResolvedCommand() {
		// Arrange
		FakeCreatePackageCommand resolvedCommand = new(new CreatePackageResponse {
			Success = true, PackageCreated = true, PackageName = "UsrCalls"
		});
		CreatePackageTool tool = CreateTool(resolvedCommand);

		// Act
		CreatePackageResponse response = tool.CreatePackage(new CreatePackageArgs(EnvironmentName, "Calls", "desc",
			["CrtBase"], "UsrApp"));

		// Assert
		response.PackageName.Should().Be("UsrCalls", because: "the command's readback is returned as is");
		CreatePackageOptions options = resolvedCommand.CapturedOptions;
		options.Environment.Should().Be(EnvironmentName, because: "the package is created in the addressed environment");
		options.PackageName.Should().Be("Calls", because: "the name is forwarded unchanged; prefixing is the creator's job");
		options.Description.Should().Be("desc", because: "the description is forwarded");
		options.Dependencies.Should().Equal(["CrtBase"], because: "the dependencies are forwarded");
		options.ApplicationCode.Should().Be("UsrApp", because: "the application code is forwarded");
	}

	[Test]
	[Description("Fails with a structured error naming package-name when it is missing, before any environment is addressed.")]
	public void CreatePackage_ShouldFail_WhenPackageNameIsMissing() {
		// Arrange
		FakeCreatePackageCommand resolvedCommand = new(new CreatePackageResponse { Success = true });
		CreatePackageTool tool = CreateTool(resolvedCommand);

		// Act
		CreatePackageResponse response = tool.CreatePackage(new CreatePackageArgs(EnvironmentName));

		// Assert
		response.Success.Should().BeFalse(because: "there is no package to create");
		response.PackageCreated.Should().BeFalse(because: "nothing was sent to the environment");
		response.Error.Should().Contain("package-name", because: "the message names the missing argument");
		resolvedCommand.CapturedOptions.Should().BeNull(because: "the command must not run without a name");
	}

	[Test]
	[TestCase("packageName", "package-name")]
	[TestCase("applicationCode", "application-code")]
	[Description("Rejects a legacy argument spelling with a rename hint instead of dropping it, even when every required argument is present.")]
	public void CreatePackage_ShouldRejectALegacyAlias_WithARenameHint(string legacyName, string canonicalName) {
		// Arrange
		FakeCreatePackageCommand resolvedCommand = new(new CreatePackageResponse { Success = true });
		CreatePackageTool tool = CreateTool(resolvedCommand);
		CreatePackageArgs args = new(EnvironmentName, "Calls") {
			ExtensionData = new() { [legacyName] = JsonDocument.Parse("\"x\"").RootElement }
		};

		// Act
		CreatePackageResponse response = tool.CreatePackage(args);

		// Assert
		response.Success.Should().BeFalse(because: "the misspelled argument must not be silently ignored");
		response.Error.Should().Contain(legacyName, because: "the hint names the spelling the caller used")
			.And.Contain(canonicalName, because: "the hint names the canonical argument");
		resolvedCommand.CapturedOptions.Should().BeNull(because: "a call with a misspelled argument must not create anything");
	}

	[Test]
	[Description("Redacts the host from a failure message while keeping package-created, so the agent knows whether the package exists.")]
	public void CreatePackage_ShouldRedactTheError_AndKeepTheCreatedFlag() {
		// Arrange
		CreatePackageTool tool = CreateTool(new FakeCreatePackageCommand(new CreatePackageResponse {
			Success = false,
			PackageCreated = true,
			PackageName = "UsrCalls",
			Error = "dependencies were not applied: connection to http://creatio.local:8080/0/ServiceModel refused"
		}));

		// Act
		CreatePackageResponse response = tool.CreatePackage(new CreatePackageArgs(EnvironmentName, "Calls"));

		// Assert
		response.PackageCreated.Should().BeTrue(because: "the package exists and must not be created again");
		response.PackageName.Should().Be("UsrCalls", because: "the readback survives the redaction");
		response.Error.Should().NotContain("creatio.local", because: "the host must not cross the MCP boundary");
	}

	private static CreatePackageTool CreateTool(FakeCreatePackageCommand resolvedCommand) {
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreatePackageCommand>(Arg.Any<CreatePackageOptions>()).Returns(resolvedCommand);
		return new CreatePackageTool(resolvedCommand, ConsoleLogger.Instance, commandResolver);
	}

	private sealed class FakeCreatePackageCommand(CreatePackageResponse response)
		: CreatePackageCommand(Substitute.For<IRemotePackageCreator>(), new EnvironmentSettings()) {

		public CreatePackageOptions CapturedOptions { get; private set; }

		public override CreatePackageResponse CreatePackage(CreatePackageOptions options) {
			CapturedOptions = options;
			return response;
		}
	}
}
