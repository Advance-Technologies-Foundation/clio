using System.Linq;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Requests;
using Clio.UserEnvironment;
using Clio.Utilities;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class RegWebAppToolTests {

	[Test]
	[Category("Unit")]
	[Description("Verifies that RegisterWebApp maps all supported args to RegAppOptions correctly, without is-net-core (now always auto-detected).")]
	public void RegisterWebApp_Should_Map_Args_To_RegAppOptions() {
		ConsoleLogger.Instance.ClearMessages();
		FakeRegAppCommand command = new();
		RegWebAppTool tool = new(command, ConsoleLogger.Instance);

		CommandExecutionResult result = tool.RegisterWebApp(new RegWebAppArgs(
			EnvironmentName: "docker_fix2",
			Uri: "http://k-krylov-nb.tscrm.com:40071",
			Login: "Supervisor",
			Password: "Supervisor",
			Maintainer: "Customer",
			CheckLogin: true,
			ActiveEnvironment: null,
			AddFromIis: false,
			Host: null,
			DeveloperModeEnabled: true,
			Safe: false,
			ClientId: "client-id",
			ClientSecret: "client-secret",
			AuthAppUri: "http://auth-app",
			WorkspacePaths: @"C:\Projects\clio-with-core-and-ui\workspace",
			EnvironmentPath: @"C:\Creatio"));

		result.ExitCode.Should().Be(0, "command should succeed when environment-name is provided");
		command.CapturedOptions.Should().NotBeNull(because: "the tool should forward options to the command");
		command.CapturedOptions.EnvironmentName.Should().Be("docker_fix2", because: "environment name should be forwarded");
		command.CapturedOptions.Uri.Should().Be("http://k-krylov-nb.tscrm.com:40071", because: "URI should be forwarded");
		command.CapturedOptions.Login.Should().Be("Supervisor", because: "login should be forwarded");
		command.CapturedOptions.Password.Should().Be("Supervisor", because: "password should be forwarded");
		command.CapturedOptions.Maintainer.Should().Be("Customer", because: "maintainer should be forwarded");
		command.CapturedOptions.CheckLogin.Should().BeTrue(because: "check-login flag should be forwarded");
		command.CapturedOptions.IsNetCore.Should().BeNull(because: "is-net-core is no longer a user-facing option and should not be set by the tool");
		command.CapturedOptions.DevMode.Should().Be(bool.TrueString, because: "developer-mode-enabled should be forwarded");
		command.CapturedOptions.Safe.Should().Be(bool.FalseString, because: "safe flag should be forwarded");
		command.CapturedOptions.ClientId.Should().Be("client-id", because: "OAuth client-id should be forwarded");
		command.CapturedOptions.ClientSecret.Should().Be("client-secret", because: "OAuth client-secret should be forwarded");
		command.CapturedOptions.AuthAppUri.Should().Be("http://auth-app", because: "OAuth auth-app-uri should be forwarded");
		command.CapturedOptions.WorkspacePathes.Should().Be(@"C:\Projects\clio-with-core-and-ui\workspace", because: "workspace-paths should be forwarded");
		command.CapturedOptions.EnvironmentPath.Should().Be(@"C:\Creatio", because: "environment-path should be forwarded");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	public void RegisterWebApp_Should_Return_Error_When_No_Mode_Is_Selected() {
		ConsoleLogger.Instance.ClearMessages();
		FakeRegAppCommand command = new();
		RegWebAppTool tool = new(command, ConsoleLogger.Instance);

		CommandExecutionResult result = tool.RegisterWebApp(new RegWebAppArgs());

		result.ExitCode.Should().Be(1);
		command.CapturedOptions.Should().BeNull();
		result.Output.Should().ContainSingle();
		result.Output.Single().Value.Should().BeOfType<string>()
			.Which.Should().Contain("Provide `environment-name`");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeRegAppCommand : RegAppCommand {
		public RegAppOptions CapturedOptions { get; private set; }

		public FakeRegAppCommand()
			: base(
				Substitute.For<ISettingsRepository>(),
				Substitute.For<IApplicationClientFactory>(),
				Substitute.For<IPowerShellFactory>(),
				Substitute.For<ILogger>()) {
		}

		public override int Execute(RegAppOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
