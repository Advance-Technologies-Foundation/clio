using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Command.ProcessModel;
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
	[Category("Unit")]
	[Description("modify-business-process emits the deterministic compile-not-required note after a successful edit, so an agent does not mistake 'edited' for 'must be compiled to run' and force compile-creatio (ENG-95706).")]
	public void ModifyBusinessProcess_Should_Emit_CompileNotRequiredNote_On_Success() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeModifyBusinessProcessCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ModifyBusinessProcessCommand>(Arg.Any<ModifyBusinessProcessOptions>())
			.Returns(resolvedCommand);
		ModifyBusinessProcessTool tool = new(new FakeModifyBusinessProcessCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(
			new ModifyBusinessProcessArgs("docker_fix2", SampleOperations, "UsrSampleProcess", null));

		// Assert
		result.ExitCode.Should().Be(0, because: "the fake command reports a successful edit");
		result.Note.Should().Be(CommandExecutionResult.CompileNotRequiredNote,
			because: "a clio-edited process stays interpreted and needs no compile; the response note is the one channel the agent cannot skip (ENG-95706)");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("modify-business-process suppresses the compile-not-required note when the edit FAILS — a failed mutation must not be told 'no compile needed' (ENG-95706).")]
	public void ModifyBusinessProcess_Should_Not_Emit_CompileNotRequiredNote_On_Failure() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeModifyBusinessProcessCommand resolvedCommand = new(exitCode: 1);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ModifyBusinessProcessCommand>(Arg.Any<ModifyBusinessProcessOptions>())
			.Returns(resolvedCommand);
		ModifyBusinessProcessTool tool = new(new FakeModifyBusinessProcessCommand(exitCode: 1), ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(
			new ModifyBusinessProcessArgs("docker_fix2", SampleOperations, "UsrSampleProcess", null));

		// Assert
		result.ExitCode.Should().NotBe(0, because: "the fake command reports a failed edit");
		result.Note.Should().NotBe(CommandExecutionResult.CompileNotRequiredNote,
			because: "a success-only signal must not ride a failed mutation");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Forwards an addElement operation that adds a sendEmail element with its full email block verbatim — the tool is an opaque pass-through, so the new element type and every email field (mode, sender, To recipients, subject, HTML body, importance, ignoreErrors, manual-mode performer) ride through to the command without modification.")]
	[Category("Unit")]
	public void ModifyBusinessProcess_Should_Forward_SendEmail_AddElement_Verbatim() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		const string sendEmailOps =
			"[{\"op\":\"addElement\",\"element\":{\"name\":\"SendEmail1\",\"type\":\"sendEmail\","
			+ "\"email\":{\"mode\":\"manual\",\"sender\":\"sales@example.com\",\"subject\":\"Order update\","
			+ "\"body\":\"<p>Hello</p>\",\"bodyFormat\":\"html\",\"to\":[{\"value\":\"to@example.com\"}],"
			+ "\"importance\":\"high\",\"ignoreErrors\":true,"
			+ "\"performer\":{\"type\":\"role\",\"role\":\"All employees\",\"showPage\":true}}}}]";
		FakeModifyBusinessProcessCommand defaultCommand = new();
		FakeModifyBusinessProcessCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ModifyBusinessProcessCommand>(Arg.Any<ModifyBusinessProcessOptions>())
			.Returns(resolvedCommand);
		ModifyBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(
			new ModifyBusinessProcessArgs("docker_fix2", sendEmailOps, "UsrSampleProcess", null));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a valid sendEmail addElement operation must be forwarded for the requested environment");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the forwarded operations");
		resolvedCommand.CapturedOptions!.OperationsJson.Should().Be(sendEmailOps,
			because: "the sendEmail addElement operation and its whole email block must pass through unchanged (opaque pass-through)");
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
		private readonly int _exitCode;

		public ModifyBusinessProcessOptions? CapturedOptions { get; private set; }

		public FakeModifyBusinessProcessCommand(int exitCode = 0)
			: base(Substitute.For<IModifyBusinessProcessService>(), Substitute.For<IProcessDescriber>(),
				Substitute.For<ILogger>()) {
			_exitCode = exitCode;
		}

		public override int Execute(ModifyBusinessProcessOptions options) {
			CapturedOptions = options;
			return _exitCode;
		}
	}
}
