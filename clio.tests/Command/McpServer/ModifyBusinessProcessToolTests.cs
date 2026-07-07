using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class ModifyBusinessProcessToolTests {
	private const string SampleOperations =
		"[{\"op\":\"removeElement\",\"elementName\":\"StartEvent1\"}]";

	[Test]
	[Description("Resolves the modify-business-process MCP tool for the requested environment and forwards the identity and operations into command options.")]
	[Category("Unit")]
	public void ModifyBusinessProcess_Should_Resolve_Command_And_Forward_Operations() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeModifyBusinessProcessCommand defaultCommand = new();
		FakeModifyBusinessProcessCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ModifyBusinessProcessCommand>(Arg.Any<ModifyBusinessProcessOptions>())
			.Returns(resolvedCommand);
		ModifyBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(
			new ModifyBusinessProcessArgs("docker_fix2", SampleOperations, "UsrSampleProcess", null));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the modify-business-process tool should forward a valid command payload for the requested environment");
		commandResolver.Received(1).Resolve<ModifyBusinessProcessCommand>(Arg.Is<ModifyBusinessProcessOptions>(options =>
			options.Environment == "docker_fix2" &&
			options.ProcessName == "UsrSampleProcess" &&
			options.OperationsJson == SampleOperations));
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-aware tool path should use the resolved command instance, not the startup one");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the forwarded modify-business-process options");
		resolvedCommand.CapturedOptions!.OperationsJson.Should().Be(SampleOperations,
			because: "the inline operations must be carried through to the command without modification");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a failed result without resolving any command when the environment name is empty.")]
	[Category("Unit")]
	public void ModifyBusinessProcess_Should_Fail_When_Environment_Is_Empty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeModifyBusinessProcessCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ModifyBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(
			new ModifyBusinessProcessArgs("   ", SampleOperations, "UsrSampleProcess", null));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "an empty environment name is a validation error that must not reach command resolution");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<ModifyBusinessProcessCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a failed result without resolving any command when no process identity is provided.")]
	[Category("Unit")]
	public void ModifyBusinessProcess_Should_Fail_When_No_Identity_Provided() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeModifyBusinessProcessCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ModifyBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(
			new ModifyBusinessProcessArgs("docker_fix2", SampleOperations, null, null));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "a missing process identity is a validation error that must not reach command resolution");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<ModifyBusinessProcessCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a failed result without resolving any command when both a process name and uid are provided.")]
	[Category("Unit")]
	public void ModifyBusinessProcess_Should_Fail_When_Both_Name_And_Uid_Provided() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeModifyBusinessProcessCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ModifyBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(new ModifyBusinessProcessArgs(
			"docker_fix2", SampleOperations, "UsrSampleProcess", "5c58c4c4-134b-4744-9c67-96d9c69c9d55"));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "an ambiguous identity (both name and uid) is a validation error that must not reach command resolution");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<ModifyBusinessProcessCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Returns a failed result without resolving any command when the operations are empty.")]
	[Category("Unit")]
	public void ModifyBusinessProcess_Should_Fail_When_Operations_Are_Empty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeModifyBusinessProcessCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ModifyBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(
			new ModifyBusinessProcessArgs("docker_fix2", "   ", "UsrSampleProcess", null));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "empty operations is a validation error that must not reach command resolution");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<ModifyBusinessProcessCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeModifyBusinessProcessCommand : ModifyBusinessProcessCommand {
		public ModifyBusinessProcessOptions? CapturedOptions { get; private set; }

		public FakeModifyBusinessProcessCommand()
			: base(Substitute.For<IModifyBusinessProcessService>(), Substitute.For<ILogger>()) {
		}

		public override int Execute(ModifyBusinessProcessOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
