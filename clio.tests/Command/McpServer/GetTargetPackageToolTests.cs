using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit coverage for the <c>get-target-package</c> probe tool: its read-only safety flags, the argument
/// mapping onto the environment-scoped command, and the failure classification an agent acts on — a
/// definitive "there is no usable target" must reach the client as such, and a failed read must not, or the
/// agent asks the user to pick another package over a network blip.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public class GetTargetPackageToolTests {

	private const string EnvironmentName = "docker_fix2";
	private const string PackageName = "UsrBrandingPkg";

	[Test]
	[Category("Unit")]
	[Description("Declares read-only, non-destructive safety flags on the get-target-package tool method so resolving the target package never prompts the host as a write would.")]
	public void GetTargetPackageTool_ShouldDeclareReadOnlySafetyFlags_WhenInspectingMcpServerToolAttribute() {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(GetTargetPackageTool)
			.GetMethod(nameof(GetTargetPackageTool.GetTargetPackage))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(GetTargetPackageTool.ToolName,
			because: "the tool must be published under its canonical kebab-case name");
		attribute.ReadOnly.Should().BeTrue(because: "resolving the target package only reads the environment");
		attribute.Destructive.Should().BeFalse(
			because: "a read that precedes a write must not be gated like the write itself, or the agent stops asking for it");
		attribute.Idempotent.Should().BeTrue(because: "resolving twice returns the same package");
		attribute.OpenWorld.Should().BeFalse(because: "the tool only touches the addressed Creatio environment");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the single args wrapper as required at the MCP schema level, so a call that omits args fails with a structured error instead of an opaque binding failure.")]
	public void GetTargetPackageTool_ShouldRequireArgsWrapper_WhenInspectingMethodSignature() {
		// Arrange & Act
		object[] requiredAttributes = typeof(GetTargetPackageTool)
			.GetMethod(nameof(GetTargetPackageTool.GetTargetPackage))!
			.GetParameters()[0]
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		requiredAttributes.Should().NotBeEmpty(
			because: "the args wrapper must be schema-required so an omitted args object fails with a structured error, not an opaque MCP binding failure");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves the environment-scoped command, forwards the package the caller named, and returns the resolved package name.")]
	public void GetTargetPackage_ShouldForwardThePackageAndReturnTheResolvedName() {
		// Arrange
		FakeGetTargetPackageCommand resolvedCommand = new(
			new GetTargetPackageResponse { Success = true, PackageName = PackageName });
		GetTargetPackageTool tool = CreateTool(resolvedCommand);

		// Act
		GetTargetPackageResponse response = tool.GetTargetPackage(
			new GetTargetPackageArgs(EnvironmentName: EnvironmentName, Package: PackageName));

		// Assert
		response.Success.Should().BeTrue(because: "the command resolved a usable target package");
		response.PackageName.Should().Be(PackageName,
			because: "the agent states this name to the user and passes it to every command of the same run");
		resolvedCommand.CapturedOptions.PackageName.Should().Be(PackageName,
			because: "a package the user named must be checked, not replaced by the environment's current package");
		resolvedCommand.CapturedOptions.Environment.Should().Be(EnvironmentName,
			because: "the probe reads the environment the caller addressed");
	}

	[Test]
	[Category("Unit")]
	[Description("Forwards an omitted package as blank so the command resolves the environment's current package instead of the tool inventing a name.")]
	public void GetTargetPackage_ShouldForwardABlankPackage_WhenTheCallerNamesNone() {
		// Arrange
		FakeGetTargetPackageCommand resolvedCommand = new(
			new GetTargetPackageResponse { Success = true, PackageName = PackageName });
		GetTargetPackageTool tool = CreateTool(resolvedCommand);

		// Act
		tool.GetTargetPackage(new GetTargetPackageArgs(EnvironmentName: EnvironmentName));

		// Assert
		resolvedCommand.CapturedOptions.PackageName.Should().BeNull(
			because: "the current-package convention lives in the resolver, so the tool passes the absence through untouched");
	}

	[Test]
	[Category("Unit")]
	[Description("Relays a definitive resolution failure as resolutionFailed so the agent asks the user for another package instead of retrying.")]
	public void GetTargetPackage_ShouldRelayADefinitiveFailure() {
		// Arrange
		FakeGetTargetPackageCommand resolvedCommand = new(new GetTargetPackageResponse {
			Success = false,
			ResolutionFailed = true,
			Error = "Package 'UsrBrandingPkg' is locked, so it cannot receive design-time writes."
		});
		GetTargetPackageTool tool = CreateTool(resolvedCommand);

		// Act
		GetTargetPackageResponse response = tool.GetTargetPackage(
			new GetTargetPackageArgs(EnvironmentName: EnvironmentName, Package: PackageName));

		// Assert
		response.Success.Should().BeFalse(because: "no usable target package was resolved");
		response.ResolutionFailed.Should().BeTrue(
			because: "the environment answered, so the agent must ask the user for another package rather than retry");
		response.Error.Should().Contain("locked", because: "the reason is what the agent relays to the user");
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps a failed read non-definitive and redacts the host detail it carries, so the agent retries instead of reporting that no target package exists.")]
	public void GetTargetPackage_ShouldKeepAFailedReadNonDefinitive_AndRedactIt() {
		// Arrange
		FakeGetTargetPackageCommand resolvedCommand = new(new GetTargetPackageResponse {
			Success = false,
			ResolutionFailed = false,
			Error = "The environment could not be asked which package to deliver the data into: " +
				"connection to http://creatio.local:8080/0/DataService refused"
		});
		GetTargetPackageTool tool = CreateTool(resolvedCommand);

		// Act
		GetTargetPackageResponse response = tool.GetTargetPackage(
			new GetTargetPackageArgs(EnvironmentName: EnvironmentName));

		// Assert
		response.ResolutionFailed.Should().BeFalse(
			because: "the environment was never asked, so the agent must not tell the user there is no target package");
		response.Error.Should().NotContain("creatio.local",
			because: "the raw read failure can carry the target host, which must not cross the MCP boundary");
	}

	[Test]
	[Category("Unit")]
	[Description("Fails with a structured error when environment-name is missing, before any environment is addressed.")]
	public void GetTargetPackage_ShouldFail_WhenEnvironmentNameIsMissing() {
		// Arrange
		GetTargetPackageTool tool = CreateTool(new FakeGetTargetPackageCommand(
			new GetTargetPackageResponse { Success = true, PackageName = PackageName }));

		// Act
		GetTargetPackageResponse response = tool.GetTargetPackage(new GetTargetPackageArgs(Package: PackageName));

		// Assert
		response.Success.Should().BeFalse(because: "there is no environment to read");
		response.Error.Should().Contain("environment-name",
			because: "the message must name the argument the caller has to add");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a camelCase package argument with a rename hint instead of silently resolving the environment's current package.")]
	public void GetTargetPackage_ShouldRejectALegacyAlias_WithARenameHint() {
		// Arrange
		GetTargetPackageTool tool = CreateTool(new FakeGetTargetPackageCommand(
			new GetTargetPackageResponse { Success = true, PackageName = PackageName }));
		GetTargetPackageArgs args = new(EnvironmentName: EnvironmentName) {
			ExtensionData = new() { ["packageName"] = JsonDocument.Parse($"\"{PackageName}\"").RootElement }
		};

		// Act
		GetTargetPackageResponse response = tool.GetTargetPackage(args);

		// Assert
		response.Success.Should().BeFalse(
			because: "silently ignoring the misspelled argument would resolve a different package than the caller asked about");
		response.Error.Should().Contain("package",
			because: "the hint must name the canonical argument");
	}

	private static GetTargetPackageTool CreateTool(FakeGetTargetPackageCommand resolvedCommand) {
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetTargetPackageCommand>(Arg.Any<GetTargetPackageOptions>())
			.Returns(resolvedCommand);
		return new GetTargetPackageTool(
			new FakeGetTargetPackageCommand(new GetTargetPackageResponse { Success = true, PackageName = PackageName }),
			ConsoleLogger.Instance,
			commandResolver);
	}

	private sealed class FakeGetTargetPackageCommand : GetTargetPackageCommand {

		private readonly GetTargetPackageResponse _response;

		public FakeGetTargetPackageCommand(GetTargetPackageResponse response)
			: base(Substitute.For<IPackageTargetResolver>(), Substitute.For<ILogger>()) {
			_response = response;
		}

		public GetTargetPackageOptions CapturedOptions { get; private set; }

		public override bool TryGetTargetPackage(
			GetTargetPackageOptions options, out GetTargetPackageResponse response) {
			CapturedOptions = options;
			response = _response;
			return _response.Success;
		}
	}
}
