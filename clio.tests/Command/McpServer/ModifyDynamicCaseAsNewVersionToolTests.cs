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
public sealed class ModifyDynamicCaseAsNewVersionToolTests {

	private FakeCommand _startup;
	private FakeCommand _resolved;
	private IToolCommandResolver _resolver;
	private ModifyDynamicCaseAsNewVersionTool _tool;

	[SetUp]
	public void Setup() {
		ConsoleLogger.Instance.ClearMessages();
		_startup = new FakeCommand();
		_resolved = new FakeCommand();
		_resolver = Substitute.For<IToolCommandResolver>();
		_resolver.Resolve<ModifyDynamicCaseAsNewVersionCommand>(Arg.Any<ModifyDynamicCaseAsNewVersionOptions>())
			.Returns(_resolved);
		_tool = new ModifyDynamicCaseAsNewVersionTool(_startup, ConsoleLogger.Instance, _resolver);
	}

	[TearDown]
	public void TearDown() {
		_resolver.ClearReceivedCalls();
		ConsoleLogger.Instance.ClearMessages();
	}

	private const string Identity = "\"environment-name\":\"dev\",\"case-name\":\"UsrOrderCase\"";

	private static ModifyDynamicCaseAsNewVersionArgs Args(string json) =>
		JsonSerializer.Deserialize<ModifyDynamicCaseAsNewVersionArgs>(json)!;

	private static string TextOf(CommandExecutionResult result) =>
		string.Join(" | ", result.Output.Select(message => message.Value?.ToString()));

	private static MethodInfo Method() =>
		typeof(ModifyDynamicCaseAsNewVersionTool).GetMethod(
			nameof(ModifyDynamicCaseAsNewVersionTool.ModifyDynamicCaseAsNewVersion))!;

	private static string Description() =>
		Method().GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;

	private static string ParameterDescription(string wireName) =>
		typeof(ModifyDynamicCaseAsNewVersionArgs).GetProperties()
			.Single(property => property.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>()
				?.Name == wireName)
			.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;

	private void ShouldNotHaveRun() {
		_resolver.DidNotReceiveWithAnyArgs().Resolve<ModifyDynamicCaseAsNewVersionCommand>(default!);
		_resolved.Captured.Should().BeNull(because: "a refused call never reaches the command");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool is a non-destructive, non-idempotent, closed-world write named modify-dynamic-case-as-new-version, kept out of the read-response deadline.")]
	public void ModifyDynamicCaseAsNewVersion_ShouldDeclareNonDestructiveNonIdempotentWriteFlags() {
		// Arrange
		MethodInfo method = Method();

		// Act
		McpServerToolAttribute attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;

		// Assert
		attribute.Name.Should().Be("modify-dynamic-case-as-new-version", because: "the guidance and agents spell this name");
		attribute.ReadOnly.Should().BeFalse(because: "it saves a new member of the case's family");
		attribute.Destructive.Should().BeFalse(
			because: "everything it writes goes to a new inactive member, so what the environment runs is unchanged");
		attribute.Idempotent.Should().BeFalse(because: "each write makes one more version");
		attribute.OpenWorld.Should().BeFalse(because: "it talks to one registered environment");
		McpReadDeadlineGate.IsRetrySafe(attribute.Name!, attribute.ReadOnly, attribute.Destructive).Should().BeFalse(
			because: "a write is never abandoned by the read-response deadline");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool runs in a per-call worker like the other process-designer writes.")]
	public void ModifyDynamicCaseAsNewVersion_ShouldRunInAPerCallWorker() {
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
		Type optionsType = typeof(ModifyDynamicCaseAsNewVersionOptions);

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
	[Description("A call that names only the case reaches the RESOLVED command, not the startup one, with no package, stages, caption, filter, confirmation or dry run.")]
	public void ModifyDynamicCaseAsNewVersion_ShouldMapTheDefaults_ToTheResolvedCommand() {
		// Arrange
		ModifyDynamicCaseAsNewVersionArgs args = Args("{" + Identity + "}");

		// Act
		CommandExecutionResult result = _tool.ModifyDynamicCaseAsNewVersion(args);

		// Assert
		result.ExitCode.Should().Be(0, because: "a named case is a valid call");
		_resolver.Received(1).Resolve<ModifyDynamicCaseAsNewVersionCommand>(
			Arg.Is<ModifyDynamicCaseAsNewVersionOptions>(options => options.Environment == "dev"));
		_startup.Captured.Should().BeNull(
			because: "an environment-sensitive tool runs the command resolved for the call's environment");
		ModifyDynamicCaseAsNewVersionOptions options = _resolved.Captured!;
		options.Environment.Should().Be("dev", because: "the environment is forwarded");
		options.CaseName.Should().Be("UsrOrderCase", because: "the case is forwarded");
		options.CaseUId.Should().BeNull(because: "the call named no UId");
		options.EntitySchemaName.Should().BeNull(because: "the call named no object");
		options.PackageName.Should().BeNull(because: "no package means the active case's package");
		options.Stages.Should().BeNull(because: "no stages means the version keeps the source's stages");
		options.Caption.Should().BeNull(because: "no caption keeps the source's");
		options.Filter.Should().BeNull(because: "no filter keeps the source's");
		options.RemoveFilter.Should().BeFalse(because: "the filter is removed only when asked");
		options.ConfirmStageRecords.Should().BeFalse(because: "the user's yes is never assumed");
		options.ConfirmValueRename.Should().BeFalse(because: "the user's yes is never assumed");
		options.DryRun.Should().BeFalse(because: "a call writes unless it asks for a dry run");
	}

	[Test]
	[Category("Unit")]
	[Description("Every identity, package-name, caption, both confirmations and dry-run map onto the options trimmed.")]
	public void ModifyDynamicCaseAsNewVersion_ShouldMapEveryArgument() {
		// Arrange
		ModifyDynamicCaseAsNewVersionArgs byUId = Args("{\"environment-name\":\"dev\","
			+ "\"case-uid\":\" 5c58c4c4-134b-4744-9c67-96d9c69c9d55 \"}");
		ModifyDynamicCaseAsNewVersionArgs args = Args("{\"environment-name\":\"dev\",\"case-name\":\" UsrOrderCase \","
			+ "\"entity-schema-name\":\" UsrOrder \","
			+ "\"package-name\":\" UsrOrdersV2 \",\"caption\":\" Orders v2 \",\"confirm-stage-records\":true,"
			+ "\"confirm-value-rename\":true,\"dry-run\":true,\"stages\":[\"New\",{\"value\":\"Done\",\"caption\":\"Closed\"}]}");

		// Act
		_tool.ModifyDynamicCaseAsNewVersion(byUId);
		string caseUId = _resolved.Captured!.CaseUId;
		CommandExecutionResult result = _tool.ModifyDynamicCaseAsNewVersion(args);

		// Assert
		result.ExitCode.Should().Be(0, because: "every argument is declared");
		ModifyDynamicCaseAsNewVersionOptions options = _resolved.Captured!;
		options.CaseName.Should().Be("UsrOrderCase", because: "the case name is forwarded trimmed");
		caseUId.Should().Be("5c58c4c4-134b-4744-9c67-96d9c69c9d55", because: "the UId is forwarded trimmed");
		options.EntitySchemaName.Should().Be("UsrOrder", because: "the object is forwarded trimmed");
		options.PackageName.Should().Be("UsrOrdersV2", because: "the version's package is forwarded trimmed");
		options.Caption.Should().Be("Orders v2", because: "the caption is forwarded trimmed");
		options.ConfirmStageRecords.Should().BeTrue(because: "the user's yes is passed through");
		options.ConfirmValueRename.Should().BeTrue(because: "the user's yes is passed through");
		options.DryRun.Should().BeTrue(because: "dry-run asks for the planned version only");
		options.Stages.Select(stage => stage.Value).Should().Equal(["New", "Done"],
			because: "the stage list keeps its order and shape");
		options.Stages[1].Caption.Should().Be("Closed", because: "a stage member is forwarded");
	}

	[Test]
	[Category("Unit")]
	[Description("A filter and a filter removal each reach the options; naming both is refused as invalid-request before the command runs.")]
	public void ModifyDynamicCaseAsNewVersion_ShouldForwardTheFilterChange_AndRefuseBoth() {
		// Arrange
		ModifyDynamicCaseAsNewVersionArgs set =
			Args("{" + Identity + ",\"filter\":{\"column\":\"UsrType\",\"values\":[\"B2B\"]}}");
		ModifyDynamicCaseAsNewVersionArgs remove = Args("{" + Identity + ",\"remove-filter\":true}");
		ModifyDynamicCaseAsNewVersionArgs both = Args("{" + Identity
			+ ",\"filter\":{\"column\":\"UsrType\",\"values\":[\"B2B\"]},\"remove-filter\":true}");

		// Act
		_tool.ModifyDynamicCaseAsNewVersion(set);
		DynamicCaseFilterArgument filter = _resolved.Captured!.Filter;
		bool removeWithSet = _resolved.Captured.RemoveFilter;
		_tool.ModifyDynamicCaseAsNewVersion(remove);
		bool removeFilter = _resolved.Captured!.RemoveFilter;
		DynamicCaseFilterArgument filterWithRemove = _resolved.Captured.Filter;
		_resolver.ClearReceivedCalls();
		CommandExecutionResult refused = _tool.ModifyDynamicCaseAsNewVersion(both);

		// Assert
		filter.Column.Should().Be("UsrType", because: "the filter column is forwarded");
		filter.Values.Should().Equal(["B2B"], because: "the filter values are forwarded");
		removeWithSet.Should().BeFalse(because: "a new filter is not a removal");
		removeFilter.Should().BeTrue(because: "a removal is forwarded");
		filterWithRemove.Should().BeNull(because: "a removal carries no new filter");
		refused.ExitCode.Should().Be(1, because: "a new filter and its removal contradict each other");
		TextOf(refused).Should().Contain("[invalid-request]", because: "the refusal carries a stable code");
		_resolver.DidNotReceiveWithAnyArgs().Resolve<ModifyDynamicCaseAsNewVersionCommand>(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("case-name and case-uid together are refused invalid-request before the command runs: the server would copy the case the UId names and ignore the name, and the write is not idempotent.")]
	public void ModifyDynamicCaseAsNewVersion_ShouldRefuse_WhenBothCaseNameAndCaseUIdAreGiven() {
		// Arrange
		ModifyDynamicCaseAsNewVersionArgs args = Args("{\"environment-name\":\"dev\",\"case-name\":\"UsrOrderCase\","
			+ "\"case-uid\":\"9a1b2c3d-0000-4000-8000-000000000001\"}");

		// Act
		CommandExecutionResult result = _tool.ModifyDynamicCaseAsNewVersion(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "two case identities would be resolved silently in favour of the UId");
		TextOf(result).Should().Contain("[invalid-request]", because: "the refusal carries a stable code")
			.And.Contain("only one of case-name or case-uid", because: "the refusal names the conflict");
		ShouldNotHaveRun();
	}

	[TestCase("caption")]
	[TestCase("package-name")]
	[Category("Unit")]
	[Description("An empty caption or package-name is refused rather than dropped, because dropping it would silently keep the source's value.")]
	public void ModifyDynamicCaseAsNewVersion_ShouldRefuseAnEmptyTextArgument(string key) {
		// Arrange
		ModifyDynamicCaseAsNewVersionArgs args = Args("{" + Identity + ",\"" + key + "\":\" \"}");

		// Act
		CommandExecutionResult result = _tool.ModifyDynamicCaseAsNewVersion(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "an empty value is a caller error");
		TextOf(result).Should().Contain(key + " cannot be empty").And.Contain("[invalid-request]",
			because: "the argument and the stable code are named");
		ShouldNotHaveRun();
	}

	[Test]
	[Category("Unit")]
	[Description("A stage carrying an undeclared key is refused as unknown-keys instead of being dropped.")]
	public void ModifyDynamicCaseAsNewVersion_ShouldRefuseAnUnknownStageKey() {
		// Arrange
		ModifyDynamicCaseAsNewVersionArgs args =
			Args("{" + Identity + ",\"stages\":[{\"value\":\"New\",\"next\":\"Done\"}]}");

		// Act
		CommandExecutionResult result = _tool.ModifyDynamicCaseAsNewVersion(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "a dropped key would make a version other than described");
		TextOf(result).Should().Contain("[unknown-keys]").And.Contain("next", because: "the key is named");
		ShouldNotHaveRun();
	}

	[Test]
	[Category("Unit")]
	[Description("An undeclared top-level key is refused through the overflow bag, naming the key and the valid keys.")]
	public void ModifyDynamicCaseAsNewVersion_ShouldRefuseAnUnknownArgument() {
		// Arrange
		ModifyDynamicCaseAsNewVersionArgs args = Args("{" + Identity + ",\"enabled\":false}");

		// Act
		CommandExecutionResult result = _tool.ModifyDynamicCaseAsNewVersion(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "an unknown key is a caller error");
		TextOf(result).Should().Contain("'enabled'").And.Contain(ModifyDynamicCaseAsNewVersionTool.ValidArgsHint,
			because: "the key and the valid set are named");
		ShouldNotHaveRun();
	}

	[Test]
	[Category("Unit")]
	[Description("The valid-keys hint names every declared argument of the args record, and nothing else.")]
	public void ValidArgsHint_ShouldNameEveryDeclaredArgument() {
		// Arrange
		string[] declared = typeof(ModifyDynamicCaseAsNewVersionArgs).GetProperties()
			.Select(property => property.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>()?.Name)
			.OfType<string>()
			.ToArray();

		// Act
		string[] hinted = ModifyDynamicCaseAsNewVersionTool.ValidArgsHint["Valid: ".Length..].TrimEnd('.')
			.Split(", ");

		// Assert
		hinted.Should().BeEquivalentTo(declared, because: "the hint is what a caller retries with");
	}

	[Test]
	[Category("Unit")]
	[Description("A call with no argument object is refused with the valid keys.")]
	public void ModifyDynamicCaseAsNewVersion_ShouldRefuseNullArgs() {
		// Arrange & Act
		CommandExecutionResult result = _tool.ModifyDynamicCaseAsNewVersion(null!);

		// Assert
		result.ExitCode.Should().Be(1, because: "there is nothing to run");
		TextOf(result).Should().Be(ModifyDynamicCaseAsNewVersionTool.NullArgsError,
			because: "the refusal is the pinned one");
		TextOf(result).Should().Contain(ModifyDynamicCaseAsNewVersionTool.ValidArgsHint,
			because: "the next attempt can name the right keys");
		ShouldNotHaveRun();
	}

	[Test]
	[Category("Unit")]
	[Description("A missing environment answers -1 and a call that names no case answers 1; both carry the mini-contract naming exactly what is missing.")]
	public void ModifyDynamicCaseAsNewVersion_ShouldReturnTheMiniContract_WhenRequiredArgumentsAreMissing() {
		// Arrange
		ModifyDynamicCaseAsNewVersionArgs empty = Args("{}");
		ModifyDynamicCaseAsNewVersionArgs noCase = Args("{\"environment-name\":\"dev\",\"caption\":\"X\"}");

		// Act
		CommandExecutionResult noEnvironment = _tool.ModifyDynamicCaseAsNewVersion(empty);
		CommandExecutionResult missingCase = _tool.ModifyDynamicCaseAsNewVersion(noCase);

		// Assert
		noEnvironment.ExitCode.Should().Be(-1, because: "a missing environment answers like the sibling tools");
		TextOf(noEnvironment).Should().StartWith("environment-name is required and cannot be empty.")
			.And.Contain("'environment-name', '" + ModifyDynamicCaseTool.CaseIdentityArgument + "'",
				because: "every missing argument is named");
		missingCase.ExitCode.Should().Be(1, because: "a call that names no case is a caller error");
		TextOf(missingCase).Should().StartWith("One of case-name, case-uid or entity-schema-name is required")
			.And.Contain("missing required argument '" + ModifyDynamicCaseTool.CaseIdentityArgument + "'")
			.And.Contain("Minimal valid arguments:", because: "the next attempt can be correct");
		ShouldNotHaveRun();
	}

	[Test]
	[Category("Unit")]
	[Description("The mini-contract's example is a real call: it binds to the args record with no unknown key and runs as a dry run.")]
	public void MinimalExample_ShouldBindAndRun() {
		// Arrange
		ModifyDynamicCaseAsNewVersionArgs args = Args(ModifyDynamicCaseAsNewVersionTool.MinimalExample);

		// Act
		CommandExecutionResult result = _tool.ModifyDynamicCaseAsNewVersion(args);

		// Assert
		args.ExtensionData.Should().BeNull(because: "every key of the example is a real argument");
		result.ExitCode.Should().Be(0, because: "advice the tool would refuse is worse than none");
		_resolved.Captured!.DryRun.Should().BeTrue(because: "the example an agent copies first writes nothing");
	}

	[Test]
	[Category("Unit")]
	[Description("A successful call carries the compile-not-required note; a failed one does not.")]
	public void ModifyDynamicCaseAsNewVersion_ShouldCarryTheCompileNote_OnSuccessOnly() {
		// Arrange
		ModifyDynamicCaseAsNewVersionArgs args = Args("{" + Identity + "}");
		_resolver.Resolve<ModifyDynamicCaseAsNewVersionCommand>(Arg.Any<ModifyDynamicCaseAsNewVersionOptions>())
			.Returns(new FakeCommand(), new FakeCommand(1));

		// Act
		CommandExecutionResult success = _tool.ModifyDynamicCaseAsNewVersion(args);
		CommandExecutionResult failure = _tool.ModifyDynamicCaseAsNewVersion(args);

		// Assert
		success.Note.Should().Be(CommandExecutionResult.CompileNotRequiredNote, because: "a case is interpreted");
		failure.ExitCode.Should().Be(1, because: "the command's exit code is passed through");
		failure.Note.Should().BeNull(because: "a success-only signal must not ride a failed call");
	}

	[Test]
	[Category("Unit")]
	[Description("The description is ASCII, its first sentence fits the compact index, and it carries the contract: the guide first, a dry run before a write, never activates, the immediate value rename, describe before retrying after failed-leftover or unknown, and the package floor.")]
	public void Description_ShouldCarryTheContract() {
		// Arrange
		string description = Description();

		// Act
		string firstSentence = description[..(description.IndexOf(". ", StringComparison.Ordinal) + 1)];

		// Assert
		description.All(character => character < 128).Should().BeTrue(because: "tool descriptions are ASCII only");
		firstSentence.Length.Should().BeLessThanOrEqualTo(120, because: "the compact index cuts the purpose at 120");
		description.Should().Contain("Read get-guidance name=process-dynamic-case-change first.",
			because: "the change article owns the workflow of changing a case");
		description.Should().Contain("dry-run=true").And.Contain("ask before writing",
			because: "the user sees the planned diff before a version is written");
		description.Should().Contain("saved INACTIVE", because: "the active case and what runs are untouched");
		description.Should().Contain("Never activates").And.Contain("set-active-dynamic-case-version",
			because: "activation is a separate call the user agrees to");
		description.Should().Contain("renames the stage's lookup value AT ONCE").And.Contain("not at activation",
			because: "a value rename takes effect for every record immediately, not when the version is activated");
		description.Should().Contain("After failed-leftover or unknown, run describe-dynamic-case before any retry.",
			because: "a blind retry makes one more version, which is never removed");
		description.Should().Contain("Not idempotent", because: "each write makes one more version");
		description.Should().Contain($"Requires CrtProcessBuilder {DynamicCaseChangeRequirement.MinimumProcessBuilderVersion} or later",
			because: "an older package lacks the operation");
	}

	[TestCase("confirm-stage-records", "a version without a stage that records hold")]
	[TestCase("confirm-value-rename", "renaming the stage's lookup value")]
	[Category("Unit")]
	[Description("The two confirmations tell the agent to leave them out unless the user said yes.")]
	public void Confirmations_ShouldBeDescribedAsTheUsersYesOnly(string parameter, string subject) {
		// Arrange & Act
		string description = ParameterDescription(parameter);

		// Assert
		description.Should().StartWith($"LEAVE IT OUT unless the user said yes to {subject}",
			because: "the flag stands for the user's consent, not the agent's");
		description.Should().Contain("never set it on your own", because: "an agent must not consent for the user");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool carries no [FeatureToggle]: it ships enabled like the other process-designer tools.")]
	public void ModifyDynamicCaseAsNewVersion_ShouldNotBeFeatureGated() {
		// Arrange
		Type toolType = typeof(ModifyDynamicCaseAsNewVersionTool);

		// Act
		object[] toggles = toolType.GetCustomAttributes(typeof(FeatureToggleAttribute), inherit: true);

		// Assert
		toggles.Should().BeEmpty(because: "a gated tool is silently unreachable on a default install");
	}

	private sealed class FakeCommand(int exitCode = 0)
		: ModifyDynamicCaseAsNewVersionCommand(Substitute.For<IDynamicCaseService>(), Substitute.For<ILogger>()) {

		public ModifyDynamicCaseAsNewVersionOptions Captured { get; private set; }

		public override int Execute(ModifyDynamicCaseAsNewVersionOptions options) {
			Captured = options;
			return exitCode;
		}
	}
}
