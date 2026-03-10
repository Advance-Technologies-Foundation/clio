using System.ComponentModel.DataAnnotations;
using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.UserEnvironment;
using Clio.Requests;
using FluentAssertions;
using FluentValidation;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public class LinkFromRepositoryToolTests {

	[Test]
	[Description("Maps the environment-mode MCP arguments into link-from-repository command options without changing package selector semantics.")]
	[Category("Unit")]
	public void LinkFromRepositoryByEnvironment_Should_Map_Required_Arguments() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeLink4RepoCommand command = new();
		LinkFromRepositoryTool tool = new(command, ConsoleLogger.Instance);

		// Act
		CommandExecutionResult result = tool.LinkFromRepositoryByEnvironment(
			"dev",
			@"C:\Repo",
			"PkgA,PkgB");

		// Assert
		result.ExitCode.Should().Be(0, because: "the MCP tool should forward a valid link-from-repository payload");
		command.CapturedOptions.Should().NotBeNull(because: "the command should receive the mapped options");
		command.CapturedOptions!.Environment.Should().Be("dev",
			because: "the requested environment key must be preserved");
		command.CapturedOptions.EnvPkgPath.Should().BeNull(
			because: "the environment-mode MCP slice should not set the direct package path");
		command.CapturedOptions.RepoPath.Should().Be(@"C:\Repo",
			because: "the repository path must be forwarded without normalization changes");
		command.CapturedOptions.Packages.Should().Be("PkgA,PkgB",
			because: "the MCP tool must preserve the raw package selector string");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Maps the direct-path MCP arguments into link-from-repository command options without changing package selector semantics.")]
	[Category("Unit")]
	public void LinkFromRepositoryByEnvPackagePath_Should_Map_Required_Arguments() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeLink4RepoCommand command = new();
		LinkFromRepositoryTool tool = new(command, ConsoleLogger.Instance);

		// Act
		CommandExecutionResult result = tool.LinkFromRepositoryByEnvPackagePath(
			@"C:\Creatio\Terrasoft.Configuration\Pkg",
			@"C:\Repo",
			"*");

		// Assert
		result.ExitCode.Should().Be(0, because: "the MCP tool should forward a valid direct-path link-from-repository payload");
		command.CapturedOptions.Should().NotBeNull(because: "the command should receive the mapped options");
		command.CapturedOptions!.EnvPkgPath.Should().Be(@"C:\Creatio\Terrasoft.Configuration\Pkg",
			because: "the explicit environment package path must be preserved");
		command.CapturedOptions.Environment.Should().BeNull(
			because: "the direct-path MCP slice should not set a registered environment name");
		command.CapturedOptions.RepoPath.Should().Be(@"C:\Repo",
			because: "the repository path must be forwarded without normalization changes");
		command.CapturedOptions.Packages.Should().Be("*",
			because: "the MCP tool must preserve the wildcard package selector");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Marks each required link-from-repository MCP parameter with Required so the MCP contract advertises the mandatory inputs clearly.")]
	[Category("Unit")]
	[TestCase(nameof(LinkFromRepositoryTool.LinkFromRepositoryByEnvironment), "environmentName")]
	[TestCase(nameof(LinkFromRepositoryTool.LinkFromRepositoryByEnvironment), "repoPath")]
	[TestCase(nameof(LinkFromRepositoryTool.LinkFromRepositoryByEnvironment), "packages")]
	[TestCase(nameof(LinkFromRepositoryTool.LinkFromRepositoryByEnvPackagePath), "envPkgPath")]
	[TestCase(nameof(LinkFromRepositoryTool.LinkFromRepositoryByEnvPackagePath), "repoPath")]
	[TestCase(nameof(LinkFromRepositoryTool.LinkFromRepositoryByEnvPackagePath), "packages")]
	public void LinkFromRepository_Methods_Should_Expose_Required_Parameters(string methodName, string parameterName) {
		// Arrange
		System.Reflection.ParameterInfo parameter = typeof(LinkFromRepositoryTool)
			.GetMethod(methodName)!
			.GetParameters()
			.Single(candidate => candidate.Name == parameterName);

		// Act
		object[] requiredAttributes = parameter.GetCustomAttributes(typeof(RequiredAttribute), inherit: false);

		// Assert
		requiredAttributes.Should().ContainSingle(
			because: "the MCP contract should declare mandatory arguments explicitly");
	}

	[Test]
	[Description("Marks both link-from-repository MCP methods as destructive so MCP clients can apply confirmation and safety policies before the command deletes package folders.")]
	[Category("Unit")]
	[TestCase(nameof(LinkFromRepositoryTool.LinkFromRepositoryByEnvironment))]
	[TestCase(nameof(LinkFromRepositoryTool.LinkFromRepositoryByEnvPackagePath))]
	public void LinkFromRepository_Methods_Should_Be_Marked_As_Destructive(string methodName) {
		// Arrange
		System.Reflection.MethodInfo method = typeof(LinkFromRepositoryTool).GetMethod(methodName)!;
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
				Substitute.For<IValidator<Link4RepoOptions>>()) {
		}

		public override int Execute(Link4RepoOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
