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
public sealed class ModifyDynamicCaseToolTests {

	private FakeCommand _resolved;
	private IToolCommandResolver _resolver;
	private ModifyDynamicCaseTool _tool;

	[SetUp]
	public void Setup() {
		ConsoleLogger.Instance.ClearMessages();
		_resolved = new FakeCommand();
		_resolver = Substitute.For<IToolCommandResolver>();
		_resolver.Resolve<ModifyDynamicCaseCommand>(Arg.Any<ModifyDynamicCaseOptions>()).Returns(_resolved);
		_tool = new ModifyDynamicCaseTool(new FakeCommand(), ConsoleLogger.Instance, _resolver);
	}

	[TearDown]
	public void TearDown() {
		_resolver.ClearReceivedCalls();
		ConsoleLogger.Instance.ClearMessages();
	}

	private const string Identity = "\"environment-name\":\"dev\",\"case-name\":\"UsrOrderCase\"";

	private static ModifyDynamicCaseArgs Args(string json) => JsonSerializer.Deserialize<ModifyDynamicCaseArgs>(json)!;

	private static string TextOf(CommandExecutionResult result) =>
		string.Join(" | ", result.Output.Select(message => message.Value?.ToString()));

	private static MethodInfo Method() =>
		typeof(ModifyDynamicCaseTool).GetMethod(nameof(ModifyDynamicCaseTool.ModifyDynamicCase))!;

	private static string Description() =>
		Method().GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;

	private static string ParameterDescription(string wireName) =>
		typeof(ModifyDynamicCaseArgs).GetProperties()
			.Single(property => property.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>()
				?.Name == wireName)
			.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;

	[Test]
	[Category("Unit")]
	[Description("The tool is a destructive, idempotent, closed-world write named modify-dynamic-case, kept out of the read-response deadline.")]
	public void ModifyDynamicCase_ShouldDeclareDestructiveIdempotentWriteFlags() {
		// Arrange
		MethodInfo method = Method();

		// Act
		McpServerToolAttribute attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;

		// Assert
		attribute.Name.Should().Be("modify-dynamic-case", because: "the guidance and agents spell this name");
		attribute.ReadOnly.Should().BeFalse(because: "it rewrites the case");
		attribute.Destructive.Should().BeTrue(because: "a stage left out of the list is removed");
		attribute.Idempotent.Should().BeTrue(because: "the same declarative call again answers unchanged");
		attribute.OpenWorld.Should().BeFalse(because: "it talks to one registered environment");
		McpReadDeadlineGate.IsRetrySafe(attribute.Name!, attribute.ReadOnly, attribute.Destructive).Should().BeFalse(
			because: "a write is never abandoned by the read-response deadline");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool runs in a per-call worker like the other process-designer writes.")]
	public void ModifyDynamicCase_ShouldRunInAPerCallWorker() {
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
	[Description("A call that names only the case reaches the RESOLVED command with no stages, no caption, no enabled change, no confirmation and no dry run.")]
	public void ModifyDynamicCase_ShouldMapTheDefaults_ToTheResolvedCommand() {
		// Arrange
		ModifyDynamicCaseArgs args = Args("{" + Identity + "}");

		// Act
		CommandExecutionResult result = _tool.ModifyDynamicCase(args);

		// Assert
		result.ExitCode.Should().Be(0, because: "a named case is a valid call");
		ModifyDynamicCaseOptions options = _resolved.Captured!;
		options.Environment.Should().Be("dev", because: "the environment is forwarded");
		options.CaseName.Should().Be("UsrOrderCase", because: "the case is forwarded");
		options.Stages.Should().BeNull(because: "no stages means the stages stay as they are");
		options.Caption.Should().BeNull(because: "no caption keeps the stored one");
		options.Enabled.Should().BeNull(because: "enabled is changed only when named");
		options.ConfirmRunningInstances.Should().BeFalse(because: "the user's yes is never assumed");
		options.DryRun.Should().BeFalse(because: "a call writes unless it asks for a dry run");
	}

	[Test]
	[Category("Unit")]
	[Description("dry-run, confirm-running-instances, enabled and caption map onto the options the service sends (dry-run becomes validateOnly there).")]
	public void ModifyDynamicCase_ShouldMapTheFlags() {
		// Arrange
		ModifyDynamicCaseArgs args = Args("{" + Identity
			+ ",\"dry-run\":true,\"confirm-running-instances\":true,\"enabled\":false,\"caption\":\" Orders \"}");

		// Act
		_tool.ModifyDynamicCase(args);

		// Assert
		_resolved.Captured!.DryRun.Should().BeTrue(because: "dry-run asks for the planned diff only");
		_resolved.Captured.ConfirmRunningInstances.Should().BeTrue(because: "the user's yes is passed through");
		_resolved.Captured.Enabled.Should().BeFalse(because: "enabled=false disables the case");
		_resolved.Captured.Caption.Should().Be("Orders", because: "the caption is forwarded trimmed");
	}

	[TestCase("[{\"value\":\"New\"},{\"value\":\"Done\"}]")]
	[TestCase("[\"New\",\"Done\"]")]
	[TestCase("\"[{\\\"value\\\":\\\"New\\\"},\\\"Done\\\"]\"")]
	[Category("Unit")]
	[Description("stages is accepted as an array of objects, an array of bare strings, or a string holding either, and normalised to the values in order.")]
	public void ModifyDynamicCase_ShouldNormaliseStages(string stagesJson) {
		// Arrange
		ModifyDynamicCaseArgs args = Args("{" + Identity + ",\"stages\":" + stagesJson + "}");

		// Act
		CommandExecutionResult result = _tool.ModifyDynamicCase(args);

		// Assert
		result.ExitCode.Should().Be(0, because: "every accepted form is a valid call");
		_resolved.Captured!.Stages.Select(stage => stage.Value).Should().Equal(["New", "Done"],
			because: "the order is kept and the shape normalised");
		_resolved.Captured.Stages.Should().OnlyContain(stage => !stage.GroupSent,
			because: "a bare string or an object without group keeps the stored group");
	}

	[Test]
	[Category("Unit")]
	[Description("Every declared stage member reaches the options, and an explicit null group is kept apart from an absent one, because it takes the stage out of its group.")]
	public void ModifyDynamicCase_ShouldKeepAnExplicitNullGroup_AndEveryStageMember() {
		// Arrange
		ModifyDynamicCaseArgs args = Args("{" + Identity + ",\"stages\":[{\"value\":\"New\",\"group\":null},"
			+ "{\"value\":\"Done\",\"caption\":\"Closed\",\"color\":\"#FF0000\",\"group\":\"New\",\"successful\":false},"
			+ "{\"value\":\"Paid\"}]}");

		// Act
		CommandExecutionResult result = _tool.ModifyDynamicCase(args);

		// Assert
		result.ExitCode.Should().Be(0, because: "every member is declared");
		DynamicCaseStageItem ungrouped = _resolved.Captured!.Stages[0];
		ungrouped.GroupSent.Should().BeTrue(because: "the call named group, as null");
		ungrouped.Group.Should().BeNull(because: "null takes the stage out of its group");
		DynamicCaseStageItem grouped = _resolved.Captured.Stages[1];
		grouped.Caption.Should().Be("Closed", because: "the stage caption is forwarded");
		grouped.Color.Should().Be("#FF0000", because: "the stage colour is forwarded");
		grouped.Group.Should().Be("New", because: "the parent stage is named by its value");
		grouped.Successful.Should().BeFalse(because: "false ends the case unsuccessfully");
		_resolved.Captured.Stages[2].GroupSent.Should().BeFalse(because: "an absent group keeps the stored one");
	}

	[Test]
	[Category("Unit")]
	[Description("A stage carrying steps is refused as steps-not-supported and the command is never resolved.")]
	public void ModifyDynamicCase_ShouldRefuseSteps() {
		// Arrange
		ModifyDynamicCaseArgs args = Args("{" + Identity + ",\"stages\":[{\"value\":\"New\",\"steps\":[]}]}");

		// Act
		CommandExecutionResult result = _tool.ModifyDynamicCase(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "a stage with steps cannot be changed yet");
		TextOf(result).Should().Contain("[steps-not-supported]", because: "the stable code is named");
		_resolver.DidNotReceiveWithAnyArgs().Resolve<ModifyDynamicCaseCommand>(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("A stage carrying an undeclared key is refused as unknown-keys instead of being dropped.")]
	public void ModifyDynamicCase_ShouldRefuseAnUnknownStageKey() {
		// Arrange
		ModifyDynamicCaseArgs args = Args("{" + Identity + ",\"stages\":[{\"value\":\"New\",\"next\":\"Done\"}]}");

		// Act
		CommandExecutionResult result = _tool.ModifyDynamicCase(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "a dropped key would change the case other than described");
		TextOf(result).Should().Contain("[unknown-keys]").And.Contain("next", because: "the key is named");
		_resolver.DidNotReceiveWithAnyArgs().Resolve<ModifyDynamicCaseCommand>(default!);
	}

	[TestCase("[1]")]
	[TestCase("[\" \"]")]
	[TestCase("{\"value\":\"New\"}")]
	[TestCase("[{\"value\":\"New\",\"caption\":\"\"}]")]
	[TestCase("[{\"value\":\"New\",\"successful\":\"no\"}]")]
	[Category("Unit")]
	[Description("A stage of the wrong shape, an empty stage or caption, a non-boolean successful, or a stages value that is not an array is refused before the command runs.")]
	public void ModifyDynamicCase_ShouldRefuseAMalformedStages(string stagesJson) {
		// Arrange
		ModifyDynamicCaseArgs args = Args("{" + Identity + ",\"stages\":" + stagesJson + "}");

		// Act
		CommandExecutionResult result = _tool.ModifyDynamicCase(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "the stage list cannot be read");
		_resolver.DidNotReceiveWithAnyArgs().Resolve<ModifyDynamicCaseCommand>(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("An empty caption is refused rather than dropped, because dropping it would keep the caption the caller meant to change.")]
	public void ModifyDynamicCase_ShouldRefuseAnEmptyCaption() {
		// Arrange
		ModifyDynamicCaseArgs args = Args("{" + Identity + ",\"caption\":\" \"}");

		// Act
		CommandExecutionResult result = _tool.ModifyDynamicCase(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "an empty caption is a caller error");
		TextOf(result).Should().Contain("[invalid-request]", because: "the stable code is named");
	}

	[Test]
	[Category("Unit")]
	[Description("An undeclared top-level key is refused through the overflow bag, naming the valid keys.")]
	public void ModifyDynamicCase_ShouldRefuseAnUnknownArgument() {
		// Arrange
		ModifyDynamicCaseArgs args = Args("{" + Identity + ",\"stage-column-name\":\"UsrStatus\"}");

		// Act
		CommandExecutionResult result = _tool.ModifyDynamicCase(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "an unknown key is a caller error");
		TextOf(result).Should().Contain("'stage-column-name'").And.Contain(ModifyDynamicCaseTool.ValidArgsHint,
			because: "the key and the valid set are named");
		_resolver.DidNotReceiveWithAnyArgs().Resolve<ModifyDynamicCaseCommand>(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("A call with no argument object is refused with the valid keys.")]
	public void ModifyDynamicCase_ShouldRefuseNullArgs() {
		// Arrange & Act
		CommandExecutionResult result = _tool.ModifyDynamicCase(null!);

		// Assert
		result.ExitCode.Should().Be(1, because: "there is nothing to run");
		TextOf(result).Should().Be(ModifyDynamicCaseTool.NullArgsError, because: "the refusal is the pinned one");
	}

	[Test]
	[Category("Unit")]
	[Description("A missing environment keeps the -1 refusal of its siblings and a call that names no case answers 1; both carry the mini-contract naming exactly what is missing.")]
	public void ModifyDynamicCase_ShouldReturnTheMiniContract_WhenRequiredArgumentsAreMissing() {
		// Arrange
		ModifyDynamicCaseArgs empty = Args("{}");
		ModifyDynamicCaseArgs noCase = Args("{\"environment-name\":\"dev\",\"enabled\":false}");

		// Act
		CommandExecutionResult noEnvironment = _tool.ModifyDynamicCase(empty);
		CommandExecutionResult missingCase = _tool.ModifyDynamicCase(noCase);

		// Assert
		noEnvironment.ExitCode.Should().Be(-1, because: "a missing environment answers like the sibling tools");
		TextOf(noEnvironment).Should().StartWith("environment-name is required and cannot be empty.")
			.And.Contain("'environment-name', '" + ModifyDynamicCaseTool.CaseIdentityArgument + "'",
				because: "every missing argument is named");
		missingCase.ExitCode.Should().Be(1, because: "a call that names no case is a caller error");
		TextOf(missingCase).Should().Contain("missing required argument '" + ModifyDynamicCaseTool.CaseIdentityArgument
			+ "'").And.Contain("Minimal valid arguments:", because: "the next attempt can be correct");
		_resolver.DidNotReceiveWithAnyArgs().Resolve<ModifyDynamicCaseCommand>(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("The mini-contract's example is a real call: it binds to the args record with no unknown key and runs as a dry run.")]
	public void MinimalExample_ShouldBindAndRun() {
		// Arrange
		ModifyDynamicCaseArgs args = Args(ModifyDynamicCaseTool.MinimalExample);

		// Act
		CommandExecutionResult result = _tool.ModifyDynamicCase(args);

		// Assert
		args.ExtensionData.Should().BeNull(because: "every key of the example is a real argument");
		result.ExitCode.Should().Be(0, because: "advice the tool would refuse is worse than none");
		_resolved.Captured!.DryRun.Should().BeTrue(because: "the example an agent copies first writes nothing");
	}

	[Test]
	[Category("Unit")]
	[Description("A successful call carries the compile-not-required note; a failed one does not.")]
	public void ModifyDynamicCase_ShouldCarryTheCompileNote_OnSuccessOnly() {
		// Arrange
		ModifyDynamicCaseArgs args = Args("{" + Identity + "}");
		_resolver.Resolve<ModifyDynamicCaseCommand>(Arg.Any<ModifyDynamicCaseOptions>())
			.Returns(new FakeCommand(), new FakeCommand(1));

		// Act
		CommandExecutionResult success = _tool.ModifyDynamicCase(args);
		CommandExecutionResult failure = _tool.ModifyDynamicCase(args);

		// Assert
		success.Note.Should().Be(CommandExecutionResult.CompileNotRequiredNote, because: "a case is interpreted");
		failure.Note.Should().BeNull(because: "a success-only signal must not ride a failed call");
	}

	[Test]
	[Category("Unit")]
	[Description("The description is ASCII, its first sentence fits the compact index, and it carries the workflow rules: the guide first, describe and a dry run before a write, the whole stage list, enabled alone, and no compile or restart.")]
	public void Description_ShouldCarryTheWorkflowRules() {
		// Arrange
		string description = Description();

		// Act
		string firstSentence = description[..(description.IndexOf(". ", StringComparison.Ordinal) + 1)];

		// Assert
		description.All(character => character < 128).Should().BeTrue(because: "tool descriptions are ASCII only");
		firstSentence.Length.Should().BeLessThanOrEqualTo(120, because: "the compact index cuts the purpose at 120");
		description.Should().Contain("Read get-guidance name=process-dynamic-case first.",
			because: "the article owns the workflow");
		description.Should().Contain("describe-dynamic-case").And.Contain("dry-run=true")
			.And.Contain("ask before writing", because: "the user sees the planned diff before anything is written");
		description.Should().Contain("WHOLE desired list").And.Contain("left out is removed",
			because: "a declarative list removes what it leaves out");
		description.Should().Contain("send enabled alone", because: "enabled is a save of its own");
		description.Should().Contain("no compile-creatio and no restart").And.Contain("reload",
			because: "a case is interpreted, but open pages keep the old case");
	}

	[Test]
	[Category("Unit")]
	[Description("confirm-running-instances tells the agent to leave it out unless the user said yes, so it is never set on the agent's own.")]
	public void ConfirmRunningInstances_ShouldBeDescribedAsTheUsersYesOnly() {
		// Arrange & Act
		string description = ParameterDescription("confirm-running-instances");

		// Assert
		description.Should().StartWith("LEAVE IT OUT unless the user said yes to changing a case that has running cases",
			because: "the flag stands for the user's consent, not the agent's");
		description.Should().Contain("never set it on your own", because: "an agent must not consent for the user");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool carries no [FeatureToggle]: it ships enabled like the other process-designer tools.")]
	public void ModifyDynamicCase_ShouldNotBeFeatureGated() {
		// Arrange
		Type toolType = typeof(ModifyDynamicCaseTool);

		// Act
		object[] toggles = toolType.GetCustomAttributes(typeof(FeatureToggleAttribute), inherit: true);

		// Assert
		toggles.Should().BeEmpty(because: "a gated tool is silently unreachable on a default install");
	}

	private sealed class FakeCommand(int exitCode = 0)
		: ModifyDynamicCaseCommand(Substitute.For<IDynamicCaseService>(), Substitute.For<ILogger>()) {

		public ModifyDynamicCaseOptions Captured { get; private set; }

		public override int Execute(ModifyDynamicCaseOptions options) {
			Captured = options;
			return exitCode;
		}
	}
}
