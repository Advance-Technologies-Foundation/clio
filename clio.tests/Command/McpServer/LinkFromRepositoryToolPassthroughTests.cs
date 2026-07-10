using System.Linq;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Package;
using Clio.Requests;
using Clio.UserEnvironment;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Story 1 (ENG-93347) passthrough behavior of the <c>link-from-repository-*</c> MCP tool family:
/// uniform guard rejection under credential passthrough (including mixed input), the allowed
/// local-only <c>skip-preparation=true</c> branch, the explicit non-passthrough
/// <c>environment-name</c> requiredness error, and unchanged stdio/registered-environment behavior.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public class LinkFromRepositoryToolPassthroughTests {

	private const string UniformMessageFragment = "not supported under credential passthrough";
	private const string GenericValidatorMessage = "Either path to creatio directory or environment name must be provided";

	private static ICredentialPassthroughToolGuard CreateGuard(bool passthroughActive) {
		ICredentialContextAccessor accessor = Substitute.For<ICredentialContextAccessor>();
		accessor.Current.Returns(passthroughActive
			? new CredentialContext(
				"https://tenant.example.com",
				CredentialMaterial.FromAccessToken("super-secret-token", "Bearer"),
				McpTransport.Http,
				PassthroughModeEnabled: true)
			: null);
		return new CredentialPassthroughToolGuard(accessor);
	}

	private static string CollectMessages(CommandExecutionResult result) =>
		string.Join(" | ", result.Output.Select(message => message.Value?.ToString()));

	[SetUp]
	public void SetUp() => ConsoleLogger.Instance.ClearMessages();

	[TearDown]
	public void TearDown() => ConsoleLogger.Instance.ClearMessages();

	[Test]
	[Category("Unit")]
	[Description("AC-01: under authorized passthrough with no environment-name, link-from-repository-by-environment returns the uniform not-supported rejection before any Creatio-reaching call — never the generic validator message.")]
	public void LinkFromRepositoryByEnvironment_ShouldReturnUniformRejection_WhenPassthroughActiveWithoutEnvironmentName() {
		// Arrange
		FakeLink4RepoCommand command = new();
		LinkFromRepositoryTool tool = new(command, ConsoleLogger.Instance, CreateGuard(passthroughActive: true));

		// Act
		CommandExecutionResult result = tool.LinkFromRepositoryByEnvironment(@"C:\Repo", "PkgA");

		// Assert
		result.ExitCode.Should().NotBe(0,
			because: "a passthrough call to a guard-only tool must fail");
		CollectMessages(result).Should().Contain(UniformMessageFragment,
			because: "the guard must return the single uniform FR-04 rejection message");
		CollectMessages(result).Should().Contain(LinkFromRepositoryTool.LinkFromRepositoryByEnvironmentToolName,
			because: "the uniform message must name the rejected tool");
		CollectMessages(result).Should().NotContain(GenericValidatorMessage,
			because: "the rejection must never fall through to Link4RepoOptionsValidator's generic message");
		command.CapturedOptions.Should().BeNull(
			because: "the guard must fire BEFORE the command executes, so no Creatio-reaching call is ever made");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-01: under authorized passthrough with no environment-name, link-from-repository-unlocked returns the uniform not-supported rejection before any Creatio-reaching call.")]
	public void LinkFromRepositoryUnlocked_ShouldReturnUniformRejection_WhenPassthroughActiveWithoutEnvironmentName() {
		// Arrange
		FakeLink4RepoCommand command = new();
		LinkFromRepositoryTool tool = new(command, ConsoleLogger.Instance, CreateGuard(passthroughActive: true));

		// Act
		CommandExecutionResult result = tool.LinkFromRepositoryUnlocked(@"C:\Repo");

		// Assert
		result.ExitCode.Should().NotBe(0,
			because: "a passthrough call to the always-Creatio-reaching unlocked variant must fail");
		CollectMessages(result).Should().Contain(UniformMessageFragment,
			because: "the guard must return the single uniform FR-04 rejection message");
		CollectMessages(result).Should().Contain(LinkFromRepositoryTool.LinkFromRepositoryUnlockedToolName,
			because: "the uniform message must name the rejected tool");
		command.CapturedOptions.Should().BeNull(
			because: "the guard must fire BEFORE the command executes, so the site is never queried for unlocked packages");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-02: under authorized passthrough WITH an explicit environment-name naming a different registered environment, link-from-repository-by-environment is rejected before execution — the named environment's stored credentials are never used (confused-deputy closed).")]
	public void LinkFromRepositoryByEnvironment_ShouldRejectBeforeExecution_WhenPassthroughActiveWithExplicitEnvironmentName() {
		// Arrange
		FakeLink4RepoCommand command = new();
		LinkFromRepositoryTool tool = new(command, ConsoleLogger.Instance, CreateGuard(passthroughActive: true));

		// Act
		CommandExecutionResult result = tool.LinkFromRepositoryByEnvironment(
			@"C:\Repo", "PkgA", environmentName: "otherRegisteredEnv");

		// Assert
		result.ExitCode.Should().NotBe(0,
			because: "mixed input (header + explicit environment-name) must be rejected under passthrough");
		CollectMessages(result).Should().Contain(UniformMessageFragment,
			because: "the mixed-input rejection reuses the same uniform guard message");
		command.CapturedOptions.Should().BeNull(
			because: "the named environment's stored credentials must never be used on a passthrough request (Security mode iii)");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-02: under authorized passthrough WITH an explicit environment-name, link-from-repository-unlocked is rejected before execution — the named environment's stored credentials are never used.")]
	public void LinkFromRepositoryUnlocked_ShouldRejectBeforeExecution_WhenPassthroughActiveWithExplicitEnvironmentName() {
		// Arrange
		FakeLink4RepoCommand command = new();
		LinkFromRepositoryTool tool = new(command, ConsoleLogger.Instance, CreateGuard(passthroughActive: true));

		// Act
		CommandExecutionResult result = tool.LinkFromRepositoryUnlocked(
			@"C:\Repo", environmentName: "otherRegisteredEnv");

		// Assert
		result.ExitCode.Should().NotBe(0,
			because: "mixed input (header + explicit environment-name) must be rejected under passthrough");
		CollectMessages(result).Should().Contain(UniformMessageFragment,
			because: "the mixed-input rejection reuses the same uniform guard message");
		command.CapturedOptions.Should().BeNull(
			because: "the named environment's stored credentials must never be used on a passthrough request");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-03: under authorized passthrough, link-from-repository-by-env-package-path with skip-preparation absent (defaults to false — the Creatio-reaching preparation branch) is rejected before any preparation call.")]
	public void LinkFromRepositoryByEnvPackagePath_ShouldReturnUniformRejection_WhenPassthroughActiveAndSkipPreparationAbsent() {
		// Arrange
		FakeLink4RepoCommand command = new();
		LinkFromRepositoryTool tool = new(command, ConsoleLogger.Instance, CreateGuard(passthroughActive: true));

		// Act
		CommandExecutionResult result = tool.LinkFromRepositoryByEnvPackagePath(
			@"C:\Creatio\Pkg", @"C:\Repo", "PkgA");

		// Assert
		result.ExitCode.Should().NotBe(0,
			because: "the preparation branch reaches Creatio (maintainer read/write + lock/design-mode) and must fail fast under passthrough");
		CollectMessages(result).Should().Contain(UniformMessageFragment,
			because: "the guard must return the single uniform FR-04 rejection message");
		CollectMessages(result).Should().Contain(LinkFromRepositoryTool.LinkFromRepositoryByEnvPackagePathToolName,
			because: "the uniform message must name the rejected tool");
		command.CapturedOptions.Should().BeNull(
			because: "no preparation Creatio call may execute before the guard fires");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-04: under authorized passthrough, link-from-repository-by-env-package-path with an explicit skip-preparation=false (mixed input against a registered environment's package path) is rejected before any preparation call — stored credentials are never used.")]
	public void LinkFromRepositoryByEnvPackagePath_ShouldRejectBeforeExecution_WhenPassthroughActiveAndSkipPreparationFalse() {
		// Arrange
		FakeLink4RepoCommand command = new();
		LinkFromRepositoryTool tool = new(command, ConsoleLogger.Instance, CreateGuard(passthroughActive: true));

		// Act
		CommandExecutionResult result = tool.LinkFromRepositoryByEnvPackagePath(
			@"C:\Creatio\RegisteredEnv\Terrasoft.Configuration\Pkg", @"C:\Repo", "PkgA",
			skipPreparation: false);

		// Assert
		result.ExitCode.Should().NotBe(0,
			because: "an explicit skip-preparation=false selects the Creatio-reaching branch and must be rejected under passthrough");
		CollectMessages(result).Should().Contain(UniformMessageFragment,
			because: "the rejection reuses the same uniform guard message");
		command.CapturedOptions.Should().BeNull(
			because: "the registered environment resolved from the package path must never be prepared with stored credentials on a passthrough request");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-05: under authorized passthrough, link-from-repository-by-env-package-path with skip-preparation=true (local-only, no Creatio call) is NOT rejected by the guard and executes normally.")]
	public void LinkFromRepositoryByEnvPackagePath_ShouldExecute_WhenPassthroughActiveAndSkipPreparationTrue() {
		// Arrange
		FakeLink4RepoCommand command = new();
		LinkFromRepositoryTool tool = new(command, ConsoleLogger.Instance, CreateGuard(passthroughActive: true));

		// Act
		CommandExecutionResult result = tool.LinkFromRepositoryByEnvPackagePath(
			@"C:\Creatio\Pkg", @"C:\Repo", "PkgA",
			skipPreparation: true);

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the skip-preparation=true branch makes no Creatio call, so the guard must not fire even under passthrough");
		command.CapturedOptions.Should().NotBeNull(
			because: "the local-only branch must execute the command normally");
		command.CapturedOptions!.SkipPreparation.Should().BeTrue(
			because: "the skip-preparation flag must still be forwarded to the command");
		command.CapturedOptions.EnvPkgPath.Should().Be(@"C:\Creatio\Pkg",
			because: "the explicit package path must still be forwarded unchanged");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-ERR: the guard rejection envelope never echoes credential material (access token / target URL) carried by the passthrough context.")]
	public void LinkFromRepositoryByEnvironment_ShouldNotLeakCredentialMaterial_WhenPassthroughRejectionIsReturned() {
		// Arrange
		FakeLink4RepoCommand command = new();
		LinkFromRepositoryTool tool = new(command, ConsoleLogger.Instance, CreateGuard(passthroughActive: true));

		// Act
		CommandExecutionResult result = tool.LinkFromRepositoryByEnvironment(@"C:\Repo", "PkgA");

		// Assert
		CollectMessages(result).Should().NotContain("super-secret-token",
			because: "the rejection envelope must never echo the access token from the credential header");
		CollectMessages(result).Should().NotContain("tenant.example.com",
			because: "the rejection envelope must not disclose the passthrough target URL");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-06: outside passthrough, a blank environment-name on link-from-repository-by-environment returns the explicit tool-level requiredness error — never Link4RepoOptionsValidator's generic message.")]
	public void LinkFromRepositoryByEnvironment_ShouldReturnExplicitRequirednessError_WhenNotPassthroughAndEnvironmentNameBlank() {
		// Arrange
		FakeLink4RepoCommand command = new();
		LinkFromRepositoryTool tool = new(command, ConsoleLogger.Instance, CreateGuard(passthroughActive: false));

		// Act
		CommandExecutionResult result = tool.LinkFromRepositoryByEnvironment(@"C:\Repo", "PkgA");

		// Assert
		result.ExitCode.Should().Be(1,
			because: "a missing required argument is an EXPECTED, caller-actionable validation error");
		CollectMessages(result).Should().Contain(
			$"environment-name is required for {LinkFromRepositoryTool.LinkFromRepositoryByEnvironmentToolName} outside credential passthrough.",
			because: "the tool must own this requiredness contract explicitly (OQ-03)");
		CollectMessages(result).Should().NotContain(GenericValidatorMessage,
			because: "the error must never fall through to the generic FluentValidation message");
		command.CapturedOptions.Should().BeNull(
			because: "the command must not execute without a resolvable environment selector");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-06: outside passthrough, a blank environment-name on link-from-repository-unlocked returns the explicit tool-level requiredness error — never Link4RepoOptionsValidator's generic message.")]
	public void LinkFromRepositoryUnlocked_ShouldReturnExplicitRequirednessError_WhenNotPassthroughAndEnvironmentNameBlank() {
		// Arrange
		FakeLink4RepoCommand command = new();
		LinkFromRepositoryTool tool = new(command, ConsoleLogger.Instance, CreateGuard(passthroughActive: false));

		// Act
		CommandExecutionResult result = tool.LinkFromRepositoryUnlocked(@"C:\Repo");

		// Assert
		result.ExitCode.Should().Be(1,
			because: "a missing required argument is an EXPECTED, caller-actionable validation error");
		CollectMessages(result).Should().Contain(
			$"environment-name is required for {LinkFromRepositoryTool.LinkFromRepositoryUnlockedToolName} outside credential passthrough.",
			because: "the tool must own this requiredness contract explicitly (OQ-03)");
		CollectMessages(result).Should().NotContain(GenericValidatorMessage,
			because: "the error must never fall through to the generic FluentValidation message");
		command.CapturedOptions.Should().BeNull(
			because: "the command must not execute without an environment to query for unlocked packages");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-07: outside passthrough (registered environment supplied), link-from-repository-by-environment maps and executes exactly as the pre-change baseline.")]
	public void LinkFromRepositoryByEnvironment_ShouldExecuteUnchanged_WhenNotPassthroughAndEnvironmentNameSupplied() {
		// Arrange
		FakeLink4RepoCommand command = new();
		LinkFromRepositoryTool tool = new(command, ConsoleLogger.Instance, CreateGuard(passthroughActive: false));

		// Act
		CommandExecutionResult result = tool.LinkFromRepositoryByEnvironment(
			@"C:\Repo", "PkgA,PkgB", environmentName: "dev");

		// Assert
		result.ExitCode.Should().Be(0,
			because: "registered-environment behavior must match the pre-change baseline exactly");
		command.CapturedOptions.Should().NotBeNull(
			because: "the command must receive the mapped options as before");
		command.CapturedOptions!.Environment.Should().Be("dev",
			because: "the requested environment key must be preserved");
		command.CapturedOptions.Packages.Should().Be("PkgA,PkgB",
			because: "the raw package selector must be preserved");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-07: a tool constructed WITHOUT a guard (stdio host shape) with environment-name supplied behaves exactly as the pre-change baseline for the unlocked variant.")]
	public void LinkFromRepositoryUnlocked_ShouldExecuteUnchanged_WhenGuardNotWiredAndEnvironmentNameSupplied() {
		// Arrange
		FakeLink4RepoCommand command = new();
		LinkFromRepositoryTool tool = new(command, ConsoleLogger.Instance);

		// Act
		CommandExecutionResult result = tool.LinkFromRepositoryUnlocked(@"C:\Repo", environmentName: "dev");

		// Assert
		result.ExitCode.Should().Be(0,
			because: "stdio behavior with an environment name must match the pre-change baseline exactly");
		command.CapturedOptions.Should().NotBeNull(
			because: "the command must receive the mapped options as before");
		command.CapturedOptions!.Environment.Should().Be("dev",
			because: "the requested environment key must be preserved");
		command.CapturedOptions.Unlocked.Should().BeTrue(
			because: "the unlocked flag must still be set by this variant");
	}

	[Test]
	[Category("Unit")]
	[Description("FR-05a: environment-name is schema-optional (has a default value and no [Required]) on both name-based methods, so a header-only passthrough call reaches the guard instead of failing MCP binding; envPkgPath stays required unconditionally.")]
	[TestCase(nameof(LinkFromRepositoryTool.LinkFromRepositoryByEnvironment))]
	[TestCase(nameof(LinkFromRepositoryTool.LinkFromRepositoryUnlocked))]
	public void LinkFromRepository_NameBasedMethods_ShouldExposeOptionalEnvironmentName(string methodName) {
		// Arrange
		System.Reflection.ParameterInfo parameter = typeof(LinkFromRepositoryTool)
			.GetMethod(methodName)!
			.GetParameters()
			.Single(candidate => candidate.Name == "environmentName");

		// Act
		bool hasDefaultValue = parameter.HasDefaultValue;
		object[] requiredAttributes = parameter.GetCustomAttributes(
			typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), inherit: false);

		// Assert
		hasDefaultValue.Should().BeTrue(
			because: "environment-name must be schema-optional so a header-only passthrough call is not rejected at the MCP binding layer");
		requiredAttributes.Should().BeEmpty(
			because: "the [Required] attribute was deliberately relaxed for the two name-based methods (ADR OQ-03)");
	}

	private sealed class FakeLink4RepoCommand : Link4RepoCommand {
		public Link4RepoOptions? CapturedOptions { get; private set; }

		public FakeLink4RepoCommand()
			: base(
				ConsoleLogger.Instance,
				Substitute.For<IIisScanner>(),
				Substitute.For<ISettingsRepository>(),
				Substitute.For<IFileSystem>(),
				new RfsEnvironment(
					Substitute.For<IFileSystem>(),
					Substitute.For<IPackageUtilities>(),
					Substitute.For<ILogger>()),
				Substitute.For<IValidator<Link4RepoOptions>>(),
				Substitute.For<IApplicationPackageListProvider>(),
				Substitute.For<IJsonConverter>(),
				Substitute.For<ISysSettingsManager>(),
				Substitute.For<IPackageLockManager>(),
				Substitute.For<IFileDesignModePackages>()) {
		}

		public override int Execute(Link4RepoOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
