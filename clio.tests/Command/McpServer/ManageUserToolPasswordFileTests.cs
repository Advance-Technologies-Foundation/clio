using System;
using System.Linq;
using Clio.Command.Administration;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Pins that the manage-user MCP tool refuses a malformed or ambiguous password reference before it resolves an
/// environment, without repeating the value.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ManageUserToolPasswordFileTests {
	private IToolCommandResolver _resolver;
	private ManageUserTool _tool;

	[SetUp]
	public void CreateTool() {
		_resolver = Substitute.For<IToolCommandResolver>();
		ManageUserCommand command = new(
			Substitute.For<IAdministrationService>(), Substitute.For<ILogger>(), Substitute.For<IClioSecretFileReader>());
		_tool = new ManageUserTool(command, Substitute.For<ILogger>(), _resolver);
	}

	private static string Text(CommandExecutionResult result) =>
		string.Join(" ", result.Output?.Select(message => message.Value?.ToString()) ?? []);

	[TestCase("../../etc/passwd")]
	[TestCase("/Users/someone/secret.txt")]
	[TestCase("Pa55 word!")]
	[Description("A password-file that is not a plain name is refused before the environment is resolved, and the value is not repeated.")]
	public void Manage_ShouldRefuseAPasswordFileThatIsNotAPlainName(string value) {
		// Arrange
		ManageUserArgs args = new() {
			EnvironmentName = "dev", Action = "password", Id = Guid.NewGuid(), PasswordFile = value
		};

		// Act
		CommandExecutionResult result = _tool.Manage(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "a path or a pasted secret is not a reference the host gave");
		Text(result).Should().Contain("password-file must be the name of a file",
			because: "the refusal must say what a valid reference is");
		Text(result).Should().NotContain(value, because: "the value may be a secret pasted by mistake");
		_resolver.ReceivedCalls().Should().BeEmpty(because: "nothing may reach an environment for a malformed reference");
	}

	[Test]
	[Description("Naming both password channels is refused before the environment is resolved.")]
	public void Manage_ShouldRefuseBothPasswordReferences() {
		// Arrange
		ManageUserArgs args = new() {
			EnvironmentName = "dev", Action = "create", Id = Guid.NewGuid(), PasswordFile = "demo-users",
			PasswordEnvironmentVariable = "CLIO_ADMIN_PASSWORD_DEMO"
		};

		// Act
		CommandExecutionResult result = _tool.Manage(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "two references leave it ambiguous which password the caller meant");
		Text(result).Should().Contain(ManageUserCommand.BothReferencesMessage, because: "the refusal must say how to fix the call");
		_resolver.ReceivedCalls().Should().BeEmpty(because: "nothing may reach an environment for an ambiguous reference");
	}
}
