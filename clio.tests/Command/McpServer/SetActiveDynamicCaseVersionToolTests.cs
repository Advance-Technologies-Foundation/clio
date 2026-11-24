using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
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

[TestFixture]
[Property("Module", "McpServer")]
public sealed class SetActiveDynamicCaseVersionToolTests {

	private FakeCommand _startup;
	private FakeCommand _resolved;
	private IToolCommandResolver _resolver;
	private SetActiveDynamicCaseVersionTool _tool;

	[SetUp]
	public void Setup() {
		ConsoleLogger.Instance.ClearMessages();
		_startup = new FakeCommand();
		_resolved = new FakeCommand();
		_resolver = Substitute.For<IToolCommandResolver>();
		_resolver.Resolve<SetActiveDynamicCaseVersionCommand>(Arg.Any<SetActiveDynamicCaseVersionOptions>())
			.Returns(_resolved);
		_tool = new SetActiveDynamicCaseVersionTool(_startup, ConsoleLogger.Instance, _resolver);
	}

	[TearDown]
	public void TearDown() {
		_resolver.ClearReceivedCalls();
		ConsoleLogger.Instance.ClearMessages();
	}

	private static SetActiveDynamicCaseVersionArgs Args(string json) =>
		JsonSerializer.Deserialize<SetActiveDynamicCaseVersionArgs>(json)!;

	private static string TextOf(CommandExecutionResult result) =>
		string.Join(" | ", result.Output.Select(message => message.Value?.ToString()));

	private static MethodInfo Method() =>
		typeof(SetActiveDynamicCaseVersionTool).GetMethod(
			nameof(SetActiveDynamicCaseVersionTool.SetActiveDynamicCaseVersion))!;

	private static string Description() =>
		Method().GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;

	private void ShouldNotHaveRun() {
		_resolver.DidNotReceiveWithAnyArgs().Resolve<SetActiveDynamicCaseVersionCommand>(default!);
		_resolved.Captured.Should().BeNull(because: "a refused call never reaches the command");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool is a destructive, idempotent, closed-world write named set-active-dynamic-case-version, kept out of the read-response deadline.")]
	public void SetActiveDynamicCaseVersion_ShouldDeclareDestructiveIdempotentWriteFlags() {
		// Arrange
		MethodInfo method = Method();

		// Act
		McpServerToolAttribute attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;

		// Assert
		attribute.Name.Should().Be("set-active-dynamic-case-version", because: "the guidance and agents spell this name");
		attribute.ReadOnly.Should().BeFalse(because: "it switches which member is active");
		attribute.Destructive.Should().BeTrue(
			because: "the switch changes the case the bar and every new record use");
		attribute.Idempotent.Should().BeTrue(because: "the same call again answers unchanged");
		attribute.OpenWorld.Should().BeFalse(because: "it talks to one registered environment");
		McpReadDeadlineGate.IsRetrySafe(attribute.Name!, attribute.ReadOnly, attribute.Destructive).Should().BeFalse(
			because: "a switch is never abandoned mid-way by the read-response deadline");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool runs in a per-call worker like the other process-designer writes.")]
	public void SetActiveDynamicCaseVersion_ShouldRunInAPerCallWorker() {
		// Arrange
		MethodInfo method = Method();

		// Act
		McpToolExecutionAttribute execution = method.GetCustomAttribute<McpToolExecutionAttribute>()!;

		// Assert
		execution.Location.Should().Be(McpToolExecutionLocation.Worker, because: "the design places it in a worker");
		execution.Lifetime.Should().Be(McpToolExecutionLifetime.PerCall, because: "each call gets its own worker");
	}

	[Test]
	[Category("Unit")]
	[Description("The options type declares the CrtProcessBuilder floor that carries the version operations, so an older package is refused before any call.")]
	public void Options_ShouldRequireTheProcessBuilderFloor() {
		// Arrange
		Type optionsType = typeof(SetActiveDynamicCaseVersionOptions);

		// Act
		RequiresPackageAttribute requirement = optionsType.GetCustomAttribute<RequiresPackageAttribute>()!;

		// Assert
		requirement.Name.Should().Be(BundledPackages.ProcessBuilderPackageName,
			because: "the operation lives in the bundled CrtProcessBuilder package");
		requirement.Version.Should().Be(DynamicCaseChangeRequirement.MinimumProcessBuilderVersion,
			because: "the floor of the case-change and version tools is written in one place");
	}

	[Test]
	[Category("Unit")]
	[Description("A call by version-name reaches the RESOLVED command, not the startup one, with the name trimmed, no UId and no dry run.")]
	public void SetActiveDynamicCaseVersion_ShouldMapAName_ToTheResolvedCommand() {
		// Arrange
		SetActiveDynamicCaseVersionArgs args =
			Args("{\"environment-name\":\"dev\",\"version-name\":\" UsrOrderCaseCustom1 \"}");

		// Act
		CommandExecutionResult result = _tool.SetActiveDynamicCaseVersion(args);

		// Assert
		result.ExitCode.Should().Be(0, because: "a named member is a valid call");
		_resolver.Received(1).Resolve<SetActiveDynamicCaseVersionCommand>(
			Arg.Is<SetActiveDynamicCaseVersionOptions>(options => options.Environment == "dev"));
		_startup.Captured.Should().BeNull(
			because: "an environment-sensitive tool runs the command resolved for the call's environment");
		SetActiveDynamicCaseVersionOptions options = _resolved.Captured!;
		options.Environment.Should().Be("dev", because: "the environment is forwarded");
		options.VersionName.Should().Be("UsrOrderCaseCustom1", because: "the member is forwarded trimmed");
		options.VersionUId.Should().BeNull(because: "the call named no UId");
		options.DryRun.Should().BeFalse(because: "a call writes unless it asks for a dry run");
	}

	[Test]
	[Category("Unit")]
	[Description("A call by version-uid with dry-run maps the UId trimmed and the dry run.")]
	public void SetActiveDynamicCaseVersion_ShouldMapAUIdAndTheDryRun() {
		// Arrange
		SetActiveDynamicCaseVersionArgs args = Args("{\"environment-name\":\"dev\","
			+ "\"version-uid\":\" 5c58c4c4-134b-4744-9c67-96d9c69c9d55 \",\"dry-run\":true}");

		// Act
		CommandExecutionResult result = _tool.SetActiveDynamicCaseVersion(args);

		// Assert
		result.ExitCode.Should().Be(0, because: "a member named by UId is a valid call");
		_resolved.Captured!.VersionUId.Should().Be("5c58c4c4-134b-4744-9c67-96d9c69c9d55",
			because: "the UId is forwarded trimmed");
		_resolved.Captured.VersionName.Should().BeNull(because: "the call named no schema name");
		_resolved.Captured.DryRun.Should().BeTrue(because: "dry-run asks for the planned switch only");
	}

	[Test]
	[Category("Unit")]
	[Description("Naming both version-name and version-uid is refused as invalid-request before the command runs.")]
	public void SetActiveDynamicCaseVersion_ShouldRefuseBothIdentities() {
		// Arrange
		SetActiveDynamicCaseVersionArgs args = Args("{\"environment-name\":\"dev\",\"version-name\":\"UsrOrderCase\","
			+ "\"version-uid\":\"5c58c4c4-134b-4744-9c67-96d9c69c9d55\"}");

		// Act
		CommandExecutionResult result = _tool.SetActiveDynamicCaseVersion(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "two identities could name two different members");
		TextOf(result).Should().Contain("only one of version-name or version-uid").And.Contain("[invalid-request]",
			because: "the conflict and the stable code are named");
		ShouldNotHaveRun();
	}

	[Test]
	[Category("Unit")]
	[Description("An undeclared top-level key is refused through the overflow bag, naming the key and the valid keys.")]
	public void SetActiveDynamicCaseVersion_ShouldRefuseAnUnknownArgument() {
		// Arrange
		SetActiveDynamicCaseVersionArgs args = Args("{\"environment-name\":\"dev\",\"case-name\":\"UsrOrderCase\"}");

		// Act
		CommandExecutionResult result = _tool.SetActiveDynamicCaseVersion(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "an unknown key is a caller error");
		TextOf(result).Should().Contain("'case-name'").And.Contain(SetActiveDynamicCaseVersionTool.ValidArgsHint,
			because: "the key and the valid set are named");
		ShouldNotHaveRun();
	}

	[Test]
	[Category("Unit")]
	[Description("The valid-keys hint names every declared argument of the args record, and nothing else.")]
	public void ValidArgsHint_ShouldNameEveryDeclaredArgument() {
		// Arrange
		string[] declared = typeof(SetActiveDynamicCaseVersionArgs).GetProperties()
			.Select(property => property.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>()?.Name)
			.OfType<string>()
			.ToArray();

		// Act
		string[] hinted = SetActiveDynamicCaseVersionTool.ValidArgsHint["Valid: ".Length..].TrimEnd('.').Split(", ");

		// Assert
		hinted.Should().BeEquivalentTo(declared, because: "the hint is what a caller retries with");
	}

	[Test]
	[Category("Unit")]
	[Description("A call with no argument object is refused with the valid keys.")]
	public void SetActiveDynamicCaseVersion_ShouldRefuseNullArgs() {
		// Arrange & Act
		CommandExecutionResult result = _tool.SetActiveDynamicCaseVersion(null!);

		// Assert
		result.ExitCode.Should().Be(1, because: "there is nothing to run");
		TextOf(result).Should().Be(SetActiveDynamicCaseVersionTool.NullArgsError, because: "the refusal is the pinned one");
		TextOf(result).Should().Contain(SetActiveDynamicCaseVersionTool.ValidArgsHint,
			because: "the next attempt can name the right keys");
		ShouldNotHaveRun();
	}

	[Test]
	[Category("Unit")]
	[Description("A missing environment answers -1 and a call that names no member answers 1; both carry the mini-contract naming exactly what is missing.")]
	public void SetActiveDynamicCaseVersion_ShouldReturnTheMiniContract_WhenRequiredArgumentsAreMissing() {
		// Arrange
		SetActiveDynamicCaseVersionArgs empty = Args("{}");
		SetActiveDynamicCaseVersionArgs noMember = Args("{\"environment-name\":\"dev\",\"version-name\":\" \"}");

		// Act
		CommandExecutionResult noEnvironment = _tool.SetActiveDynamicCaseVersion(empty);
		CommandExecutionResult missingMember = _tool.SetActiveDynamicCaseVersion(noMember);

		// Assert
		noEnvironment.ExitCode.Should().Be(-1, because: "a missing environment answers like the sibling tools");
		TextOf(noEnvironment).Should().StartWith("environment-name is required and cannot be empty.")
			.And.Contain("'environment-name', '" + SetActiveDynamicCaseVersionTool.VersionIdentityArgument + "'",
				because: "every missing argument is named");
		missingMember.ExitCode.Should().Be(1, because: "a call that names no member is a caller error");
		TextOf(missingMember).Should().StartWith("One of version-name or version-uid is required")
			.And.Contain("missing required argument '" + SetActiveDynamicCaseVersionTool.VersionIdentityArgument + "'")
			.And.Contain("Minimal valid arguments:", because: "the next attempt can be correct");
		ShouldNotHaveRun();
	}

	[Test]
	[Category("Unit")]
	[Description("The mini-contract's example is a real call: it binds to the args record with no unknown key and runs as a dry run.")]
	public void MinimalExample_ShouldBindAndRun() {
		// Arrange
		SetActiveDynamicCaseVersionArgs args = Args(SetActiveDynamicCaseVersionTool.MinimalExample);

		// Act
		CommandExecutionResult result = _tool.SetActiveDynamicCaseVersion(args);

		// Assert
		args.ExtensionData.Should().BeNull(because: "every key of the example is a real argument");
		result.ExitCode.Should().Be(0, because: "advice the tool would refuse is worse than none");
		_resolved.Captured!.DryRun.Should().BeTrue(because: "the example an agent copies first writes nothing");
	}

	[Test]
	[Category("Unit")]
	[Description("A successful call carries the compile-not-required note; a failed one does not.")]
	public void SetActiveDynamicCaseVersion_ShouldCarryTheCompileNote_OnSuccessOnly() {
		// Arrange
		SetActiveDynamicCaseVersionArgs args = Args("{\"environment-name\":\"dev\",\"version-name\":\"UsrOrderCase\"}");
		_resolver.Resolve<SetActiveDynamicCaseVersionCommand>(Arg.Any<SetActiveDynamicCaseVersionOptions>())
			.Returns(new FakeCommand(), new FakeCommand(1));

		// Act
		CommandExecutionResult success = _tool.SetActiveDynamicCaseVersion(args);
		CommandExecutionResult failure = _tool.SetActiveDynamicCaseVersion(args);

		// Assert
		success.Note.Should().Be(CommandExecutionResult.CompileNotRequiredNote, because: "a case is interpreted");
		failure.ExitCode.Should().Be(1, because: "the command's exit code is passed through");
		failure.Note.Should().BeNull(because: "a success-only signal must not ride a failed call");
	}

	[Test]
	[Category("Unit")]
	[Description("The description is ASCII, its first sentence fits the compact index, and it carries the contract: ask the user first with a dry run, running cases stay, read-back with failed-not-restored safe to repeat, unchanged on a repeat, and the package floor.")]
	public void Description_ShouldCarryTheContract() {
		// Arrange
		string description = Description();

		// Act
		string firstSentence = description[..(description.IndexOf(". ", StringComparison.Ordinal) + 1)];

		// Assert
		description.All(character => character < 128).Should().BeTrue(because: "tool descriptions are ASCII only");
		firstSentence.Length.Should().BeLessThanOrEqualTo(120, because: "the compact index cuts the purpose at 120");
		description.Should().Contain("ASK THE USER FIRST")
			.And.Contain("never call it on your own after modify-dynamic-case-as-new-version")
			.And.Contain("dry-run=true", because: "activation needs the user's yes after a dry run");
		description.Should().Contain("the family root included (that is how you go back)",
			because: "going back to the root is an activation too");
		description.Should().Contain("Running cases keep the version they started on",
			because: "the switch affects the bar and new records only");
		description.Should().Contain("failed-not-restored").And.Contain("the same call can be repeated",
			because: "a switch that did not read back is never undone and is safe to repeat");
		description.Should().Contain("Activating the active member again answers unchanged",
			because: "the call is idempotent");
		description.Should().Contain($"Requires CrtProcessBuilder {DynamicCaseChangeRequirement.MinimumProcessBuilderVersion} or later",
			because: "an older package lacks the operation");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool carries no [FeatureToggle]: it ships enabled like the other process-designer tools.")]
	public void SetActiveDynamicCaseVersion_ShouldNotBeFeatureGated() {
		// Arrange
		Type toolType = typeof(SetActiveDynamicCaseVersionTool);

		// Act
		object[] toggles = toolType.GetCustomAttributes(typeof(FeatureToggleAttribute), inherit: true);

		// Assert
		toggles.Should().BeEmpty(because: "a gated tool is silently unreachable on a default install");
	}

	private sealed class FakeCommand(int exitCode = 0)
		: SetActiveDynamicCaseVersionCommand(Substitute.For<IDynamicCaseService>(), Substitute.For<ILogger>()) {

		public SetActiveDynamicCaseVersionOptions Captured { get; private set; }

		public override int Execute(SetActiveDynamicCaseVersionOptions options) {
			Captured = options;
			return exitCode;
		}
	}
}
