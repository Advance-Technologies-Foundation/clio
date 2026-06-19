using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class IdentityAssertionToolTests {

	[Test]
	[Description("Resolves the get-identity-assertion MCP tool for the requested environment and forwards the environment, format, and timeout into options.")]
	[Category("Unit")]
	public void GetIdentityAssertion_Should_Resolve_Command_And_Forward_Options() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeGetIdentityAssertionCommand resolvedCommand = new();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<GetIdentityAssertionCommand>(Arg.Any<GetIdentityAssertionOptions>()).Returns(resolvedCommand);
		GetIdentityAssertionTool tool = new(new FakeGetIdentityAssertionCommand(), ConsoleLogger.Instance, resolver);

		// Act
		CommandExecutionResult result = tool.GetIdentityAssertion("dev", "json");

		// Assert
		result.ExitCode.Should().Be(0, because: "the tool should forward a valid assertion command payload");
		resolver.Received(1).Resolve<GetIdentityAssertionCommand>(Arg.Is<GetIdentityAssertionOptions>(o =>
			o.Environment == "dev" && o.Format == IdentityOutputFormat.Json && o.TimeOut == 30_000));
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Defaults the get-identity-assertion MCP tool to text format when the format argument is omitted.")]
	[Category("Unit")]
	public void GetIdentityAssertion_Should_Default_To_Text_Format_When_Omitted() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeGetIdentityAssertionCommand resolvedCommand = new();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<GetIdentityAssertionCommand>(Arg.Any<GetIdentityAssertionOptions>()).Returns(resolvedCommand);
		GetIdentityAssertionTool tool = new(new FakeGetIdentityAssertionCommand(), ConsoleLogger.Instance, resolver);

		// Act
		CommandExecutionResult result = tool.GetIdentityAssertion("dev");

		// Assert
		result.ExitCode.Should().Be(0, because: "the tool should run with the default text format");
		resolver.Received(1).Resolve<GetIdentityAssertionCommand>(Arg.Is<GetIdentityAssertionOptions>(o =>
			o.Format == IdentityOutputFormat.Text));
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns an error from the get-identity-assertion MCP tool when the environment name is empty.")]
	[Category("Unit")]
	public void GetIdentityAssertion_Should_Return_Error_When_Environment_Is_Empty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		GetIdentityAssertionTool tool = new(new FakeGetIdentityAssertionCommand(), ConsoleLogger.Instance, resolver);

		// Act
		CommandExecutionResult result = tool.GetIdentityAssertion("   ");

		// Assert
		result.ExitCode.Should().NotBe(0, because: "an empty environment name is invalid input");
		resolver.DidNotReceive().Resolve<GetIdentityAssertionCommand>(Arg.Any<GetIdentityAssertionOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Resolves the get-identity-public-jwk MCP tool for the requested environment and forwards options.")]
	[Category("Unit")]
	public void GetIdentityPublicJwk_Should_Resolve_Command_And_Forward_Options() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeGetIdentityPublicJwkCommand resolvedCommand = new();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<GetIdentityPublicJwkCommand>(Arg.Any<GetIdentityPublicJwkOptions>()).Returns(resolvedCommand);
		GetIdentityPublicJwkTool tool = new(new FakeGetIdentityPublicJwkCommand(), ConsoleLogger.Instance, resolver);

		// Act
		CommandExecutionResult result = tool.GetIdentityPublicJwk("dev");

		// Assert
		result.ExitCode.Should().Be(0, because: "the tool should forward a valid public-jwk command payload");
		resolver.Received(1).Resolve<GetIdentityPublicJwkCommand>(Arg.Is<GetIdentityPublicJwkOptions>(o =>
			o.Environment == "dev" && o.Format == IdentityOutputFormat.Text && o.TimeOut == 30_000));
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Resolves the regenerate-identity-signing-key MCP tool for the requested environment and forwards options.")]
	[Category("Unit")]
	public void RegenerateIdentitySigningKey_Should_Resolve_Command_And_Forward_Options() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeRegenerateIdentitySigningKeyCommand resolvedCommand = new();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<RegenerateIdentitySigningKeyCommand>(Arg.Any<RegenerateIdentitySigningKeyOptions>())
			.Returns(resolvedCommand);
		RegenerateIdentitySigningKeyTool tool =
			new(new FakeRegenerateIdentitySigningKeyCommand(), ConsoleLogger.Instance, resolver);

		// Act
		CommandExecutionResult result = tool.RegenerateIdentitySigningKey("dev", "json");

		// Assert
		result.ExitCode.Should().Be(0, because: "the tool should forward a valid regenerate command payload");
		resolver.Received(1).Resolve<RegenerateIdentitySigningKeyCommand>(
			Arg.Is<RegenerateIdentitySigningKeyOptions>(o =>
				o.Environment == "dev" && o.Format == IdentityOutputFormat.Json && o.TimeOut == 30_000));
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Resolves the check-auth-code-flow MCP tool for the requested environment and forwards options.")]
	[Category("Unit")]
	public void CheckAuthCodeFlow_Should_Resolve_Command_And_Forward_Options() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCheckAuthCodeFlowCommand resolvedCommand = new();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<CheckAuthCodeFlowCommand>(Arg.Any<CheckAuthCodeFlowOptions>()).Returns(resolvedCommand);
		CheckAuthCodeFlowTool tool = new(new FakeCheckAuthCodeFlowCommand(), ConsoleLogger.Instance, resolver);

		// Act
		CommandExecutionResult result = tool.CheckAuthCodeFlow("dev");

		// Assert
		result.ExitCode.Should().Be(0, because: "the tool should forward a valid auth-code-flow command payload");
		resolver.Received(1).Resolve<CheckAuthCodeFlowCommand>(Arg.Is<CheckAuthCodeFlowOptions>(o =>
			o.Environment == "dev" && o.TimeOut == 30_000));
		ConsoleLogger.Instance.ClearMessages();
	}

	#region Fakes

	private sealed class FakeGetIdentityAssertionCommand : GetIdentityAssertionCommand {
		public FakeGetIdentityAssertionCommand()
			: base(Substitute.For<IApplicationClient>(), new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>()) { }

		public override int Execute(GetIdentityAssertionOptions options) => 0;
	}

	private sealed class FakeGetIdentityPublicJwkCommand : GetIdentityPublicJwkCommand {
		public FakeGetIdentityPublicJwkCommand()
			: base(Substitute.For<IApplicationClient>(), new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>()) { }

		public override int Execute(GetIdentityPublicJwkOptions options) => 0;
	}

	private sealed class FakeRegenerateIdentitySigningKeyCommand : RegenerateIdentitySigningKeyCommand {
		public FakeRegenerateIdentitySigningKeyCommand()
			: base(Substitute.For<IApplicationClient>(), new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>()) { }

		public override int Execute(RegenerateIdentitySigningKeyOptions options) => 0;
	}

	private sealed class FakeCheckAuthCodeFlowCommand : CheckAuthCodeFlowCommand {
		public FakeCheckAuthCodeFlowCommand()
			: base(Substitute.For<IApplicationClient>(), new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>()) { }

		public override int Execute(CheckAuthCodeFlowOptions options) => 0;
	}

	#endregion

}
