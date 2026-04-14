using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.UserEnvironment;
using Clio.Utilities;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class RegWebAppToolAutoDetectionTests {
	[Test]
	[Description("Leaves IsNetCore unset when the MCP caller omits is-net-core so reg-web-app can auto-detect the runtime.")]
	public void RegisterWebApp_Should_Leave_IsNetCore_Null_When_Override_Is_Omitted() {
		FakeRegAppCommand command = new();
		RegWebAppTool tool = new(command, ConsoleLogger.Instance);

		CommandExecutionResult result = tool.RegisterWebApp(new RegWebAppArgs(
			EnvironmentName: "sandbox",
			Uri: "http://example.invalid",
			Login: "Supervisor",
			Password: "Supervisor"));

		result.ExitCode.Should().Be(0,
			because: "the MCP tool should still execute normally when the runtime override is omitted");
		command.CapturedOptions.Should().NotBeNull(
			because: "the MCP tool should forward the request to reg-web-app");
		command.CapturedOptions!.IsNetCore.Should().BeNull(
			because: "omitting is-net-core should preserve the auto-detect path instead of forcing false");
	}

	private sealed class FakeRegAppCommand : RegAppCommand {
		public FakeRegAppCommand()
			: base(
				Substitute.For<ISettingsRepository>(),
				Substitute.For<IApplicationClientFactory>(),
				Substitute.For<IPowerShellFactory>(),
				Substitute.For<ILogger>()) {
		}

		public RegAppOptions? CapturedOptions { get; private set; }

		public override int Execute(RegAppOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
