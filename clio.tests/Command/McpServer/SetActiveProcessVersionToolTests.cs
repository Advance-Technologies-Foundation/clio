using System;
using System.Linq;
using System.Reflection;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Covers the <c>set-active-business-process-version</c> MCP surface: argument validation, the metadata flags,
/// and the two contract statements the description has to carry — that activation reaches new instances only,
/// and that no version can ever be deleted.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public class SetActiveProcessVersionToolTests {

	[Test]
	[Category("Unit")]
	[Description("Resolves the command for the requested environment and forwards the version identity into command options.")]
	public void SetActiveProcessVersion_Should_Resolve_Command_And_Forward_Identity() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCommand defaultCommand = new();
		FakeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SetActiveProcessVersionCommand>(Arg.Any<SetActiveProcessVersionOptions>())
			.Returns(resolvedCommand);
		SetActiveProcessVersionTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.SetActiveProcessVersion(
			new SetActiveProcessVersionArgs("docker_fix2", "UsrSampleProcessCustom2"));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a complete, unambiguous request must reach the command rather than be refused here");
		commandResolver.Received(1).Resolve<SetActiveProcessVersionCommand>(
			Arg.Is<SetActiveProcessVersionOptions>(options =>
				options.Environment == "docker_fix2" && options.VersionName == "UsrSampleProcessCustom2"));
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-aware path must use the RESOLVED command instance, not the startup one");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Emits the deterministic compile-not-required note on success — activating a version puts the environment into no state that needs compiling, and an agent that assumes otherwise runs compile-creatio for nothing (ENG-95706).")]
	public void SetActiveProcessVersion_Should_Emit_CompileNotRequiredNote_On_Success() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SetActiveProcessVersionCommand>(Arg.Any<SetActiveProcessVersionOptions>())
			.Returns(resolvedCommand);
		SetActiveProcessVersionTool tool = new(new FakeCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.SetActiveProcessVersion(
			new SetActiveProcessVersionArgs("docker_fix2", "UsrSampleProcessCustom2"));

		// Assert
		result.Note.Should().Be(CommandExecutionResult.CompileNotRequiredNote,
			because: "the note is the one channel an agent cannot skip past on the way to a wrong compile");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Suppresses the compile-not-required note when the activation FAILS — a success-only signal must not ride a failed operation (ENG-95706).")]
	public void SetActiveProcessVersion_Should_Not_Emit_CompileNotRequiredNote_On_Failure() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCommand resolvedCommand = new(exitCode: 1);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SetActiveProcessVersionCommand>(Arg.Any<SetActiveProcessVersionOptions>())
			.Returns(resolvedCommand);
		SetActiveProcessVersionTool tool = new(new FakeCommand(exitCode: 1), ConsoleLogger.Instance,
			commandResolver);

		// Act
		CommandExecutionResult result = tool.SetActiveProcessVersion(
			new SetActiveProcessVersionArgs("docker_fix2", "UsrSampleProcessCustom2"));

		// Assert
		result.ExitCode.Should().NotBe(0, because: "the fake command reports a failed activation");
		result.Note.Should().NotBe(CommandExecutionResult.CompileNotRequiredNote,
			because: "a success-only signal must not ride a failed operation");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Fails without resolving any command when the environment name is empty.")]
	public void SetActiveProcessVersion_Should_Fail_When_Environment_Is_Empty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SetActiveProcessVersionTool tool = new(new FakeCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.SetActiveProcessVersion(
			new SetActiveProcessVersionArgs("   ", "UsrSampleProcessCustom2"));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "an empty environment is a validation error that must not reach command resolution");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<SetActiveProcessVersionCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Fails naming the violation when NEITHER a version name nor a uid is given, and resolves no command.")]
	public void SetActiveProcessVersion_Should_Fail_When_No_Identity_Provided() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SetActiveProcessVersionTool tool = new(new FakeCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.SetActiveProcessVersion(
			new SetActiveProcessVersionArgs("docker_fix2"));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "a missing version identity is a validation error that must not reach command resolution");
		result.Output.Select(entry => entry.Value?.ToString() ?? string.Empty).Should()
			.Contain(entry => entry.Contains("version-name") && entry.Contains("version-uid"),
				because: "the caller has to be told WHICH rule was broken, not just that something was wrong");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<SetActiveProcessVersionCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Fails naming the violation when BOTH a version name and a uid are given, and resolves no command.")]
	public void SetActiveProcessVersion_Should_Fail_When_Both_Name_And_Uid_Provided() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SetActiveProcessVersionTool tool = new(new FakeCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.SetActiveProcessVersion(new SetActiveProcessVersionArgs(
			"docker_fix2", "UsrSampleProcessCustom2", "5c58c4c4-134b-4744-9c67-96d9c69c9d55"));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "an ambiguous identity is a validation error that must not reach command resolution");
		result.Output.Select(entry => entry.Value?.ToString() ?? string.Empty).Should()
			.Contain(entry => entry.Contains("not both"),
				because: "an ambiguous identity must be named as such rather than silently resolved one way");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<SetActiveProcessVersionCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("The metadata flags are pinned. Destructive = true because this changes what the whole environment executes — the highest-blast-radius write in the process-designer surface. Idempotent = true because activating the same version twice lands in the same state.")]
	public void SetActiveProcessVersion_ShouldDeclare_DestructiveIdempotentWriteFlags() {
		// Arrange
		MethodInfo method = typeof(SetActiveProcessVersionTool)
			.GetMethod(nameof(SetActiveProcessVersionTool.SetActiveProcessVersion))!;

		// Act
		McpServerToolAttribute attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;

		// Assert
		attribute.Name.Should().Be("set-active-business-process-version",
			because: "the wire name is what agents and the guidance article both spell out");
		attribute.ReadOnly.Should().BeFalse(because: "it writes the family's active flags");
		attribute.Destructive.Should().BeTrue(
			because: "it changes which graph the whole environment executes for every new instance — the "
				+ "opposite of its sibling, which only ever writes a clone");
		attribute.Idempotent.Should().BeTrue(
			because: "setting the same version active twice yields the same state, so a sequential re-run is safe");
		attribute.OpenWorld.Should().BeFalse(because: "it talks to one named, registered environment");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool is excluded from the 120 s read-response deadline. The gate is `!destructive && …`, so Destructive = true is what excludes it — and that matters: a deadline that abandoned this call would leave the family mid-switch with nobody reading it back.")]
	public void SetActiveProcessVersion_ShouldNotBeBoundedByTheReadResponseDeadline() {
		// Arrange — the flags come off the DECLARED attribute, not from this test. Supplied by hand they made
		// the assertion a statement about IsRetrySafe's arithmetic, which stayed green if the tool's own
		// Destructive flag were flipped — the one change that would actually put this write under the deadline.
		MethodInfo method = typeof(SetActiveProcessVersionTool)
			.GetMethod(nameof(SetActiveProcessVersionTool.SetActiveProcessVersion))!;
		McpServerToolAttribute declared = method.GetCustomAttribute<McpServerToolAttribute>()!;

		// Act
		bool retrySafe = McpReadDeadlineGate.IsRetrySafe(declared.Name!, declared.ReadOnly, declared.Destructive);

		// Assert
		retrySafe.Should().BeFalse(
			because: "the read deadline covers reads only and never a server write, so a destructive tool owns "
				+ "its own timeout contract instead of being abandoned mid-write");
	}

	[Test]
	[Category("Unit")]
	[Description("The description carries the four statements an agent cannot derive: activation reaches NEW instances only, running instances stay on their version, the UI word is 'actual', and no version can ever be deleted. Each of these is a wrong assumption an agent otherwise makes and then reports as fact.")]
	public void SetActiveProcessVersion_Description_ShouldStateWhatActivationDoesAndDoesNotDo() {
		// Arrange
		MethodInfo method = typeof(SetActiveProcessVersionTool)
			.GetMethod(nameof(SetActiveProcessVersionTool.SetActiveProcessVersion))!;

		// Act
		string description = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;

		// Assert
		description.Should().Contain("NEW process instances",
			because: "an agent that thinks activation migrates running instances will report a rollback as "
				+ "complete while the old graph is still executing");
		description.Should().Contain("already running stay",
			because: "the counterpart has to be stated too, or 'new instances' reads as an implementation detail");
		description.Should().Contain("ACTUAL",
			because: "the product's UI says actual where the platform's data says active, and the agent has to "
				+ "map what the user sees onto what it called");
		description.Should().Contain("deletes a version",
			because: "rollback means activating an earlier version, never removing the newer one — there is no "
				+ "operation anywhere that deletes one");
		description.Should().Contain("ASK FIRST",
			because: "the product's own designer asks before making a version actual, so an agent must not do it "
				+ "on its own initiative after creating one");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool carries no [FeatureToggle], so it ships enabled like its process-designer siblings. Pinned here rather than in ProcessDesignerGoLiveTests, whose set records the ENG-96132 go-live and counts five members.")]
	public void SetActiveProcessVersion_ShouldNotBeFeatureGated() {
		// Arrange
		Type toolType = typeof(SetActiveProcessVersionTool);

		// Act
		object[] toggles = toolType.GetCustomAttributes(typeof(FeatureToggleAttribute), inherit: true);

		// Assert
		toggles.Should().BeEmpty(
			because: "a gated tool is filtered out of MCP registration entirely, so an accidental toggle would "
				+ "make the tool silently unreachable rather than fail anywhere");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool routes through the ProcessDesignService entry the package exposes, with the leading slash its siblings carry — a wrong path fails only at run time on a stand.")]
	public void SetActiveProcessVersion_ShouldResolve_TheProcessDesignServiceRoute() {
		// Arrange
		ServiceUrlBuilder.KnownRoute route = ServiceUrlBuilder.KnownRoute.SetActiveProcessVersion;

		// Act
		string path = ServiceUrlBuilder.KnownRoutes[route];

		// Assert
		path.Should().Be("/rest/ProcessDesignService/SetActiveProcessVersion",
			because: "the leading slash is what the sibling ProcessDesignService routes carry, and a wrong path "
				+ "fails only on a stand");
	}

	private sealed class FakeCommand : SetActiveProcessVersionCommand {
		private readonly int _exitCode;

		public SetActiveProcessVersionOptions? CapturedOptions { get; private set; }

		public FakeCommand(int exitCode = 0)
			: base(Substitute.For<ISetActiveProcessVersionService>(), Substitute.For<ILogger>()) {
			_exitCode = exitCode;
		}

		public override int Execute(SetActiveProcessVersionOptions options) {
			CapturedOptions = options;
			return _exitCode;
		}
	}
}
