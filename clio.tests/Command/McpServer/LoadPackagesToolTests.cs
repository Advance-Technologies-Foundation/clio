using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class LoadPackagesToolTests {

	[Test]
	[Category("Unit")]
	[Description("target='file-system' dispatches to the LoadPackagesToFileSystemCommand for the requested environment.")]
	public void PkgMode_TargetFileSystem_Should_Resolve_Command_For_Requested_Environment() {
		ConsoleLogger.Instance.ClearMessages();
		FakeLoadPackagesToFileSystemCommand defaultCommand = new();
		FakeLoadPackagesToFileSystemCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<LoadPackagesToFileSystemCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(resolvedCommand);
		LoadPackagesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		CommandExecutionResult result = tool.Apply(new PkgModeRunArgs(LoadPackagesTool.TargetFileSystem, "docker_fix2"));

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
	[Description("target='db' dispatches to the LoadPackagesToDbCommand for the requested environment.")]
	public void PkgMode_TargetDb_Should_Resolve_Command_For_Requested_Environment() {
		ConsoleLogger.Instance.ClearMessages();
		FakeLoadPackagesToFileSystemCommand defaultCommand = new();
		FakeLoadPackagesToDbCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<LoadPackagesToDbCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(resolvedCommand);
		LoadPackagesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		CommandExecutionResult result = tool.Apply(new PkgModeRunArgs(LoadPackagesTool.TargetDb, "docker_fix2"));

		result.ExitCode.Should().Be(0);
		commandResolver.Received(1).Resolve<LoadPackagesToDbCommand>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "docker_fix2"));
		defaultCommand.CapturedOptions.Should().BeNull();
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions.Environment.Should().Be("docker_fix2");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects an unknown target value with a clear listing of allowed values.")]
	public void PkgMode_Should_Reject_Invalid_Target() {
		ConsoleLogger.Instance.ClearMessages();
		FakeLoadPackagesToFileSystemCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		LoadPackagesTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		CommandExecutionResult result = tool.Apply(new PkgModeRunArgs("bogus", "docker_fix2"));

		result.ExitCode.Should().Be(-1);
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<LoadPackagesToFileSystemCommand>(default!);
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
