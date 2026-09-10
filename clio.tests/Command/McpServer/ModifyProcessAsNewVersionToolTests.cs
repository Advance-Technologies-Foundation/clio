using System;
using System.Linq;
using System.Reflection;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Covers the <c>modify-business-process-as-new-version</c> MCP surface: argument validation, the opaque
/// pass-through of the shared operations vocabulary, and the metadata flags — of which
/// <c>Destructive = false</c> is a substantive claim about the operation rather than a formality.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public class ModifyProcessAsNewVersionToolTests {
	private const string SampleOperations =
		"[{\"op\":\"removeElement\",\"elementName\":\"StartEvent1\"}]";

	[Test]
	[Category("Unit")]
	[Description("Resolves the command for the requested environment and forwards the source identity, the target package and the operations into command options.")]
	public void ModifyProcessAsNewVersion_Should_Resolve_Command_And_Forward_Everything() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCommand defaultCommand = new();
		FakeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ModifyProcessAsNewVersionCommand>(Arg.Any<ModifyProcessAsNewVersionOptions>())
			.Returns(resolvedCommand);
		ModifyProcessAsNewVersionTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyProcessAsNewVersion(new ModifyProcessAsNewVersionArgs(
			"docker_fix2", "UsrSampleProcess", null, SampleOperations, "UsrAntonTest"));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "a complete, unambiguous request must reach the command rather than be refused here");
		commandResolver.Received(1).Resolve<ModifyProcessAsNewVersionCommand>(
			Arg.Is<ModifyProcessAsNewVersionOptions>(options =>
				options.Environment == "docker_fix2" &&
				options.ProcessName == "UsrSampleProcess" &&
				options.PackageName == "UsrAntonTest" &&
				options.OperationsJson == SampleOperations));
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the environment-aware path must use the RESOLVED command instance, not the startup one");
		resolvedCommand.CapturedOptions!.OperationsJson.Should().Be(SampleOperations,
			because: "the operations vocabulary is shared with modify-business-process and the tool is an opaque "
				+ "pass-through — reshaping it here would make one payload mean two different things");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("An OMITTED operations array is accepted and forwarded as empty, because a version with no edits is a plain snapshot of the source. modify-business-process refuses the same input; copying that refusal would remove the snapshot gesture entirely.")]
	public void ModifyProcessAsNewVersion_Should_Accept_NoOperations_AsASnapshot() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ModifyProcessAsNewVersionCommand>(Arg.Any<ModifyProcessAsNewVersionOptions>())
			.Returns(resolvedCommand);
		ModifyProcessAsNewVersionTool tool = new(new FakeCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyProcessAsNewVersion(
			new ModifyProcessAsNewVersionArgs("docker_fix2", "UsrSampleProcess"));

		// Assert
		result.ExitCode.Should().Be(0, because: "an edit-free version is a legal request, not a validation error");
		resolvedCommand.CapturedOptions!.OperationsJson.Should().BeEmpty(
			because: "nothing is invented to stand in for the missing array - the service turns absence into []");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("The package name is optional: omitting it leaves the choice to the platform, which picks the source's package when the caller may edit it and the design package otherwise.")]
	public void ModifyProcessAsNewVersion_Should_Accept_NoPackageName() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ModifyProcessAsNewVersionCommand>(Arg.Any<ModifyProcessAsNewVersionOptions>())
			.Returns(resolvedCommand);
		ModifyProcessAsNewVersionTool tool = new(new FakeCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyProcessAsNewVersion(new ModifyProcessAsNewVersionArgs(
			"docker_fix2", "UsrSampleProcess", null, SampleOperations));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the package is optional and its absence is not a validation error");
		resolvedCommand.CapturedOptions!.PackageName.Should().BeEmpty(
			because: "an empty package must stay empty, so the server sees absence and picks for itself");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Emits the deterministic compile-not-required note on success, so an agent does not read 'version saved' as 'must be compiled to run' and force compile-creatio (ENG-95706).")]
	public void ModifyProcessAsNewVersion_Should_Emit_CompileNotRequiredNote_On_Success() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ModifyProcessAsNewVersionCommand>(Arg.Any<ModifyProcessAsNewVersionOptions>())
			.Returns(resolvedCommand);
		ModifyProcessAsNewVersionTool tool = new(new FakeCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyProcessAsNewVersion(new ModifyProcessAsNewVersionArgs(
			"docker_fix2", "UsrSampleProcess", null, SampleOperations));

		// Assert
		result.Note.Should().Be(CommandExecutionResult.CompileNotRequiredNote,
			because: "the note is the one channel an agent cannot skip past on the way to a wrong compile");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Suppresses the compile-not-required note when the save FAILS — a success-only signal must not ride a failed operation (ENG-95706).")]
	public void ModifyProcessAsNewVersion_Should_Not_Emit_CompileNotRequiredNote_On_Failure() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCommand resolvedCommand = new(exitCode: 1);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ModifyProcessAsNewVersionCommand>(Arg.Any<ModifyProcessAsNewVersionOptions>())
			.Returns(resolvedCommand);
		ModifyProcessAsNewVersionTool tool = new(new FakeCommand(exitCode: 1), ConsoleLogger.Instance,
			commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyProcessAsNewVersion(new ModifyProcessAsNewVersionArgs(
			"docker_fix2", "UsrSampleProcess", null, SampleOperations));

		// Assert
		result.ExitCode.Should().NotBe(0,
			because: "the fake command reports a failed save");
		result.Note.Should().NotBe(CommandExecutionResult.CompileNotRequiredNote,
			because: "a success-only signal must not ride a failed operation");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Fails without resolving any command when the environment name is empty.")]
	public void ModifyProcessAsNewVersion_Should_Fail_When_Environment_Is_Empty() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ModifyProcessAsNewVersionTool tool = new(new FakeCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyProcessAsNewVersion(new ModifyProcessAsNewVersionArgs(
			"   ", "UsrSampleProcess", null, SampleOperations));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "an empty environment is a validation error that must not reach command resolution");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<ModifyProcessAsNewVersionCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Fails naming the violation when NEITHER a source name nor a uid is given, and resolves no command.")]
	public void ModifyProcessAsNewVersion_Should_Fail_When_No_Identity_Provided() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ModifyProcessAsNewVersionTool tool = new(new FakeCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyProcessAsNewVersion(
			new ModifyProcessAsNewVersionArgs("docker_fix2", null, null, SampleOperations));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "a missing source identity is a validation error that must not reach command resolution");
		result.Output.Select(entry => entry.Value?.ToString() ?? string.Empty).Should()
			.Contain(entry => entry.Contains("process-name") && entry.Contains("process-uid"),
				because: "the caller has to be told WHICH rule was broken, not just that something was wrong");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<ModifyProcessAsNewVersionCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Fails naming the violation when BOTH a source name and a uid are given, and resolves no command.")]
	public void ModifyProcessAsNewVersion_Should_Fail_When_Both_Name_And_Uid_Provided() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ModifyProcessAsNewVersionTool tool = new(new FakeCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyProcessAsNewVersion(new ModifyProcessAsNewVersionArgs(
			"docker_fix2", "UsrSampleProcess", "5c58c4c4-134b-4744-9c67-96d9c69c9d55", SampleOperations));

		// Assert
		result.ExitCode.Should().Be(-1,
			because: "an ambiguous identity is a validation error that must not reach command resolution");
		result.Output.Select(entry => entry.Value?.ToString() ?? string.Empty).Should()
			.Contain(entry => entry.Contains("not both"),
				because: "an ambiguous identity must be named as such rather than silently resolved one way");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<ModifyProcessAsNewVersionCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("The metadata flags are pinned, and Destructive = false is the substantive one: this operation never opens, saves or activates the SOURCE, and what the environment executes is unchanged when it returns. Marking it destructive would make a consent-gated caller refuse the one edit path that is safe by construction.")]
	public void ModifyProcessAsNewVersion_ShouldDeclare_NonDestructiveWriteFlags() {
		// Arrange
		MethodInfo method = typeof(ModifyProcessAsNewVersionTool)
			.GetMethod(nameof(ModifyProcessAsNewVersionTool.ModifyProcessAsNewVersion))!;

		// Act
		McpServerToolAttribute attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;

		// Assert
		attribute.Name.Should().Be("modify-business-process-as-new-version",
			because: "the wire name is what agents and the guidance article both spell out");
		attribute.ReadOnly.Should().BeFalse(because: "it writes: a new schema is created on the environment");
		attribute.Destructive.Should().BeFalse(
			because: "nothing existing is changed or removed — every write lands on a clone, and the source keeps "
				+ "running untouched");
		attribute.Idempotent.Should().BeFalse(
			because: "calling it twice creates a SECOND version, and versions can never be deleted");
		attribute.OpenWorld.Should().BeFalse(because: "it talks to one named, registered environment");
	}

	[Test]
	[Category("Unit")]
	[Description("The description states the two things an agent gets wrong on its own: that the product calls this 'Save new version', and that the source keeps running until something activates the new one. Both are contract, not colour — an agent that assumes the edit is live stops one step early.")]
	public void ModifyProcessAsNewVersion_Description_ShouldStateTheProductGestureAndTheInactiveOutcome() {
		// Arrange
		MethodInfo method = typeof(ModifyProcessAsNewVersionTool)
			.GetMethod(nameof(ModifyProcessAsNewVersionTool.ModifyProcessAsNewVersion))!;

		// Act
		string description = method.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;

		// Assert
		description.Should().Contain("Save new version",
			because: "the agent has to be able to map the call onto what the user sees in the designer");
		description.Should().Contain("INACTIVE",
			because: "an agent that assumes the edit is live reports the work as done one step early");
		description.Should().Contain("set-active-business-process-version",
			because: "activation is a separate, explicit step and the description is where that sequence is stated");
		description.Should().Contain("never be DELETED",
			because: "a version is permanent, and an agent creating one speculatively cannot undo it");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool routes through the ProcessDesignService entry the package actually exposes, with the leading slash its five siblings carry — a route that resolves to the wrong path fails at run time on a stand, which is the slowest possible place to find it.")]
	public void ModifyProcessAsNewVersion_ShouldResolve_TheProcessDesignServiceRoute() {
		// Arrange
		ServiceUrlBuilder.KnownRoute route = ServiceUrlBuilder.KnownRoute.ModifyProcessAsNewVersion;

		// Act
		string path = ServiceUrlBuilder.KnownRoutes[route];

		// Assert
		path.Should().Be("/rest/ProcessDesignService/ModifyProcessAsNewVersion",
			because: "the leading slash is what the five sibling ProcessDesignService routes carry, and a wrong "
				+ "path fails only on a stand");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool carries no [FeatureToggle], so it ships enabled like its process-designer siblings. Pinned HERE rather than by joining ProcessDesignerGoLiveTests' GoLiveToolTypes: that set records what shipped at the ENG-96132 go-live and its own description counts five members, so adding a tool that ships later would both falsify the count and backdate a GA claim.")]
	public void ModifyProcessAsNewVersion_ShouldNotBeFeatureGated() {
		// Arrange
		Type toolType = typeof(ModifyProcessAsNewVersionTool);

		// Act
		object[] toggles = toolType.GetCustomAttributes(typeof(FeatureToggleAttribute), inherit: true);

		// Assert
		toggles.Should().BeEmpty(
			because: "a gated tool is filtered out of MCP registration entirely, so an accidental toggle would "
				+ "make the tool silently unreachable rather than fail anywhere");
	}

	private sealed class FakeCommand : ModifyProcessAsNewVersionCommand {
		private readonly int _exitCode;

		public ModifyProcessAsNewVersionOptions? CapturedOptions { get; private set; }

		public FakeCommand(int exitCode = 0)
			: base(Substitute.For<IModifyProcessAsNewVersionService>(), Substitute.For<ILogger>()) {
			_exitCode = exitCode;
		}

		public override int Execute(ModifyProcessAsNewVersionOptions options) {
			CapturedOptions = options;
			return _exitCode;
		}
	}
}
