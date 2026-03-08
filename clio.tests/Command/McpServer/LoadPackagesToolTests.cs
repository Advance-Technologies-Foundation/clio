using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public class LoadPackagesToolTests {

	[Test]
	[Category("Unit")]
	public void LoadPackagesToFileSystem_Should_Resolve_Command_For_Requested_Environment() {
		ConsoleLogger.Instance.ClearMessages();
		FakeLoadPackagesToFileSystemCommand defaultCommand = new();
		FakeLoadPackagesToFileSystemCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<LoadPackagesToFileSystemCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(resolvedCommand);
		LoadPackagesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		CommandExecutionResult result = tool.LoadPackagesToFileSystem("docker_fix2");

		result.ExitCode.Should().Be(0);
		commandResolver.Received(1).Resolve<LoadPackagesToFileSystemCommand>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "docker_fix2"));
		defaultCommand.CapturedOptions.Should().BeNull();
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions.Environment.Should().Be("docker_fix2");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	public void LoadPackagesToDb_Should_Resolve_Command_For_Requested_Environment() {
		ConsoleLogger.Instance.ClearMessages();
		FakeLoadPackagesToFileSystemCommand defaultCommand = new();
		FakeLoadPackagesToDbCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<LoadPackagesToDbCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(resolvedCommand);
		LoadPackagesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		CommandExecutionResult result = tool.LoadPackagesToDb("docker_fix2");

		result.ExitCode.Should().Be(0);
		commandResolver.Received(1).Resolve<LoadPackagesToDbCommand>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "docker_fix2"));
		defaultCommand.CapturedOptions.Should().BeNull();
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions.Environment.Should().Be("docker_fix2");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeLoadPackagesToFileSystemCommand : LoadPackagesToFileSystemCommand {
		public EnvironmentOptions CapturedOptions { get; private set; }

		public FakeLoadPackagesToFileSystemCommand()
			: base(
				Substitute.For<IFileDesignModePackages>(),
				Substitute.For<ILogger>()) {
		}

		public override int Execute(EnvironmentOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}

	private sealed class FakeLoadPackagesToDbCommand : LoadPackagesToDbCommand {
		public EnvironmentOptions CapturedOptions { get; private set; }

		public FakeLoadPackagesToDbCommand()
			: base(
				Substitute.For<IFileDesignModePackages>(),
				Substitute.For<ILogger>()) {
		}

		public override int Execute(EnvironmentOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
