using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Command.ProcessModel;
using Clio.Common;
using System.Linq;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class ModifyBusinessProcessToolTests {
	private const string SampleOperations =
		"[{\"op\":\"removeElement\",\"elementName\":\"StartEvent1\"}]";

	[Test]
	[Category("Unit")]
	[Description("Pins the destructive classification of modify-business-process. This annotation - not the description prose - is what an MCP host reads to decide whether a call needs human approval, so a silent flip back to false would let a host auto-run it.")]
	public void ModifyBusinessProcess_Should_Be_Marked_As_Destructive() {
		// Arrange
		System.Reflection.MethodInfo method = typeof(ModifyBusinessProcessTool).GetMethod(nameof(ModifyBusinessProcessTool.ModifyBusinessProcess))!;
		McpServerToolAttribute attribute = method
			.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false)
			.Cast<McpServerToolAttribute>()
			.Single();

		// Act
		bool destructive = attribute.Destructive;

		// Assert
		destructive.Should().BeTrue(because: "modify-business-process edits an existing process in place and can revoke record permissions through an accessRights block");
	}

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
	[Description("Forwards a setElement operation carrying a changeData block verbatim — the tool is an opaque pass-through, so the target source, every value-source kind (constant, processParameter, sourceElement pair, expression) and the whole block ride through to the command unmodified. Guards the one thing this repo CAN assert about the changeData contract: that clio does not reshape it on the way to the server.")]
	[Category("Unit")]
	public void ModifyBusinessProcess_Should_Forward_ChangeData_SetElement_Verbatim() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		const string changeDataOps =
			"[{\"op\":\"setElement\",\"elementName\":\"UpdateContact\",\"elementUpdate\":{\"changeData\":{"
			+ "\"source\":\"Contact\",\"values\":["
			+ "{\"column\":\"JobTitle\",\"value\":\"Manager\"},"
			+ "{\"column\":\"Notes\",\"processParameter\":\"NoteTextParameter\"},"
			+ "{\"column\":\"AccountId\",\"sourceElement\":\"RecordModifiedSignal\","
			+ "\"sourceElementParameter\":\"RecordId\"},"
			+ "{\"column\":\"DueDate\",\"expression\":\"[#DateValue.2026-09-01#]\"}]}}}]";
		FakeModifyBusinessProcessCommand defaultCommand = new();
		FakeModifyBusinessProcessCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ModifyBusinessProcessCommand>(Arg.Any<ModifyBusinessProcessOptions>())
			.Returns(resolvedCommand);
		ModifyBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(
			new ModifyBusinessProcessArgs("docker_fix2", changeDataOps, "UsrSampleProcess", null));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a valid changeData setElement operation must be forwarded for the requested environment");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the forwarded operations");
		resolvedCommand.CapturedOptions!.OperationsJson.Should().Be(changeDataOps,
			because: "the changeData block and every value-source kind in it must pass through unchanged — the "
				+ "server owns the semantics, so any reshaping here would silently alter what the caller asked for");
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

	[Test]
	[Description("Forwards a setElement operation carrying an accessRights block verbatim — the tool is an opaque pass-through, so a replaced add collection, a remove collection cleared with an empty array, and the object retarget that clears the stored record filter all ride through unchanged, together with the setFilter re-issued in the same array.")]
	[Category("Unit")]
	public void ModifyBusinessProcess_Should_Forward_AccessRights_SetElement_Verbatim() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		const string accessRightsOps =
			"[{\"op\":\"setElement\",\"elementName\":\"GrantRights\",\"elementUpdate\":{\"accessRights\":{"
			+ "\"object\":\"Contact\",\"considerTimeInFilter\":false,"
			+ "\"add\":[{\"operations\":[\"read\"],\"level\":\"permit\","
			+ "\"grantee\":{\"type\":\"role\",\"role\":\"System administrators\"}}],"
			+ "\"remove\":[]}}},"
			+ "{\"op\":\"setFilter\",\"elementName\":\"GrantRights\",\"filter\":{\"object\":\"Contact\","
			+ "\"conditions\":[{\"column\":\"Id\",\"comparison\":\"equal\",\"processParameter\":\"ContactId\"}]}}]";
		FakeModifyBusinessProcessCommand defaultCommand = new();
		FakeModifyBusinessProcessCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ModifyBusinessProcessCommand>(Arg.Any<ModifyBusinessProcessOptions>())
			.Returns(resolvedCommand);
		ModifyBusinessProcessTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(
			new ModifyBusinessProcessArgs("docker_fix2", accessRightsOps, "UsrSampleProcess", null));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a valid accessRights setElement operation must be forwarded for the requested environment");
		resolvedCommand.CapturedOptions.Should().NotBeNull(
			because: "the resolved command should receive the forwarded operations");
		resolvedCommand.CapturedOptions!.OperationsJson.Should().Be(accessRightsOps,
			because: "replace-and-clear semantics live in the exact arrays the caller sent — an empty "
				+ "remove array means CLEAR, so dropping or rewriting it here would silently keep permissions "
				+ "the caller asked to stop revoking, and the paired setFilter must survive in the same order");
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
