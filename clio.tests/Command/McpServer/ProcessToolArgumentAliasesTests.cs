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
		response.Error.Should().Contain("Ambiguous graph");
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
		first.Error.Should().Contain("Ambiguous graph");
		second.Success.Should().BeFalse(because: "'graph' and flat 'elements' both claim to be the graph");
		second.Error.Should().Contain("Ambiguous graph");
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
		response.Error.Should().Contain("Unknown args").And.Contain("process-name");
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
		command.Captured!.ProcessName.Should().Be(name);
		command.Captured.ProcessUid.Should().Be(uid);
		command.Captured.ProcessCaption.Should().Be(caption);
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
		TextOf(first).Should().Contain("Ambiguous process identity");
		second.ExitCode.Should().Be(1);
		TextOf(second).Should().Contain("Ambiguous process identity");
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
		command.Captured.Should().BeNull();
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
		TextOf(result).Should().Contain($"'{key}' was accepted as 'descriptor'");
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
		built.RootElement.GetProperty("caption").GetString().Should().Be("Demo");
		built.RootElement.GetProperty("packageName").GetString().Should().Be("Custom");
		built.RootElement.GetProperty("elements").GetArrayLength().Should().Be(2);
		built.RootElement.GetProperty("flows").GetArrayLength().Should().Be(1);
		TextOf(result).Should().Contain("wrapped into 'descriptor'");
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
			$"{{\"environment-name\":\"dev\",\"process\":{Descriptor},\"caption\":\"x\"}}"
		];

		foreach (string body in bodies) {
			// Act
			CommandExecutionResult result = tool.CreateBusinessProcess(Bind<CreateBusinessProcessArgs>(body));

			// Assert
			result.ExitCode.Should().Be(1, because: "an ambiguous descriptor is a caller error: " + body);
			TextOf(result).Should().Contain("Ambiguous descriptor");
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
		result.ExitCode.Should().Be(1);
		TextOf(result).Should().Contain("Unknown args");
		command.Captured.Should().BeNull();
	}
}
