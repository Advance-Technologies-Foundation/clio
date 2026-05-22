using System.ComponentModel.DataAnnotations;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Package;
using Clio.UserEnvironment;
using Clio.Requests;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class LinkFromRepositoryToolTests {

	[Test]
	[Description("Maps mode='by-env' MCP arguments into link-from-repository command options without changing package selector semantics.")]
	[Category("Unit")]
	public void LinkFromRepository_ByEnv_Should_Map_Required_Arguments() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeLink4RepoCommand command = new();
		LinkFromRepositoryTool tool = new(command, ConsoleLogger.Instance);

		// Act
		CommandExecutionResult result = tool.LinkFromRepository(new LinkFromRepositoryArgs(
			Mode: LinkFromRepositoryTool.ModeByEnv,
			RepoPath: @"C:\Repo",
			EnvironmentName: "dev",
			Packages: "PkgA,PkgB"));

		// Assert
		result.ExitCode.Should().Be(0, because: "the MCP tool should forward a valid link-from-repository payload");
		command.CapturedOptions.Should().NotBeNull(because: "the command should receive the mapped options");
		command.CapturedOptions!.Environment.Should().Be("dev",
			because: "the requested environment key must be preserved");
		command.CapturedOptions.EnvPkgPath.Should().BeNull(
			because: "the by-env mode should not set the direct package path");
		command.CapturedOptions.RepoPath.Should().Be(@"C:\Repo",
			because: "the repository path must be forwarded without normalization changes");
		command.CapturedOptions.Packages.Should().Be("PkgA,PkgB",
			because: "the MCP tool must preserve the raw package selector string");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Maps mode='by-pkg-path' MCP arguments into link-from-repository command options without changing package selector semantics.")]
	[Category("Unit")]
	public void LinkFromRepository_ByPkgPath_Should_Map_Required_Arguments() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeLink4RepoCommand command = new();
		LinkFromRepositoryTool tool = new(command, ConsoleLogger.Instance);

		// Act
		CommandExecutionResult result = tool.LinkFromRepository(new LinkFromRepositoryArgs(
			Mode: LinkFromRepositoryTool.ModeByPkgPath,
			RepoPath: @"C:\Repo",
			EnvPkgPath: @"C:\Creatio\Terrasoft.Configuration\Pkg",
			Packages: "*"));

		// Assert
		result.ExitCode.Should().Be(0, because: "the MCP tool should forward a valid direct-path link-from-repository payload");
		command.CapturedOptions.Should().NotBeNull(because: "the command should receive the mapped options");
		command.CapturedOptions!.EnvPkgPath.Should().Be(@"C:\Creatio\Terrasoft.Configuration\Pkg",
			because: "the explicit environment package path must be preserved");
		command.CapturedOptions.Environment.Should().BeNull(
			because: "the by-pkg-path mode should not set a registered environment name");
		command.CapturedOptions.RepoPath.Should().Be(@"C:\Repo",
			because: "the repository path must be forwarded without normalization changes");
		command.CapturedOptions.Packages.Should().Be("*",
			because: "the MCP tool must preserve the wildcard package selector");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Maps mode='unlocked' MCP arguments into link-from-repository options with Unlocked=true and no Packages.")]
	[Category("Unit")]
	public void LinkFromRepository_Unlocked_Should_Map_Arguments() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeLink4RepoCommand command = new();
		LinkFromRepositoryTool tool = new(command, ConsoleLogger.Instance);

		// Act
		CommandExecutionResult result = tool.LinkFromRepository(new LinkFromRepositoryArgs(
			Mode: LinkFromRepositoryTool.ModeUnlocked,
			RepoPath: @"C:\Repo",
			EnvironmentName: "dev"));

		// Assert
		result.ExitCode.Should().Be(0);
		command.CapturedOptions.Should().NotBeNull();
		command.CapturedOptions!.Environment.Should().Be("dev");
		command.CapturedOptions.RepoPath.Should().Be(@"C:\Repo");
		command.CapturedOptions.Unlocked.Should().BeTrue(
			because: "the unlocked mode must set the Unlocked flag");
		command.CapturedOptions.Packages.Should().BeNull(
			because: "the unlocked flow does not require packages");
		command.CapturedOptions.DryRun.Should().BeFalse(
			because: "dry-run defaults to false when not specified");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Maps dry-run flag through the unlocked mode.")]
	[Category("Unit")]
	public void LinkFromRepository_Unlocked_DryRun_Should_Map() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeLink4RepoCommand command = new();
		LinkFromRepositoryTool tool = new(command, ConsoleLogger.Instance);

		// Act
		CommandExecutionResult result = tool.LinkFromRepository(new LinkFromRepositoryArgs(
			Mode: LinkFromRepositoryTool.ModeUnlocked,
			RepoPath: @"C:\Repo",
			EnvironmentName: "dev",
			DryRun: true));

		// Assert
		result.ExitCode.Should().Be(0);
		command.CapturedOptions!.DryRun.Should().BeTrue(
			because: "the dry-run flag must be forwarded to the command");
		command.CapturedOptions.Unlocked.Should().BeTrue();
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Maps dry-run and skip-preparation flags through mode='by-env'.")]
	[Category("Unit")]
	public void LinkFromRepository_ByEnv_OptionalFlags_Should_Map() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeLink4RepoCommand command = new();
		LinkFromRepositoryTool tool = new(command, ConsoleLogger.Instance);

		// Act
		CommandExecutionResult result = tool.LinkFromRepository(new LinkFromRepositoryArgs(
			Mode: LinkFromRepositoryTool.ModeByEnv,
			RepoPath: @"C:\Repo",
			EnvironmentName: "dev",
			Packages: "PkgA",
			DryRun: true,
			SkipPreparation: true));

		// Assert
		result.ExitCode.Should().Be(0);
		command.CapturedOptions!.DryRun.Should().BeTrue(
			because: "the dry-run flag must be forwarded to the command");
		command.CapturedOptions.SkipPreparation.Should().BeTrue(
			because: "the skip-preparation flag must be forwarded to the command");
		command.CapturedOptions.Packages.Should().Be("PkgA");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Maps dry-run and skip-preparation flags through mode='by-pkg-path'.")]
	[Category("Unit")]
	public void LinkFromRepository_ByPkgPath_OptionalFlags_Should_Map() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeLink4RepoCommand command = new();
		LinkFromRepositoryTool tool = new(command, ConsoleLogger.Instance);

		// Act
		CommandExecutionResult result = tool.LinkFromRepository(new LinkFromRepositoryArgs(
			Mode: LinkFromRepositoryTool.ModeByPkgPath,
			RepoPath: @"C:\Repo",
			EnvPkgPath: @"C:\Creatio\Pkg",
			Packages: "PkgB",
			DryRun: true,
			SkipPreparation: true));

		// Assert
		result.ExitCode.Should().Be(0);
		command.CapturedOptions!.DryRun.Should().BeTrue();
		command.CapturedOptions.SkipPreparation.Should().BeTrue();
		command.CapturedOptions.EnvPkgPath.Should().Be(@"C:\Creatio\Pkg");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Optional flags default to false when not provided.")]
	[Category("Unit")]
	public void LinkFromRepository_ByEnv_OptionalFlags_DefaultToFalse() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeLink4RepoCommand command = new();
		LinkFromRepositoryTool tool = new(command, ConsoleLogger.Instance);

		// Act
		tool.LinkFromRepository(new LinkFromRepositoryArgs(
			Mode: LinkFromRepositoryTool.ModeByEnv,
			RepoPath: @"C:\Repo",
			EnvironmentName: "dev",
			Packages: "PkgA"));

		// Assert
		command.CapturedOptions!.DryRun.Should().BeFalse(
			because: "dry-run should default to false when not specified");
		command.CapturedOptions.SkipPreparation.Should().BeFalse(
			because: "skip-preparation should default to false when not specified");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Rejects an invalid mode value with a clear error listing allowed modes.")]
	[Category("Unit")]
	public void LinkFromRepository_Should_Reject_Invalid_Mode() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeLink4RepoCommand command = new();
		LinkFromRepositoryTool tool = new(command, ConsoleLogger.Instance);

		// Act
		CommandExecutionResult result = tool.LinkFromRepository(new LinkFromRepositoryArgs(
			Mode: "bogus",
			RepoPath: @"C:\Repo",
			EnvironmentName: "dev",
			Packages: "PkgA"));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "an unknown mode discriminator should be rejected before any command is invoked");
		command.CapturedOptions.Should().BeNull(
			because: "the underlying command must not run when the mode discriminator is invalid");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Marks the link-from-repository MCP tool as destructive so MCP clients can apply confirmation and safety policies.")]
	[Category("Unit")]
	public void LinkFromRepository_Should_Be_Marked_As_Destructive() {
		// Arrange
		System.Reflection.MethodInfo method = typeof(LinkFromRepositoryTool)
			.GetMethod(nameof(LinkFromRepositoryTool.LinkFromRepository))!;
		ModelContextProtocol.Server.McpServerToolAttribute attribute = method
			.GetCustomAttributes(typeof(ModelContextProtocol.Server.McpServerToolAttribute), inherit: false)
			.Cast<ModelContextProtocol.Server.McpServerToolAttribute>()
			.Single();

		// Act
		bool destructive = attribute.Destructive;

		// Assert
		destructive.Should().BeTrue(
			because: "link-from-repository deletes existing package directories before replacing them with symbolic links");
	}

	[Test]
	[Description("Advertises the stable consolidated tool name 'link-from-repository' and the three supported mode discriminator values.")]
	[Category("Unit")]
	public void LinkFromRepository_Should_Advertise_Stable_Contract() {
		// Arrange / Act
		string toolName = LinkFromRepositoryTool.LinkFromRepositoryToolName;
		string[] modes = [
			LinkFromRepositoryTool.ModeByEnv,
			LinkFromRepositoryTool.ModeByPkgPath,
			LinkFromRepositoryTool.ModeUnlocked,
		];

		// Assert
		toolName.Should().Be("link-from-repository",
			because: "the MCP contract identifier must stay stable after consolidation");
		modes.Should().BeEquivalentTo(["by-env", "by-pkg-path", "unlocked"],
			because: "the three supported mode discriminator values must remain stable");
	}

	private sealed class FakeLink4RepoCommand : Link4RepoCommand {
		public Link4RepoOptions? CapturedOptions { get; private set; }

		public FakeLink4RepoCommand()
			: base(
				ConsoleLogger.Instance,
				Substitute.For<MediatR.IMediator>(),
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
