using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Command.ProcessModel;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-103025. A write tool called with {} or without a required argument answers with the required arguments and
/// a minimal valid call, short enough to ride along on every probe, and never at the cost of the refusal it
/// already gave: text prefix and exit code are unchanged.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ToolMiniContractTests {

	private static string TextOf(CommandExecutionResult result) =>
		string.Join(" | ", result.Output.Select(message => message.Value?.ToString()));

	[SetUp]
	public void SetUp() => ConsoleLogger.Instance.ClearMessages();

	[TearDown]
	public void TearDown() => ConsoleLogger.Instance.ClearMessages();

	[Test]
	[Category("Unit")]
	[Description("Every mini-contract stays under the size budget even with every argument reported missing.")]
	public void MiniContracts_ShouldStayWithinTheSizeBudget() {
		// Arrange
		string[] all = ["environment-name", "descriptor", "package-name", "schema-name", "title-localizations", "name", "code"];
		string[] contracts = [
			ToolMiniContract.ForCreateApp(all),
			ToolMiniContract.ForCreateBusinessProcess(all),
			ToolMiniContract.ForModifyBusinessProcess(all),
			ToolMiniContract.ForCreateEntitySchema(all)
		];

		// Act & Assert
		contracts.Should().OnlyContain(text => text.Length <= ToolMiniContract.MaxLength,
			because: "the text travels in the error of every probe; the full contract is one get-tool-contract call away");
		contracts.Should().OnlyContain(text => text.Contains("Required:") && text.Contains("Minimal valid arguments:"),
			because: "required arguments and one valid call are the whole point");
	}

	[Test]
	[Category("Unit")]
	[Description("The examples are real argument objects: each binds to its tool's args record and passes the tool's own required-argument checks.")]
	public void Examples_ShouldBindToTheToolsOwnArgs() {
		// Arrange & Act
		ApplicationCreateArgs app = JsonSerializer.Deserialize<ApplicationCreateArgs>(ToolMiniContract.CreateAppExample)!;
		CreateBusinessProcessArgs create = JsonSerializer.Deserialize<CreateBusinessProcessArgs>(
			ToolMiniContract.CreateBusinessProcessExample)!;
		ModifyBusinessProcessArgs modify = JsonSerializer.Deserialize<ModifyBusinessProcessArgs>(
			ToolMiniContract.ModifyBusinessProcessExample)!;
		CreateEntitySchemaArgs schema = JsonSerializer.Deserialize<CreateEntitySchemaArgs>(
			ToolMiniContract.CreateEntitySchemaExample)!;

		// Assert
		app.Name.Should().NotBeNullOrWhiteSpace(because: "the create-app example must carry a name");
		app.Code.Should().NotBeNullOrWhiteSpace(because: "the create-app example must carry a code");
		create.ExtensionData.Should().BeNull(because: "every key of the example is a real argument");
		McpToolArgumentSupport.TryReadJsonDocumentArgument(create.Descriptor, JsonValueKind.Object, "descriptor",
			out _, out _).Should().BeTrue(because: "the descriptor example must be accepted as the descriptor");
		modify.ExtensionData.Should().BeNull(because: "every key of the modify example is a real argument");
		McpToolArgumentSupport.TryReadJsonDocumentArgument(modify.Operations, JsonValueKind.Array, "operations",
			out _, out _).Should().BeTrue(because: "the operations example must be accepted as the operations array");
		CreateEntitySchemaTool.MissingCreateArguments(schema).Should().BeEmpty(
			because: "the entity-schema example supplies everything the tool requires");
		Action build = () => CreateEntitySchemaTool.CreateOptions(schema, schema.ParentSchemaName, schema.ExtendParent);
		build.Should().NotThrow(because: "the example's columns must pass the tool's own column checks");
	}

	[Test]
	[Category("Unit")]
	[Description("create-app with no name and code, or with only one of them, returns the mini-contract naming exactly what is missing.")]
	public async Task CreateApp_ShouldReturnTheMiniContract_WhenNameOrCodeIsMissing() {
		// Arrange
		ApplicationCreateTool tool = new(Substitute.For<ILogger>(), Substitute.For<IToolCommandResolver>(),
			Substitute.For<IApplicationCreateService>(), Substitute.For<IApplicationCreateEnrichmentService>());

		// Act
		ApplicationContextResponse empty = await tool.ApplicationCreate(
			new ApplicationCreateArgs(null!, null!), null!, null!);
		ApplicationContextResponse noCode = await tool.ApplicationCreate(
			new ApplicationCreateArgs("Todo", " "), null!, null!);

		// Assert
		empty.Success.Should().BeFalse(because: "a call without name and code cannot run");
		empty.Error.Should().StartWith("name is required.",
			because: "the refusal create-app always gave still leads, so clients matching it keep working");
		empty.Error.Should().Contain("missing required arguments 'name', 'code'")
			.And.Contain("Minimal valid arguments:").And.Contain("\"code\":\"TodoList\"", because: "both missing arguments and a runnable example are named");
		noCode.Error.Should().Contain("missing required argument 'code'").And.NotContain("'name'",
			because: "only what is actually missing is named");
		noCode.Error!.Length.Should().BeLessThanOrEqualTo(ToolMiniContract.MaxLength, because: "the mini-contract stays within its size budget");
	}

	private sealed class FakeCreateCommand : CreateBusinessProcessCommand {
		public CreateBusinessProcessOptions? Captured { get; private set; }

		public FakeCreateCommand() : base(Substitute.For<ICreateBusinessProcessService>(),
			Substitute.For<IProcessDescriber>(), Substitute.For<ILogger>()) {
		}

		public override int Execute(CreateBusinessProcessOptions options) {
			Captured = options;
			return 0;
		}
	}

	[Test]
	[Category("Unit")]
	[Description("create-business-process: a missing environment keeps its -1 refusal and a missing descriptor its 1; both gain the mini-contract, and the example itself is built.")]
	public void CreateBusinessProcess_ShouldAppendTheMiniContract_AndKeepTheRefusal() {
		// Arrange
		FakeCreateCommand command = new();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<CreateBusinessProcessCommand>(Arg.Any<CreateBusinessProcessOptions>()).Returns(command);
		CreateBusinessProcessTool tool = new(command, ConsoleLogger.Instance, resolver);

		// Act
		CommandExecutionResult noEnvironment = tool.CreateBusinessProcess(
			JsonSerializer.Deserialize<CreateBusinessProcessArgs>("{}")!);
		CommandExecutionResult noDescriptor = tool.CreateBusinessProcess(
			JsonSerializer.Deserialize<CreateBusinessProcessArgs>("{\"environment-name\":\"dev\"}")!);
		CommandExecutionResult example = tool.CreateBusinessProcess(
			JsonSerializer.Deserialize<CreateBusinessProcessArgs>(ToolMiniContract.CreateBusinessProcessExample)!);

		// Assert
		noEnvironment.ExitCode.Should().Be(-1, because: "the pinned exit code of a missing environment does not change");
		TextOf(noEnvironment).Should().StartWith("environment-name is required and cannot be empty.")
			.And.Contain("missing required arguments 'environment-name', 'descriptor'", because: "the original refusal leads and the mini-contract follows it");
		noDescriptor.ExitCode.Should().Be(1, because: "the descriptor refusal keeps its exit code");
		TextOf(noDescriptor).Should().Contain("descriptor is required and cannot be empty.")
			.And.Contain("missing required argument 'descriptor'").And.Contain("Minimal valid arguments:", because: "the original refusal leads and the mini-contract follows it");
		example.ExitCode.Should().Be(0, because: "advice a tool would refuse is worse than none");
	}

	[Test]
	[Category("Unit")]
	[Description("modify-business-process: missing environment, identity and operations keep their refusals (-1, -1, 1) and gain the mini-contract; a wrong-kind operations value does not.")]
	public void ModifyBusinessProcess_ShouldAppendTheMiniContract_AndKeepTheRefusal() {
		// Arrange
		ModifyBusinessProcessTool tool = new(null!, ConsoleLogger.Instance, Substitute.For<IToolCommandResolver>());

		// Act
		CommandExecutionResult empty = tool.ModifyBusinessProcess(
			JsonSerializer.Deserialize<ModifyBusinessProcessArgs>("{}")!);
		CommandExecutionResult noIdentity = tool.ModifyBusinessProcess(JsonSerializer.Deserialize<ModifyBusinessProcessArgs>(
			"{\"environment-name\":\"dev\",\"operations\":[{\"op\":\"removeElement\",\"elementName\":\"A\"}]}")!);
		CommandExecutionResult noOperations = tool.ModifyBusinessProcess(JsonSerializer.Deserialize<ModifyBusinessProcessArgs>(
			"{\"environment-name\":\"dev\",\"process-name\":\"UsrP\"}")!);
		CommandExecutionResult wrongKind = tool.ModifyBusinessProcess(JsonSerializer.Deserialize<ModifyBusinessProcessArgs>(
			"{\"environment-name\":\"dev\",\"process-name\":\"UsrP\",\"operations\":{}}")!);

		// Assert
		empty.ExitCode.Should().Be(-1, because: "the empty-call refusal keeps its exit code");
		TextOf(empty).Should().Contain("environment-name is required").And.Contain("Minimal valid arguments:", because: "the original refusal leads and the mini-contract follows it");
		noIdentity.ExitCode.Should().Be(-1, because: "the missing-identity refusal keeps its exit code");
		TextOf(noIdentity).Should().Contain("one of process-name or process-uid is required.")
			.And.Contain("missing required argument 'process-name or process-uid'", because: "the original refusal leads and the mini-contract names the missing identity");
		noOperations.ExitCode.Should().Be(1, because: "the missing-operations refusal keeps its exit code");
		TextOf(noOperations).Should().Contain("operations is required and cannot be empty.")
			.And.Contain("Minimal valid arguments:", because: "the original refusal leads and the mini-contract follows it");
		TextOf(wrongKind).Should().NotContain("Minimal valid arguments",
			because: "a value of the wrong kind is not a missing argument, so it gets its own refusal only");
	}

	[Test]
	[Category("Unit")]
	[Description("create-entity-schema: {} and partial calls return the mini-contract without reaching enrichment or the command; a complete call is not intercepted.")]
	public async Task CreateEntitySchema_ShouldReturnTheMiniContract_WhenRequiredArgumentsAreMissing() {
		// Arrange
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		ISchemaEnrichmentService enrichment = Substitute.For<ISchemaEnrichmentService>();
		CreateEntitySchemaTool tool = new(null!, ConsoleLogger.Instance, resolver, enrichment);

		// Act
		CommandExecutionResult empty = await tool.CreateEntitySchema(
			JsonSerializer.Deserialize<CreateEntitySchemaArgs>("{}")!);
		CommandExecutionResult noTitle = await tool.CreateEntitySchema(JsonSerializer.Deserialize<CreateEntitySchemaArgs>(
			"{\"environment-name\":\"dev\",\"package-name\":\"P\",\"schema-name\":\"UsrX\"}")!);

		// Assert
		empty.ExitCode.Should().Be(1, because: "a call missing its required arguments cannot run");
		TextOf(empty).Should().Contain("missing required arguments 'package-name', 'schema-name', 'title-localizations'")
			.And.Contain("Minimal valid arguments:", because: "every missing argument and a runnable example are named");
		TextOf(noTitle).Should().Contain("missing required argument 'title-localizations'", because: "only the missing title is named");
		TextOf(empty).Should().NotContain("'environment-name'",
			because: "under credential passthrough the caller must omit environment-name, so the gate never demands it");
		CreateEntitySchemaTool.MissingCreateArguments(JsonSerializer.Deserialize<CreateEntitySchemaArgs>(
				"{\"package-name\":\"P\",\"schema-name\":\"UsrX\",\"title-localizations\":{\"en-US\":\"X\"}}")!)
			.Should().BeEmpty(because: "a passthrough call without environment-name must reach the resolver");
		enrichment.ReceivedCalls().Should().BeEmpty(because: "a probe must not cost a Data Forge lookup");
		resolver.ReceivedCalls().Should().BeEmpty(because: "nothing was resolved for a call that cannot run");
		TextOf(empty).Length.Should().BeLessThanOrEqualTo(ToolMiniContract.MaxLength, because: "the mini-contract stays within its size budget");
	}
}
