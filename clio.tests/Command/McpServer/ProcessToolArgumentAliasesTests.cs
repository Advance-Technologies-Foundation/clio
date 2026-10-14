using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
/// ENG-103025. Unambiguous argument shapes on the process tools run and report the canonical form in a warning;
/// ambiguous ones are refused and nothing runs.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ProcessToolArgumentAliasesTests {

	private const string Graph =
		"{\"nodes\":[{\"name\":\"s\",\"type\":\"startEvent\"},{\"name\":\"e\",\"type\":\"endEvent\"}],"
		+ "\"edges\":[{\"source\":\"s\",\"target\":\"e\"}]}";

	private const string Descriptor =
		"{\"name\":\"UsrDemo\",\"caption\":\"Demo\",\"packageName\":\"Custom\",\"elements\":["
		+ "{\"name\":\"s\",\"type\":\"startEvent\",\"caption\":\"Start\"},"
		+ "{\"name\":\"e\",\"type\":\"endEvent\",\"caption\":\"End\"}],\"flows\":[{\"source\":\"s\",\"target\":\"e\"}]}";

	private IRequiredPackageChecker _checker = null!;
	private IToolCommandResolver _resolver = null!;
	private ValidateProcessGraphTool _validateTool = null!;

	[SetUp]
	public void SetUp() {
		ConsoleLogger.Instance.ClearMessages();
		_checker = Substitute.For<IRequiredPackageChecker>();
		_resolver = Substitute.For<IToolCommandResolver>();
		_resolver.Resolve<IRequiredPackageChecker>(Arg.Any<EnvironmentOptions>()).Returns(_checker);
		_validateTool = new ValidateProcessGraphTool(new ProcessGraphValidator(), _resolver);
	}

	[TearDown]
	public void TearDown() => ConsoleLogger.Instance.ClearMessages();

	private static T Bind<T>(string json) => JsonSerializer.Deserialize<T>(json)!;

	private static string TextOf(CommandExecutionResult result) =>
		string.Join(" | ", result.Output.Select(message => message.Value?.ToString()));

	// ------------------------------------------------------------------------------------------ validate

	[TestCase("graph")]
	[TestCase("descriptor")]
	[TestCase("process")]
	[TestCase("process-graph")]
	[TestCase("processGraph")]
	[Category("Unit")]
	[Description("A graph sent as an object under graph/descriptor/process is validated and the response names the canonical form.")]
	public void Validate_ShouldRunTheGraph_WhenItArrivesUnderAWrapperName(string wrapper) {
		// Arrange
		ValidateProcessGraphArgs args = Bind<ValidateProcessGraphArgs>(
			$"{{\"environment-name\":\"dev\",\"{wrapper}\":{Graph}}}");

		// Act
		ValidateProcessGraphResponse response = _validateTool.Validate(args);

		// Assert
		response.Success.Should().BeTrue(because: "the wrapped graph is the same graph, so it must be validated, not refused");
		response.HasErrors.Should().BeFalse(because: "Start -> End breaks no rule");
		response.Warnings.Should().ContainSingle(
			because: "the caller must learn the canonical spelling exactly once").Which
			.Should().Contain("'nodes' and 'edges'", because: "the warning names the canonical form")
			.And.Contain(wrapper, because: "and echoes the spelling that was accepted");
	}

	[Test]
	[Category("Unit")]
	[Description("A wrapper carrying a JSON string of the graph is read the same way, as the descriptor argument is elsewhere.")]
	public void Validate_ShouldRunTheGraph_WhenTheWrapperIsAJsonString() {
		// Arrange
		string quoted = JsonSerializer.Serialize(Graph);
		ValidateProcessGraphArgs args = Bind<ValidateProcessGraphArgs>(
			$"{{\"environment-name\":\"dev\",\"graph\":{quoted}}}");

		// Act
		ValidateProcessGraphResponse response = _validateTool.Validate(args);

		// Assert
		response.Success.Should().BeTrue(because: "a string holding the graph JSON has exactly one reading");
		response.Warnings.Should().NotBeNull(because: "it is still a non-canonical spelling");
	}

	[Test]
	[Category("Unit")]
	[Description("A create-business-process descriptor (elements/flows, with kind) is validated as the graph it describes; only the graph fields are read.")]
	public void Validate_ShouldRunTheGraph_WhenADescriptorWithElementsAndFlowsIsSent() {
		// Arrange
		ValidateProcessGraphArgs args = Bind<ValidateProcessGraphArgs>(
			$"{{\"environment-name\":\"dev\",\"descriptor\":{Descriptor}}}");

		// Act
		ValidateProcessGraphResponse response = _validateTool.Validate(args);

		// Assert
		response.Success.Should().BeTrue(because: "elements/flows map one-to-one onto nodes/edges");
		response.HasErrors.Should().BeFalse(because: "the descriptor's graph is Start -> End");
		response.Findings.Should().BeEmpty(because: "the validator saw both nodes and the flow");
	}

	[Test]
	[Category("Unit")]
	[Description("Flat top-level elements and flows (no wrapper) are accepted as nodes and edges; a flow's kind becomes flow-kind.")]
	public void Validate_ShouldMapFlatElementsAndFlows_ToNodesAndEdges() {
		// Arrange
		ValidateProcessGraphArgs args = Bind<ValidateProcessGraphArgs>(
			"{\"environment-name\":\"dev\",\"elements\":[{\"name\":\"s\",\"type\":\"startEvent\"},"
			+ "{\"name\":\"g\",\"type\":\"exclusiveGateway\"},{\"name\":\"e\",\"type\":\"endEvent\"}],"
			+ "\"flows\":[{\"source\":\"s\",\"target\":\"g\"},{\"source\":\"g\",\"target\":\"e\",\"kind\":\"conditional\"}]}");

		// Act
		ValidateProcessGraphResponse response = _validateTool.Validate(args);

		// Assert
		response.Success.Should().BeTrue(because: "flat elements and flows are unambiguous");
		response.Findings.Should().Contain(finding => finding.RuleId == "R13",
			because: "kind:conditional must reach the validator as a conditional flow, whose missing condition R13 reports");
	}

	[Test]
	[Category("Unit")]
	[Description("A canonical call is untouched: no warnings, and the field is absent from the wire JSON.")]
	public void Validate_ShouldNotWarn_WhenTheCallIsCanonical() {
		// Arrange
		ValidateProcessGraphArgs args = Bind<ValidateProcessGraphArgs>($"{{\"environment-name\":\"dev\",{Graph[1..]}");

		// Act
		ValidateProcessGraphResponse response = _validateTool.Validate(args);

		// Assert
		response.Success.Should().BeTrue(because: "the canonical shape is the baseline");
		response.Warnings.Should().BeNull(because: "there is nothing to correct");
		JsonSerializer.Serialize(response).Should().NotContain("warnings",
			because: "a canonical response must stay byte-for-byte what it was");
	}

	[Test]
	[Category("Unit")]
	[Description("Ambiguity is refused without validating: the canonical nodes/edges together with a wrapper.")]
	public void Validate_ShouldRefuse_WhenWrapperAndCanonicalNodesAreBothSent() {
		// Arrange
		ValidateProcessGraphArgs args = Bind<ValidateProcessGraphArgs>(
			$"{{\"environment-name\":\"dev\",{Graph[1..^1]},\"graph\":{Graph}}}");

		// Act
		ValidateProcessGraphResponse response = _validateTool.Validate(args);

		// Assert
		response.Success.Should().BeFalse(because: "two graphs cannot both be the graph, and picking one would be a guess");
		response.Error.Should().Contain("Ambiguous graph", because: "the graph and the canonical nodes cannot both be meant");
		response.HasErrors.Should().BeNull(because: "nothing was validated");
		_resolver.ReceivedCalls().Should().BeEmpty(because: "a caller mistake is answered before any environment is resolved");
	}

	[Test]
	[Category("Unit")]
	[Description("Two wrappers, or a wrapper plus flat elements, are ambiguous.")]
	public void Validate_ShouldRefuse_WhenTwoWrappersAreSent() {
		// Arrange
		ValidateProcessGraphArgs twoWrappers = Bind<ValidateProcessGraphArgs>(
			$"{{\"environment-name\":\"dev\",\"graph\":{Graph},\"process\":{Graph}}}");
		ValidateProcessGraphArgs wrapperAndFlat = Bind<ValidateProcessGraphArgs>(
			$"{{\"environment-name\":\"dev\",\"graph\":{Graph},\"elements\":[]}}");

		// Act
		ValidateProcessGraphResponse first = _validateTool.Validate(twoWrappers);
		ValidateProcessGraphResponse second = _validateTool.Validate(wrapperAndFlat);

		// Assert
		first.Success.Should().BeFalse(because: "'graph' and 'process' both claim to be the graph");
		first.Error.Should().Contain("Ambiguous graph", because: "two wrappers cannot both be the graph");
		second.Success.Should().BeFalse(because: "'graph' and flat 'elements' both claim to be the graph");
		second.Error.Should().Contain("Ambiguous graph", because: "a wrapper and flat elements cannot both be the graph");
	}

	[Test]
	[Category("Unit")]
	[Description("Unknown keys inside a wrapper, a wrapper of the wrong kind and a node with an unknown nested key are refused, never dropped.")]
	public void Validate_ShouldRefuse_WhenTheWrapperContentIsNotAGraph() {
		// Arrange
		ValidateProcessGraphArgs foreign = Bind<ValidateProcessGraphArgs>(
			$"{{\"environment-name\":\"dev\",\"graph\":{{\"nodes\":[{{\"name\":\"s\",\"type\":\"startEvent\"}}],\"nodez\":[]}}}}");
		ValidateProcessGraphArgs wrongKind = Bind<ValidateProcessGraphArgs>(
			"{\"environment-name\":\"dev\",\"graph\":[1,2]}");
		ValidateProcessGraphArgs nestedKey = Bind<ValidateProcessGraphArgs>(
			"{\"environment-name\":\"dev\",\"graph\":{\"nodes\":[{\"name\":\"s\",\"nodeKind\":\"startEvent\"}]}}");

		// Act
		ValidateProcessGraphResponse first = _validateTool.Validate(foreign);
		ValidateProcessGraphResponse second = _validateTool.Validate(wrongKind);
		ValidateProcessGraphResponse third = _validateTool.Validate(nestedKey);

		// Assert
		first.Error.Should().Contain("nodez", because: "the unknown key inside the wrapper is named, not ignored");
		second.Error.Should().Contain("must be a JSON object", because: "an array is not a graph");
		third.Error.Should().Contain("nodeKind", because: "the strict node check still names an unknown nested key");
		new[] { first, second, third }.Should().OnlyContain(response => !response.Success,
			because: "none of these has a single reading");
	}

	[Test]
	[Category("Unit")]
	[Description("A key that is not a known synonym is still refused by the ordinary unknown-argument check, even next to a valid wrapper.")]
	public void Validate_ShouldStillRefuseUnknownKeys_NextToAWrapper() {
		// Arrange
		ValidateProcessGraphArgs args = Bind<ValidateProcessGraphArgs>(
			$"{{\"environment-name\":\"dev\",\"graph\":{Graph},\"process-name\":\"X\"}}");

		// Act
		ValidateProcessGraphResponse response = _validateTool.Validate(args);

		// Assert
		response.Success.Should().BeFalse(because: "'process-name' is not an argument of this tool");
		response.Error.Should().Contain("Unknown args").And.Contain("process-name", because: "an unrecognised key is still named back to the caller");
	}

	// ------------------------------------------------------------------------------------------ describe

	private sealed class FakeDescribeCommand : DescribeProcessCommand {
		public DescribeProcessOptions? Captured { get; private set; }

		public FakeDescribeCommand() : base(Substitute.For<IProcessDescriber>(), Substitute.For<ILogger>()) {
		}

		public override int Execute(DescribeProcessOptions options) {
			Captured = options;
			return 0;
		}
	}

	private (DescribeProcessTool Tool, FakeDescribeCommand Command) DescribeTool() {
		FakeDescribeCommand command = new();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<DescribeProcessCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		return (new DescribeProcessTool(command, ConsoleLogger.Instance, resolver), command);
	}

	[TestCase("processName", "UsrOrder_Flow", "UsrOrder_Flow", null, null)]
	[TestCase("process_name", "UsrOrder_Flow", "UsrOrder_Flow", null, null)]
	[TestCase("schemaName", "UsrOrder_Flow", "UsrOrder_Flow", null, null)]
	[TestCase("name", "UsrOrder_Flow", "UsrOrder_Flow", null, null)]
	[TestCase("process", "UsrOrder_Flow", "UsrOrder_Flow", null, null)]
	[TestCase("processUid", "0a1b2c3d-1111-2222-3333-444455556666", null, "0a1b2c3d-1111-2222-3333-444455556666", null)]
	[TestCase("uid", "0a1b2c3d-1111-2222-3333-444455556666", null, "0a1b2c3d-1111-2222-3333-444455556666", null)]
	[TestCase("name", "0a1b2c3d-1111-2222-3333-444455556666", null, "0a1b2c3d-1111-2222-3333-444455556666", null)]
	[TestCase("caption", "Order approval", null, null, "Order approval")]
	[TestCase("name", "Order approval", null, null, "Order approval")]
	[Category("Unit")]
	[Description("Identity synonyms of describe-business-process are executed as the canonical argument they name, with a warning.")]
	public void DescribeProcess_ShouldAcceptIdentitySynonyms(
		string key, string value, string? name, string? uid, string? caption) {
		// Arrange
		(DescribeProcessTool tool, FakeDescribeCommand command) = DescribeTool();
		DescribeProcessArgs args = Bind<DescribeProcessArgs>(
			$"{{\"environment-name\":\"dev\",\"{key}\":\"{value}\"}}");

		// Act
		CommandExecutionResult result = tool.DescribeProcess(args);

		// Assert
		result.ExitCode.Should().Be(0, because: "the synonym has one reading, so the call runs");
		command.Captured!.ProcessName.Should().Be(name, because: "the synonym is read as process-name");
		command.Captured.ProcessUid.Should().Be(uid, because: "the synonym is read as process-uid");
		command.Captured.ProcessCaption.Should().Be(caption, because: "the synonym is read as process-caption");
		result.Output.Should().Contain(message => message.Value!.ToString()!.Contains(key),
			because: "the response must echo which spelling was accepted");
		TextOf(result).Should().Contain("process-",
			because: "and name the canonical argument it was read as");
	}

	[Test]
	[Category("Unit")]
	[Description("A synonym next to its canonical argument, or two synonyms of one argument, is ambiguous: refused, command not resolved.")]
	public void DescribeProcess_ShouldRefuse_WhenTheIdentityIsAmbiguous() {
		// Arrange
		(DescribeProcessTool tool, FakeDescribeCommand command) = DescribeTool();
		DescribeProcessArgs withCanonical = Bind<DescribeProcessArgs>(
			"{\"environment-name\":\"dev\",\"process-name\":\"UsrA\",\"name\":\"UsrB\"}");
		DescribeProcessArgs twoSynonyms = Bind<DescribeProcessArgs>(
			"{\"environment-name\":\"dev\",\"processName\":\"UsrA\",\"name\":\"UsrB\"}");

		// Act
		CommandExecutionResult first = tool.DescribeProcess(withCanonical);
		CommandExecutionResult second = tool.DescribeProcess(twoSynonyms);

		// Assert
		first.ExitCode.Should().Be(1, because: "an ambiguous identity is a caller error");
		TextOf(first).Should().Contain("Ambiguous process identity", because: "the canonical identity next to a synonym is ambiguous");
		second.ExitCode.Should().Be(1, because: "an ambiguous identity is a validation refusal");
		TextOf(second).Should().Contain("Ambiguous process identity", because: "two synonyms of one identity are ambiguous");
		command.Captured.Should().BeNull(because: "nothing may be read when the identity is a guess");
	}

	[Test]
	[Category("Unit")]
	[Description("Keys that are not identity synonyms, and synonyms with an unusable value, are still refused by name.")]
	public void DescribeProcess_ShouldStillRefuseUnknownKeys() {
		// Arrange
		(DescribeProcessTool tool, FakeDescribeCommand command) = DescribeTool();
		DescribeProcessArgs unknown = Bind<DescribeProcessArgs>("{\"environment-name\":\"dev\",\"procesName\":\"UsrA\"}");
		DescribeProcessArgs notText = Bind<DescribeProcessArgs>("{\"environment-name\":\"dev\",\"name\":42}");

		// Act
		CommandExecutionResult first = tool.DescribeProcess(unknown);
		CommandExecutionResult second = tool.DescribeProcess(notText);

		// Assert
		TextOf(first).Should().Contain("procesName", because: "a typo is not a synonym");
		TextOf(second).Should().Contain("Unknown args",
			because: "a non-text identity is left to the ordinary refusal rather than coerced");
		command.Captured.Should().BeNull(because: "nothing is read for an ambiguous identity");
	}

	// ------------------------------------------------------------------------------------------ create

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

	private (CreateBusinessProcessTool Tool, FakeCreateCommand Command) CreateTool() {
		FakeCreateCommand command = new();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<CreateBusinessProcessCommand>(Arg.Any<CreateBusinessProcessOptions>()).Returns(command);
		return (new CreateBusinessProcessTool(command, ConsoleLogger.Instance, resolver), command);
	}

	[TestCase("process")]
	[TestCase("definition")]
	[TestCase("process-descriptor")]
	[TestCase("processDescriptor")]
	[Category("Unit")]
	[Description("A descriptor object sent under a synonym is built, and the warning names 'descriptor'.")]
	public void CreateBusinessProcess_ShouldBuild_WhenTheDescriptorArrivesUnderASynonym(string key) {
		// Arrange
		(CreateBusinessProcessTool tool, FakeCreateCommand command) = CreateTool();
		CreateBusinessProcessArgs args = Bind<CreateBusinessProcessArgs>(
			$"{{\"environment-name\":\"dev\",\"{key}\":{Descriptor}}}");

		// Act
		CommandExecutionResult result = tool.CreateBusinessProcess(args);

		// Assert
		result.ExitCode.Should().Be(0, because: "the synonym is unambiguous");
		JsonDocument.Parse(command.Captured!.DescriptorJson).RootElement.GetProperty("name").GetString()
			.Should().Be("UsrDemo", because: "the descriptor reaches the command intact");
		TextOf(result).Should().Contain($"'{key}' was accepted as 'descriptor'", because: "the response names the canonical argument");
	}

	[Test]
	[Category("Unit")]
	[Description("Flat descriptor fields (name, caption, packageName, elements, flows) are wrapped into the descriptor and built.")]
	public void CreateBusinessProcess_ShouldWrapFlatDescriptorFields() {
		// Arrange
		(CreateBusinessProcessTool tool, FakeCreateCommand command) = CreateTool();
		CreateBusinessProcessArgs args = Bind<CreateBusinessProcessArgs>(
			$"{{\"environment-name\":\"dev\",{Descriptor[1..]}");

		// Act
		CommandExecutionResult result = tool.CreateBusinessProcess(args);

		// Assert
		result.ExitCode.Should().Be(0, because: "flat fields that include elements can only be a descriptor");
		using JsonDocument built = JsonDocument.Parse(command.Captured!.DescriptorJson);
		built.RootElement.GetProperty("caption").GetString().Should().Be("Demo", because: "the flat caption lands in the descriptor");
		built.RootElement.GetProperty("packageName").GetString().Should().Be("Custom", because: "the flat packageName lands in the descriptor");
		built.RootElement.GetProperty("elements").GetArrayLength().Should().Be(2, because: "the flat elements land in the descriptor");
		built.RootElement.GetProperty("flows").GetArrayLength().Should().Be(1, because: "the flat flows land in the descriptor");
		TextOf(result).Should().Contain("wrapped into 'descriptor'", because: "the response says the fields were wrapped");
	}

	[Test]
	[Category("Unit")]
	[Description("Ambiguous descriptors are refused unbuilt: descriptor plus a synonym, descriptor plus flat fields, two synonyms.")]
	public void CreateBusinessProcess_ShouldRefuse_WhenTheDescriptorIsAmbiguous() {
		// Arrange
		(CreateBusinessProcessTool tool, FakeCreateCommand command) = CreateTool();
		string[] bodies = [
			$"{{\"environment-name\":\"dev\",\"descriptor\":{Descriptor},\"process\":{Descriptor}}}",
			$"{{\"environment-name\":\"dev\",\"descriptor\":{Descriptor},\"elements\":[]}}",
			$"{{\"environment-name\":\"dev\",\"process\":{Descriptor},\"definition\":{Descriptor}}}",
			$"{{\"environment-name\":\"dev\",\"process\":{Descriptor},\"caption\":\"x\"}}",
			"{\"environment-name\":\"dev\",\"packageName\":\"PkgA\",\"package_name\":\"PkgB\",\"elements\":[],\"flows\":[]}",
			"{\"environment-name\":\"dev\",\"elements\":[],\"Elements\":[],\"flows\":[]}"
		];

		foreach (string body in bodies) {
			// Act
			CommandExecutionResult result = tool.CreateBusinessProcess(Bind<CreateBusinessProcessArgs>(body));

			// Assert
			result.ExitCode.Should().Be(1, because: "an ambiguous descriptor is a caller error: " + body);
			TextOf(result).Should().Contain("Ambiguous descriptor", because: "a descriptor and flat descriptor fields, or two spellings of one field, cannot both be the process");
		}
		command.Captured.Should().BeNull(because: "nothing may be built from a guessed descriptor");
	}

	[Test]
	[Category("Unit")]
	[Description("Flat fields WITHOUT elements are not recognised as a descriptor and stay refused as unknown arguments.")]
	public void CreateBusinessProcess_ShouldNotWrap_WhenThereAreNoElements() {
		// Arrange
		(CreateBusinessProcessTool tool, FakeCreateCommand command) = CreateTool();
		CreateBusinessProcessArgs args = Bind<CreateBusinessProcessArgs>(
			"{\"environment-name\":\"dev\",\"name\":\"UsrX\",\"caption\":\"X\"}");

		// Act
		CommandExecutionResult result = tool.CreateBusinessProcess(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "an unrecognised key is refused");
		TextOf(result).Should().Contain("Unknown args", because: "the unknown key is named back to the caller");
		command.Captured.Should().BeNull(because: "nothing is built for a refused call");
	}
	[TestCase("descriptor")]
	[TestCase("process")]
	[Category("Unit")]
	[Description("QA ENG-103025: a descriptor wrapped twice ({\"descriptor\":{\"descriptor\":{...}}}) is unwrapped and built, with a warning.")]
	public void CreateBusinessProcess_ShouldUnwrap_ADoubleWrappedDescriptor(string innerKey) {
		// Arrange
		(CreateBusinessProcessTool tool, FakeCreateCommand command) = CreateTool();
		CreateBusinessProcessArgs args = Bind<CreateBusinessProcessArgs>(
			$"{{\"environment-name\":\"dev\",\"descriptor\":{{\"{innerKey}\":{Descriptor}}}}}");

		// Act
		CommandExecutionResult result = tool.CreateBusinessProcess(args);

		// Assert
		result.ExitCode.Should().Be(0, because: "a wrapper that is the descriptor's only member has one reading");
		JsonDocument.Parse(command.Captured!.DescriptorJson).RootElement.GetProperty("name").GetString()
			.Should().Be("UsrDemo", because: "the inner object is the process");
		TextOf(result).Should().Contain($"'descriptor' held only '{innerKey}'", because: "the warning names the wrapper the caller actually sent")
			.And.Contain("the inner object was used", because: "the caller learns the canonical shape");
	}

	[Test]
	[Category("Unit")]
	[Description("QA ENG-103025: a nested descriptor next to other fields is refused unbuilt as ambiguous.")]
	public void CreateBusinessProcess_ShouldRefuse_ANestedDescriptorNextToOtherFields() {
		// Arrange
		(CreateBusinessProcessTool tool, FakeCreateCommand command) = CreateTool();
		CreateBusinessProcessArgs args = Bind<CreateBusinessProcessArgs>(
			$"{{\"environment-name\":\"dev\",\"descriptor\":{{\"name\":\"UsrOther\",\"descriptor\":{Descriptor}}}}}");

		// Act
		CommandExecutionResult result = tool.CreateBusinessProcess(args);

		// Assert
		result.ExitCode.Should().Be(1, because: "two candidate process objects have no single reading");
		TextOf(result).Should().Contain("Ambiguous descriptor", because: "the refusal names the problem");
		command.Captured.Should().BeNull(because: "nothing may be built from a guessed descriptor");
	}
	[Test]
	[Category("Unit")]
	[Description("A descriptor nested deeper than the unwrap limit is refused clearly instead of being passed on still wrapped.")]
	public void CreateBusinessProcess_ShouldRefuse_ADescriptorNestedTooDeep() {
		// Arrange
		(CreateBusinessProcessTool tool, FakeCreateCommand command) = CreateTool();
		string nested = Descriptor;
		for (int level = 0; level < 4; level++) {
			nested = $"{{\"descriptor\":{nested}}}";
		}

		// Act
		CommandExecutionResult result = tool.CreateBusinessProcess(
			Bind<CreateBusinessProcessArgs>($"{{\"environment-name\":\"dev\",\"descriptor\":{nested}}}"));

		// Assert
		result.ExitCode.Should().Be(1, because: "an over-nested descriptor is a caller error");
		TextOf(result).Should().Contain("nested more than", because: "the refusal says why");
		command.Captured.Should().BeNull(because: "nothing is built from a still-wrapped object");
	}

	// ------------------------------------------------------------------------------------------ review round 1

	private sealed class FakeModifyCommand : ModifyBusinessProcessCommand {
		public ModifyBusinessProcessOptions? Captured { get; private set; }

		public FakeModifyCommand() : base(Substitute.For<IModifyBusinessProcessService>(),
			Substitute.For<IProcessDescriber>(), Substitute.For<ILogger>()) {
		}

		public override int Execute(ModifyBusinessProcessOptions options) {
			Captured = options;
			return 0;
		}
	}

	private sealed class FakeNewVersionCommand : ModifyProcessAsNewVersionCommand {
		public ModifyProcessAsNewVersionOptions? Captured { get; private set; }

		public FakeNewVersionCommand() : base(Substitute.For<IModifyProcessAsNewVersionService>(), Substitute.For<IProcessDescriber>(),
			Substitute.For<ILogger>()) {
		}

		public override int Execute(ModifyProcessAsNewVersionOptions options) {
			Captured = options;
			return 0;
		}
	}

	private static (ModifyBusinessProcessTool Tool, FakeModifyCommand Command) ModifyTool() {
		FakeModifyCommand command = new();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<ModifyBusinessProcessCommand>(Arg.Any<ModifyBusinessProcessOptions>()).Returns(command);
		return (new ModifyBusinessProcessTool(command, ConsoleLogger.Instance, resolver), command);
	}

	private const string Operations = "[{\"op\":\"addParameter\",\"parameter\":{\"name\":\"UsrA\",\"type\":\"Integer\"}}]";

	[TestCase("processName", "UsrOrder_Flow", "UsrOrder_Flow", "")]
	[TestCase("code", "UsrOrder_Flow", "UsrOrder_Flow", "")]
	[TestCase("uid", "0a1b2c3d-1111-2222-3333-444455556666", "", "0a1b2c3d-1111-2222-3333-444455556666")]
	[Category("Unit")]
	[Description("Review: modify-business-process accepts the value-independent identity synonyms describe-business-process accepts, with the same warning.")]
	public void ModifyBusinessProcess_ShouldAcceptIdentitySynonyms(string key, string value, string expectedName,
		string expectedUid) {
		// Arrange
		(ModifyBusinessProcessTool tool, FakeModifyCommand command) = ModifyTool();

		// Act
		CommandExecutionResult result = tool.ModifyBusinessProcess(Bind<ModifyBusinessProcessArgs>(
			$"{{\"environment-name\":\"dev\",\"{key}\":\"{value}\",\"operations\":{Operations}}}"));

		// Assert
		result.ExitCode.Should().Be(0, because: "a value-independent synonym has one reading on every process tool");
		command.Captured!.ProcessName.Should().Be(expectedName, because: "the synonym is read as the canonical name");
		command.Captured.ProcessUid.Should().Be(expectedUid, because: "the synonym is read as the canonical uid");
		TextOf(result).Should().Contain($"'{key}' was accepted as", because: "the response names the canonical argument");
	}

	[Test]
	[Category("Unit")]
	[Description("Review: on a write, name/process are never read by the shape of their value, a caption cannot address the process, and a synonym next to its canonical argument is refused.")]
	public void ModifyBusinessProcess_ShouldRefuse_GuessedOrAmbiguousIdentities() {
		// Arrange
		(ModifyBusinessProcessTool tool, FakeModifyCommand command) = ModifyTool();
		string[] unknown = ["\"name\":\"UsrOrder_Flow\"", "\"process\":\"UsrOrder_Flow\"", "\"caption\":\"Order\""];

		// Act
		List<CommandExecutionResult> refusedAsUnknown = unknown.Select(pair => tool.ModifyBusinessProcess(
			Bind<ModifyBusinessProcessArgs>($"{{\"environment-name\":\"dev\",{pair},\"operations\":{Operations}}}"))).ToList();
		CommandExecutionResult ambiguous = tool.ModifyBusinessProcess(Bind<ModifyBusinessProcessArgs>(
			$"{{\"environment-name\":\"dev\",\"process-name\":\"UsrA\",\"processName\":\"UsrB\",\"operations\":{Operations}}}"));

		// Assert
		refusedAsUnknown.Should().OnlyContain(result => result.ExitCode == 1 && TextOf(result).Contains("Unknown args"),
			because: "a write never runs on a reading guessed from a value's shape, nor on a caption it cannot address");
		ambiguous.ExitCode.Should().Be(1, because: "two names for one process are a caller error");
		TextOf(ambiguous).Should().Contain("Ambiguous process identity").And.Contain("Nothing was changed",
			because: "the refusal says nothing was written");
		command.Captured.Should().BeNull(because: "nothing may be written for a refused call");
	}

	[Test]
	[Category("Unit")]
	[Description("Review: modify-business-process-as-new-version follows the same identity rule as modify-business-process.")]
	public void ModifyProcessAsNewVersion_ShouldAcceptIdentitySynonyms() {
		// Arrange
		FakeNewVersionCommand command = new();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<ModifyProcessAsNewVersionCommand>(Arg.Any<ModifyProcessAsNewVersionOptions>()).Returns(command);
		ModifyProcessAsNewVersionTool tool = new(command, ConsoleLogger.Instance, resolver);

		// Act
		CommandExecutionResult result = tool.ModifyProcessAsNewVersion(Bind<ModifyProcessAsNewVersionArgs>(
			$"{{\"environment-name\":\"dev\",\"processName\":\"UsrOrder_Flow\",\"operations\":{Operations}}}"));

		// Assert
		result.ExitCode.Should().Be(0, because: "the family shares one identity rule");
		command.Captured!.ProcessName.Should().Be("UsrOrder_Flow", because: "processName is read as process-name");
		TextOf(result).Should().Contain("'processName' was accepted as 'process-name'", because: "the response names the canonical argument");
	}

	[Test]
	[Category("Unit")]
	[Description("Review: a flat packageName next to the top-level package-name override is refused unbuilt, not resolved in favour of the override.")]
	public void CreateBusinessProcess_ShouldRefuse_FlatPackageNameNextToTheOverride() {
		// Arrange
		(CreateBusinessProcessTool tool, FakeCreateCommand command) = CreateTool();

		// Act
		CommandExecutionResult result = tool.CreateBusinessProcess(Bind<CreateBusinessProcessArgs>(
			$"{{\"environment-name\":\"dev\",\"package-name\":\"Other\",{Descriptor[1..]}"));
		CommandExecutionResult canonical = tool.CreateBusinessProcess(Bind<CreateBusinessProcessArgs>(
			$"{{\"environment-name\":\"dev\",\"package-name\":\"Other\",\"descriptor\":{Descriptor}}}"));

		// Assert
		result.ExitCode.Should().Be(1, because: "in a flat call both keys mean the package, so the process could land in the wrong one");
		TextOf(result).Should().Contain("Ambiguous package", because: "the refusal names the clash");
		canonical.ExitCode.Should().Be(0, because: "in the canonical shape package-name is the documented override");
		command.Captured!.PackageName.Should().Be("Other", because: "only the canonical call was built, with its override");
	}

	[Test]
	[Category("Unit")]
	[Description("Review: 'process' holding text is an identity, never a document: create-business-process and validate-process-graph refuse it, naming the tools that take an identity.")]
	public void ProcessAsText_ShouldBeRefused_WhereADocumentIsExpected() {
		// Arrange
		(CreateBusinessProcessTool tool, FakeCreateCommand command) = CreateTool();

		// Act
		CommandExecutionResult create = tool.CreateBusinessProcess(Bind<CreateBusinessProcessArgs>(
			"{\"environment-name\":\"dev\",\"process\":\"UsrOrder_Flow\"}"));
		ValidateProcessGraphResponse validate = _validateTool.Validate(Bind<ValidateProcessGraphArgs>(
			"{\"environment-name\":\"dev\",\"process\":\"UsrOrder_Flow\"}"));

		// Assert
		create.ExitCode.Should().Be(1, because: "a process name is not a descriptor");
		TextOf(create).Should().Contain("holds text, not a process object").And.Contain("describe-business-process",
			because: "the refusal points at the tools that take a process name");
		command.Captured.Should().BeNull(because: "nothing is built from a name");
		validate.Success.Should().BeFalse(because: "a process name is not a graph");
		validate.Error.Should().Contain("holds text, not a graph object").And.Contain("describe-business-process",
			because: "this tool never reads a process from the environment, and the refusal says which one does");
		ValidateProcessGraphResponse broken = _validateTool.Validate(Bind<ValidateProcessGraphArgs>(
			"{\"environment-name\":\"dev\",\"graph\":\"{\\\"nodes\\\":[\"}"));
		broken.Error.Should().Contain("not valid JSON").And.NotContain("holds text",
			because: "a broken JSON document is reported as broken JSON, not as a process name");
	}

	[Test]
	[Category("Unit")]
	[Description("Review: a graph read from a descriptor's elements/flows says that element configuration was not checked; a nodes/edges wrapper does not.")]
	public void Validate_ShouldSayElementConfigurationWasNotChecked_WhenTheGraphCameFromADescriptor() {
		// Act
		ValidateProcessGraphResponse fromDescriptor = _validateTool.Validate(Bind<ValidateProcessGraphArgs>(
			$"{{\"environment-name\":\"dev\",\"descriptor\":{Descriptor}}}"));
		ValidateProcessGraphResponse fromGraph = _validateTool.Validate(Bind<ValidateProcessGraphArgs>(
			$"{{\"environment-name\":\"dev\",\"graph\":{Graph}}}"));

		// Assert
		fromDescriptor.Success.Should().BeTrue(because: $"the descriptor's graph is valid: {fromDescriptor.Error}");
		fromDescriptor.Warnings.Should().Contain(ProcessToolArgumentAliases.DescriptorOnlyGraphCheckedNote,
			because: "'accepted as the graph' must not read as 'the descriptor passed'");
		fromGraph.Warnings.Should().NotContain(ProcessToolArgumentAliases.DescriptorOnlyGraphCheckedNote,
			because: "a nodes/edges graph carries no element configuration to leave unchecked");
	}

	[TestCase("descriptor")]
	[TestCase("process")]
	[Category("Unit")]
	[Description("QA ENG-103025: a double wrapper around an EMPTY object is refused with the mini-contract, never built as an empty process.")]
	public void CreateBusinessProcess_ShouldRefuse_ADoubleWrapperAroundNothing(string innerKey) {
		// Arrange
		(CreateBusinessProcessTool tool, FakeCreateCommand command) = CreateTool();

		// Act
		CommandExecutionResult result = tool.CreateBusinessProcess(Bind<CreateBusinessProcessArgs>(
			$"{{\"environment-name\":\"dev\",\"descriptor\":{{\"{innerKey}\":{{}}}}}}"));

		// Assert
		result.ExitCode.Should().Be(1, because: "there is no process to build");
		TextOf(result).Should().Contain("it is empty").And.Contain("Minimal valid arguments:",
			because: "the caller gets the shape of a buildable descriptor");
		command.Captured.Should().BeNull(because: "an empty process must never reach the build");
	}
}
