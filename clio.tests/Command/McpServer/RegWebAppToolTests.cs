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
public class RegWebAppToolTests {

	[Test]
	[Category("Unit")]
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
			IsNetCore: true,
			DeveloperModeEnabled: true,
			Safe: false,
			ClientId: "client-id",
			ClientSecret: "client-secret",
			AuthAppUri: "http://auth-app",
			WorkspacePaths: @"C:\Projects\clio-with-core-and-ui\workspace",
			EnvironmentPath: @"C:\Creatio"));

		result.ExitCode.Should().Be(0);
		command.CapturedOptions.Should().NotBeNull();
		command.CapturedOptions.EnvironmentName.Should().Be("docker_fix2");
		command.CapturedOptions.Uri.Should().Be("http://k-krylov-nb.tscrm.com:40071");
		command.CapturedOptions.Login.Should().Be("Supervisor");
		command.CapturedOptions.Password.Should().Be("Supervisor");
		command.CapturedOptions.Maintainer.Should().Be("Customer");
		command.CapturedOptions.CheckLogin.Should().BeTrue();
		command.CapturedOptions.IsNetCore.Should().BeTrue();
		command.CapturedOptions.DevMode.Should().Be(bool.TrueString);
		command.CapturedOptions.Safe.Should().Be(bool.FalseString);
		command.CapturedOptions.ClientId.Should().Be("client-id");
		command.CapturedOptions.ClientSecret.Should().Be("client-secret");
		command.CapturedOptions.AuthAppUri.Should().Be("http://auth-app");
		command.CapturedOptions.WorkspacePathes.Should().Be(@"C:\Projects\clio-with-core-and-ui\workspace");
		command.CapturedOptions.EnvironmentPath.Should().Be(@"C:\Creatio");
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
