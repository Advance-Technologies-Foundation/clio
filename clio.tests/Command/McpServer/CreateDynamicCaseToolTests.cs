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
public sealed class CreateDynamicCaseToolTests {

	private FakeCommand _resolved;
	private IToolCommandResolver _resolver;
	private CreateDynamicCaseTool _tool;

	[SetUp]
	public void Setup() {
		ConsoleLogger.Instance.ClearMessages();
		_resolved = new FakeCommand();
		_resolver = Substitute.For<IToolCommandResolver>();
		_resolver.Resolve<CreateDynamicCaseCommand>(Arg.Any<CreateDynamicCaseOptions>()).Returns(_resolved);
		_tool = new CreateDynamicCaseTool(new FakeCommand(), ConsoleLogger.Instance, _resolver);
	}

	[TearDown]
	public void TearDown() {
		_resolver.ClearReceivedCalls();
		ConsoleLogger.Instance.ClearMessages();
	}

	private static CreateDynamicCaseArgs Args(string json) => JsonSerializer.Deserialize<CreateDynamicCaseArgs>(json)!;

	private const string Required =
		"\"environment-name\":\"dev\",\"package-name\":\"UsrOrders\",\"entity-schema-name\":\"UsrOrder\","
		+ "\"stage-column-name\":\"UsrStatus\"";

	private static string TextOf(CommandExecutionResult result) =>
		string.Join(" | ", result.Output.Select(message => message.Value?.ToString()));

	private static string Description() =>
		typeof(CreateDynamicCaseTool).GetMethod(nameof(CreateDynamicCaseTool.CreateDynamicCase))!
			.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;

	[Test]
	[Category("Unit")]
	[Description("The tool is a destructive, idempotent, closed-world write named create-dynamic-case: an equivalent re-run answers already-present and writes nothing to the case.")]
	public void CreateDynamicCase_ShouldDeclareDestructiveIdempotentWriteFlags() {
		// Arrange
		MethodInfo method = typeof(CreateDynamicCaseTool).GetMethod(nameof(CreateDynamicCaseTool.CreateDynamicCase))!;

		// Act
		McpServerToolAttribute attribute = method.GetCustomAttribute<McpServerToolAttribute>()!;

		// Assert
		attribute.Name.Should().Be("create-dynamic-case", because: "the guidance and agents spell this name");
		attribute.ReadOnly.Should().BeFalse(because: "it writes a schema, rows, bindings and a page");
		attribute.Destructive.Should().BeTrue(because: "it changes the environment's configuration");
		attribute.Idempotent.Should().BeTrue(because: "an equivalent re-run writes nothing to the case");
		attribute.OpenWorld.Should().BeFalse(because: "it talks to one registered environment");
		McpReadDeadlineGate.IsRetrySafe(attribute.Name!, attribute.ReadOnly, attribute.Destructive).Should().BeFalse(
			because: "a write is never abandoned by the read-response deadline");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool runs in a per-call worker like the other process-designer writes.")]
	public void CreateDynamicCase_ShouldRunInAPerCallWorker() {
		// Arrange
		MethodInfo method = typeof(CreateDynamicCaseTool).GetMethod(nameof(CreateDynamicCaseTool.CreateDynamicCase))!;

		// Act
		McpToolExecutionAttribute execution = method.GetCustomAttribute<McpToolExecutionAttribute>()!;

		// Assert
		execution.Location.Should().Be(McpToolExecutionLocation.Worker, because: "the design places it in a worker");
		execution.Lifetime.Should().Be(McpToolExecutionLifetime.PerCall, because: "each call gets its own worker");
	}

	[Test]
	[Category("Unit")]
	[Description("A complete call reaches the RESOLVED command with every argument mapped and the defaults add-progress-bar=true, add-package-dependencies=true, dry-run=false.")]
	public void CreateDynamicCase_ShouldMapArgumentsAndDefaults_ToTheResolvedCommand() {
		// Arrange
		CreateDynamicCaseArgs args = Args("{" + Required + ",\"order-column\":\"Position\",\"name\":\"UsrOrderCase\","
			+ "\"caption\":\"Order case\",\"page-schema-name\":\"UsrOrders_FormPage\"}");

		// Act
		CommandExecutionResult result = _tool.CreateDynamicCase(args);

		// Assert
		result.ExitCode.Should().Be(0, because: "a complete call runs");
		CreateDynamicCaseOptions options = _resolved.Captured!;
		options.Environment.Should().Be("dev", because: "the environment is forwarded");
		options.PackageName.Should().Be("UsrOrders", because: "the package is forwarded");
		options.EntitySchemaName.Should().Be("UsrOrder", because: "the object is forwarded");
		options.StageColumnName.Should().Be("UsrStatus", because: "the column is forwarded");
		options.OrderColumn.Should().Be("Position", because: "the order column is forwarded");
		options.Name.Should().Be("UsrOrderCase", because: "the name is forwarded");
		options.Caption.Should().Be("Order case", because: "the caption is forwarded");
		options.PageSchemaName.Should().Be("UsrOrders_FormPage", because: "the page is forwarded");
		options.AddProgressBar.Should().BeTrue(because: "the bar is added unless the caller says otherwise");
		options.AddPackageDependencies.Should().BeTrue(because: "missing dependencies are added by default");
		options.DryRun.Should().BeFalse(because: "a call writes unless it asks for a dry run");
		options.Stages.Should().BeEmpty(because: "no stages means the default order");
	}

	[Test]
	[Category("Unit")]
	[Description("dry-run, add-progress-bar and add-package-dependencies map onto the options the service reads (dry-run becomes the validate-only plan).")]
	public void CreateDynamicCase_ShouldMapTheFlags() {
		// Arrange
		CreateDynamicCaseArgs args = Args("{" + Required
			+ ",\"dry-run\":true,\"add-progress-bar\":false,\"add-package-dependencies\":false}");

		// Act
		_tool.CreateDynamicCase(args);

		// Assert
		_resolved.Captured!.DryRun.Should().BeTrue(because: "dry-run asks for the plan only");
		_resolved.Captured.AddProgressBar.Should().BeFalse(because: "the caller turned the bar off");
		_resolved.Captured.AddPackageDependencies.Should().BeFalse(because: "the caller turned the add off");
	}

	[TestCase("[{\"value\":\"New\"},{\"value\":\"Done\"}]")]
	[TestCase("[\"New\",\"Done\"]")]
	[TestCase("\"[{\\\"value\\\":\\\"New\\\"},\\\"Done\\\"]\"")]
	[Category("Unit")]
	[Description("stages is accepted as an array of {value} objects, an array of strings, or a string holding either, and normalised to the values in order.")]
	public void CreateDynamicCase_ShouldNormaliseStages(string stagesJson) {
		// Arrange
		CreateDynamicCaseArgs args = Args("{" + Required + ",\"stages\":" + stagesJson + "}");

		// Act
		CommandExecutionResult result = _tool.CreateDynamicCase(args);

		// Assert
		result.ExitCode.Should().Be(0, because: "every accepted form is a valid call");
		_resolved.Captured!.Stages.Should().Equal(["New", "Done"], because: "the order is kept and the shape normalised");
	}

	[Test]
	[Category("Unit")]
	[Description("A stage carrying steps is refused as steps-not-supported and the command is never resolved.")]
	public void CreateDynamicCase_ShouldRefuseSteps() {
		// Arrange
		CreateDynamicCaseArgs args = Args("{" + Required + ",\"stages\":[{\"value\":\"New\",\"steps\":[]}]}");

		// Act
		CommandExecutionResult result = _tool.CreateDynamicCase(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "a stage with steps cannot be built yet");
		TextOf(result).Should().Contain("[steps-not-supported]", because: "the stable code is named");
		_resolver.DidNotReceiveWithAnyArgs().Resolve<CreateDynamicCaseCommand>(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("A stage carrying any key other than value is refused as unknown-keys instead of being dropped.")]
	public void CreateDynamicCase_ShouldRefuseAnUnknownStageKey() {
		// Arrange
		CreateDynamicCaseArgs args = Args("{" + Required + ",\"stages\":[{\"value\":\"New\",\"color\":\"#fff\"}]}");

		// Act
		CommandExecutionResult result = _tool.CreateDynamicCase(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "a dropped key would build a stage other than the one described");
		TextOf(result).Should().Contain("[unknown-keys]").And.Contain("color", because: "the key is named");
	}

	[TestCase("[1]")]
	[TestCase("[\" \"]")]
	[TestCase("{\"value\":\"New\"}")]
	[Category("Unit")]
	[Description("A stage of the wrong shape, an empty stage, or a stages value that is not an array is refused before the command runs.")]
	public void CreateDynamicCase_ShouldRefuseAMalformedStages(string stagesJson) {
		// Arrange
		CreateDynamicCaseArgs args = Args("{" + Required + ",\"stages\":" + stagesJson + "}");

		// Act
		CommandExecutionResult result = _tool.CreateDynamicCase(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "the stage list cannot be read");
		_resolver.DidNotReceiveWithAnyArgs().Resolve<CreateDynamicCaseCommand>(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("An undeclared top-level key is refused through the overflow bag, naming the valid keys.")]
	public void CreateDynamicCase_ShouldRefuseAnUnknownArgument() {
		// Arrange
		CreateDynamicCaseArgs args = Args("{" + Required + ",\"steps\":[]}");

		// Act
		CommandExecutionResult result = _tool.CreateDynamicCase(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "an unknown key is a caller error");
		TextOf(result).Should().Contain("'steps'").And.Contain(CreateDynamicCaseTool.ValidArgsHint,
			because: "the key and the valid set are named");
		_resolver.DidNotReceiveWithAnyArgs().Resolve<CreateDynamicCaseCommand>(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("A call with no argument object is refused with the valid keys.")]
	public void CreateDynamicCase_ShouldRefuseNullArgs() {
		// Arrange & Act
		CommandExecutionResult result = _tool.CreateDynamicCase(null!);

		// Assert
		result.ExitCode.Should().Be(1, because: "there is nothing to run");
		TextOf(result).Should().Be(CreateDynamicCaseTool.NullArgsError, because: "the refusal is the pinned one");
	}

	[Test]
	[Category("Unit")]
	[Description("A missing environment keeps the -1 refusal of its siblings and a missing required argument answers 1; both carry the mini-contract naming exactly what is missing.")]
	public void CreateDynamicCase_ShouldReturnTheMiniContract_WhenRequiredArgumentsAreMissing() {
		// Arrange
		CreateDynamicCaseArgs empty = Args("{}");
		CreateDynamicCaseArgs noColumn = Args("{\"environment-name\":\"dev\",\"package-name\":\"UsrOrders\","
			+ "\"entity-schema-name\":\"UsrOrder\"}");

		// Act
		CommandExecutionResult noEnvironment = _tool.CreateDynamicCase(empty);
		CommandExecutionResult missingColumn = _tool.CreateDynamicCase(noColumn);

		// Assert
		noEnvironment.ExitCode.Should().Be(-1, because: "a missing environment answers like the sibling tools");
		TextOf(noEnvironment).Should().StartWith("environment-name is required and cannot be empty.")
			.And.Contain("missing required arguments 'environment-name', 'package-name', 'entity-schema-name', "
				+ "'stage-column-name'", because: "every missing argument is named");
		missingColumn.ExitCode.Should().Be(1, because: "a missing argument is a caller error");
		TextOf(missingColumn).Should().Contain("missing required argument 'stage-column-name'")
			.And.Contain("Minimal valid arguments:", because: "the next attempt can be correct");
		TextOf(missingColumn).Length.Should().BeLessThanOrEqualTo(ToolMiniContract.MaxLength + 100,
			because: "the mini-contract stays short");
		_resolver.DidNotReceiveWithAnyArgs().Resolve<CreateDynamicCaseCommand>(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("The mini-contract's example is a real call: it binds to the args record with no unknown key and runs.")]
	public void MinimalExample_ShouldBindAndRun() {
		// Arrange
		CreateDynamicCaseArgs args = Args(CreateDynamicCaseTool.MinimalExample);

		// Act
		CommandExecutionResult result = _tool.CreateDynamicCase(args);

		// Assert
		args.ExtensionData.Should().BeNull(because: "every key of the example is a real argument");
		result.ExitCode.Should().Be(0, because: "advice the tool would refuse is worse than none");
	}

	[Test]
	[Category("Unit")]
	[Description("A successful call carries the compile-not-required note; a failed one does not.")]
	public void CreateDynamicCase_ShouldCarryTheCompileNote_OnSuccessOnly() {
		// Arrange
		CreateDynamicCaseArgs args = Args("{" + Required + "}");
		_resolver.Resolve<CreateDynamicCaseCommand>(Arg.Any<CreateDynamicCaseOptions>())
			.Returns(new FakeCommand(), new FakeCommand(1));

		// Act
		CommandExecutionResult success = _tool.CreateDynamicCase(args);
		CommandExecutionResult failure = _tool.CreateDynamicCase(args);

		// Assert
		success.Note.Should().Be(CommandExecutionResult.CompileNotRequiredNote,
			because: "a case, its rows and a Freedom UI page need no compile");
		failure.Note.Should().BeNull(because: "a success-only signal must not ride a failed call");
	}

	[Test]
	[Category("Unit")]
	[Description("The description is ASCII, its first sentence fits the compact index, and it names the guidance article an agent must read first.")]
	public void Description_ShouldBeAsciiAndNameTheGuide() {
		// Arrange
		string description = Description();

		// Act
		string firstSentence = description[..(description.IndexOf(". ", StringComparison.Ordinal) + 1)];

		// Assert
		description.All(character => character < 128).Should().BeTrue(because: "tool descriptions are ASCII only");
		description.Should().Contain("Read get-guidance name=process-dynamic-case first.",
			because: "the article owns the workflow");
		firstSentence.Length.Should().BeLessThanOrEqualTo(120, because: "the compact index cuts the purpose at 120");
		firstSentence.Should().StartWith("Create a minimal dynamic case (DCM)", because: "the index line says what it does");
		description.Should().Contain("not supported", because: "what the tool cannot do yet is stated");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool carries no [FeatureToggle]: it ships enabled like the other process-designer tools.")]
	public void CreateDynamicCase_ShouldNotBeFeatureGated() {
		// Arrange
		Type toolType = typeof(CreateDynamicCaseTool);

		// Act
		object[] toggles = toolType.GetCustomAttributes(typeof(FeatureToggleAttribute), inherit: true);

		// Assert
		toggles.Should().BeEmpty(because: "a gated tool is silently unreachable on a default install");
	}

	private sealed class FakeCommand(int exitCode = 0)
		: CreateDynamicCaseCommand(Substitute.For<IDynamicCaseService>(), Substitute.For<ILogger>()) {

		public CreateDynamicCaseOptions Captured { get; private set; }

		public override int Execute(CreateDynamicCaseOptions options) {
			Captured = options;
			return exitCode;
		}
	}
}
