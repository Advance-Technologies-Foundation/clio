using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Command.ProcessModel;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-100153: the process-designer tools accept their JSON-document argument - the create descriptor, the
/// modify operations - EITHER as the JSON value itself or as a string holding its JSON text.
/// </summary>
/// <remarks>
/// Every call here goes through <see cref="McpServerTool.Create(System.Reflection.MethodInfo, object,
/// McpServerToolCreateOptions)"/> on the PRODUCTION serializer options and is invoked with a wire-shaped
/// <c>{"args":{...}}</c> payload, because the defect being fixed lived in the binder: an object descriptor was
/// refused before the tool body ran. A test that constructs the args record directly would bypass exactly
/// that layer and prove nothing about it.
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ProcessDesignerJsonDocumentArgumentTests {

	private const string Descriptor =
		"{\"name\":\"UsrAccount_Onboard\",\"packageName\":\"Custom\",\"elements\":[],\"flows\":[]}";

	private const string Operations =
		"[{\"op\":\"removeElement\",\"elementName\":\"NotifyAccountOwner\"}]";

	private static readonly JsonSerializerOptions WireOptions = Clio.BindingsModule.CreateMcpSerializerOptions();

	[Test]
	[Category("Unit")]
	[Description("create-business-process binds a descriptor sent as a JSON OBJECT and hands the command that same document - the call an agent could not make before ENG-100153.")]
	public async Task CreateBusinessProcess_Should_AcceptTheDescriptorAsAnObject() {
		// Arrange
		FakeCreateCommand command = new();
		McpServerTool tool = CreateTool(command);

		// Act
		CallToolResult result = await InvokeAsync(tool, CreateBusinessProcessTool.CreateBusinessProcessToolName,
			$"{{\"environment-name\":\"sandbox\",\"descriptor\":{Descriptor}}}");

		// Assert
		result.IsError.Should().NotBe(true,
			because: "an object descriptor is the natural call and must bind rather than be refused as a non-string");
		command.CapturedOptions.Should().NotBeNull(
			because: "the call must reach the command, which is where the descriptor is parsed and built");
		JsonDocumentsShouldBeEqual(command.CapturedOptions!.DescriptorJson, Descriptor,
			because: "the command must receive the document the caller sent, not a re-shaped copy of it");
	}

	[Test]
	[Category("Unit")]
	[Description("create-business-process keeps accepting the descriptor as a JSON STRING and forwards that string byte-for-byte, so every existing caller keeps working unchanged.")]
	public async Task CreateBusinessProcess_Should_KeepAcceptingTheDescriptorAsAString() {
		// Arrange
		FakeCreateCommand command = new();
		McpServerTool tool = CreateTool(command);
		string encoded = JsonSerializer.Serialize(Descriptor);

		// Act
		CallToolResult result = await InvokeAsync(tool, CreateBusinessProcessTool.CreateBusinessProcessToolName,
			$"{{\"environment-name\":\"sandbox\",\"descriptor\":{encoded}}}");

		// Assert
		result.IsError.Should().NotBe(true,
			because: "the string form is the long-standing contract and must stay accepted");
		command.CapturedOptions.Should().NotBeNull(
			because: "the call must reach the command before what it received can be asserted");
		command.CapturedOptions!.DescriptorJson.Should().Be(Descriptor,
			because: "a string descriptor is forwarded verbatim - the command, not the tool, parses and words its errors");
	}

	[TestCase("[]", "a JSON array")]
	[TestCase("42", "a JSON number")]
	[TestCase("true", "a JSON boolean")]
	[TestCase("false", "a JSON boolean")]
	[Category("Unit")]
	[Description("create-business-process refuses a descriptor that is neither an object nor a string, naming both accepted forms, without dispatching the command.")]
	public async Task CreateBusinessProcess_Should_RefuseADescriptorOfAnotherKind(string descriptor, string received) {
		// Arrange
		FakeCreateCommand command = new();
		McpServerTool tool = CreateTool(command);

		// Act
		CallToolResult result = await InvokeAsync(tool, CreateBusinessProcessTool.CreateBusinessProcessToolName,
			$"{{\"environment-name\":\"sandbox\",\"descriptor\":{descriptor}}}");

		// Assert
		command.CapturedOptions.Should().BeNull(
			because: "a descriptor of the wrong kind is refused before anything is built");
		TextOf(result).Should().Contain("descriptor must be a JSON object, or a string holding one",
			because: "the refusal names both accepted forms so the caller can correct the call in one step");
		TextOf(result).Should().Contain(received.Replace("a JSON ", "Received a JSON "),
			because: "the refusal says what was received, which is what distinguishes it from an empty descriptor");
	}

	[Test]
	[Category("Unit")]
	[Description("modify-business-process binds operations sent as a JSON ARRAY and hands the command that same array.")]
	public async Task ModifyBusinessProcess_Should_AcceptTheOperationsAsAnArray() {
		// Arrange
		FakeModifyCommand command = new();
		McpServerTool tool = ModifyTool(command);

		// Act
		CallToolResult result = await InvokeAsync(tool, ModifyBusinessProcessTool.ModifyBusinessProcessToolName,
			$"{{\"environment-name\":\"sandbox\",\"process-name\":\"UsrAccount_Onboard\",\"operations\":{Operations}}}");

		// Assert
		result.IsError.Should().NotBe(true,
			because: "an operations array is the natural call and must bind rather than be refused as a non-string");
		command.CapturedOptions.Should().NotBeNull(
			because: "the call must reach the command before what it received can be asserted");
		JsonDocumentsShouldBeEqual(command.CapturedOptions!.OperationsJson, Operations,
			because: "the command must receive the operations the caller sent");
	}

	[Test]
	[Category("Unit")]
	[Description("modify-business-process keeps accepting operations as a JSON STRING and forwards it verbatim.")]
	public async Task ModifyBusinessProcess_Should_KeepAcceptingTheOperationsAsAString() {
		// Arrange
		FakeModifyCommand command = new();
		McpServerTool tool = ModifyTool(command);
		string encoded = JsonSerializer.Serialize(Operations);

		// Act
		CallToolResult result = await InvokeAsync(tool, ModifyBusinessProcessTool.ModifyBusinessProcessToolName,
			$"{{\"environment-name\":\"sandbox\",\"process-name\":\"UsrAccount_Onboard\",\"operations\":{encoded}}}");

		// Assert
		result.IsError.Should().NotBe(true,
			because: "the string form is the long-standing contract and must stay accepted");
		command.CapturedOptions.Should().NotBeNull(
			because: "the call must reach the command before what it received can be asserted");
		command.CapturedOptions!.OperationsJson.Should().Be(Operations,
			because: "a string is forwarded verbatim");
	}

	[Test]
	[Category("Unit")]
	[Description("modify-business-process refuses operations sent as an OBJECT - the right container kind is an array - naming both accepted forms.")]
	public async Task ModifyBusinessProcess_Should_RefuseOperationsSentAsAnObject() {
		// Arrange
		FakeModifyCommand command = new();
		McpServerTool tool = ModifyTool(command);

		// Act
		CallToolResult result = await InvokeAsync(tool, ModifyBusinessProcessTool.ModifyBusinessProcessToolName,
			"{\"environment-name\":\"sandbox\",\"process-name\":\"UsrAccount_Onboard\",\"operations\":{\"op\":\"removeElement\"}}");

		// Assert
		command.CapturedOptions.Should().BeNull(
			because: "a single operation object is not an operations array, and guessing a wrapper would hide the mistake");
		TextOf(result).Should().Contain("operations must be a JSON array, or a string holding one",
			because: "the refusal names both accepted forms");
	}

	[Test]
	[Category("Unit")]
	[Description("modify-business-process-as-new-version binds operations sent as a JSON ARRAY and hands the command that same array.")]
	public async Task ModifyProcessAsNewVersion_Should_AcceptTheOperationsAsAnArray() {
		// Arrange
		FakeNewVersionCommand command = new();
		McpServerTool tool = NewVersionTool(command);

		// Act
		CallToolResult result = await InvokeAsync(tool, ModifyProcessAsNewVersionTool.ModifyProcessAsNewVersionToolName,
			$"{{\"environment-name\":\"sandbox\",\"process-name\":\"UsrAccount_Onboard\",\"operations\":{Operations}}}");

		// Assert
		result.IsError.Should().NotBe(true,
			because: "an operations array is the natural call and must bind rather than be refused as a non-string");
		command.CapturedOptions.Should().NotBeNull(
			because: "the call must reach the command before what it received can be asserted");
		JsonDocumentsShouldBeEqual(command.CapturedOptions!.OperationsJson, Operations,
			because: "the command must receive the operations the caller sent");
	}

	[TestCase("", Description = "operations omitted")]
	[TestCase(",\"operations\":null", Description = "operations null")]
	[TestCase(",\"operations\":\"\"", Description = "operations an empty string")]
	[TestCase(",\"operations\":\"   \"", Description = "operations a whitespace-only string")]
	[Category("Unit")]
	[Description("modify-business-process-as-new-version still treats absent, null and empty-string operations as the snapshot form, forwarding no operations rather than refusing the call.")]
	public async Task ModifyProcessAsNewVersion_Should_KeepTheSnapshotForm_WhenOperationsAreAbsent(string operations) {
		// Arrange
		FakeNewVersionCommand command = new();
		McpServerTool tool = NewVersionTool(command);

		// Act
		CallToolResult result = await InvokeAsync(tool, ModifyProcessAsNewVersionTool.ModifyProcessAsNewVersionToolName,
			$"{{\"environment-name\":\"sandbox\",\"process-name\":\"UsrAccount_Onboard\"{operations}}}");

		// Assert
		result.IsError.Should().NotBe(true,
			because: "no operations is the documented way to snapshot the source unchanged as a new version");
		command.CapturedOptions.Should().NotBeNull(
			because: "the call must reach the command before what it received can be asserted");
		command.CapturedOptions!.OperationsJson.Should().BeEmpty(
			because: "the snapshot form reaches the command as no operations, exactly as before ENG-100153");
	}

	[TestCase(JsonValueKind.String)]
	[TestCase(JsonValueKind.Number)]
	[TestCase(JsonValueKind.Undefined)]
	[Category("Unit")]
	[Description("The JSON-document reader accepts only Object or Array as the expected kind, and throws on any other: a caller passing String would make every string argument 'the expected kind' and silently skip the kind check.")]
	public void TryReadJsonDocumentArgument_Should_Throw_WhenTheExpectedKindIsNotAContainer(JsonValueKind expectedKind) {
		// Arrange
		JsonElement value = JsonDocument.Parse("{}").RootElement.Clone();

		// Act
		System.Action act = () => McpToolArgumentSupport.TryReadJsonDocumentArgument(value, expectedKind, "descriptor",
			out _, out _);

		// Assert
		act.Should().Throw<System.ArgumentOutOfRangeException>(
			because: "a non-container expected kind is a programming error, not a caller mistake to word back");
	}

	private static McpServerTool CreateTool(FakeCreateCommand command) {
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<CreateBusinessProcessCommand>(Arg.Any<CreateBusinessProcessOptions>()).Returns(command);
		return McpServerTool.Create(
			typeof(CreateBusinessProcessTool).GetMethod(nameof(CreateBusinessProcessTool.CreateBusinessProcess))!,
			target: new CreateBusinessProcessTool(command, ConsoleLogger.Instance, resolver),
			new McpServerToolCreateOptions { SerializerOptions = WireOptions });
	}

	private static McpServerTool ModifyTool(FakeModifyCommand command) {
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<ModifyBusinessProcessCommand>(Arg.Any<ModifyBusinessProcessOptions>()).Returns(command);
		return McpServerTool.Create(
			typeof(ModifyBusinessProcessTool).GetMethod(nameof(ModifyBusinessProcessTool.ModifyBusinessProcess))!,
			target: new ModifyBusinessProcessTool(command, ConsoleLogger.Instance, resolver),
			new McpServerToolCreateOptions { SerializerOptions = WireOptions });
	}

	private static McpServerTool NewVersionTool(FakeNewVersionCommand command) {
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<ModifyProcessAsNewVersionCommand>(Arg.Any<ModifyProcessAsNewVersionOptions>())
			.Returns(command);
		return McpServerTool.Create(
			typeof(ModifyProcessAsNewVersionTool).GetMethod(nameof(ModifyProcessAsNewVersionTool.ModifyProcessAsNewVersion))!,
			target: new ModifyProcessAsNewVersionTool(command, ConsoleLogger.Instance, resolver),
			new McpServerToolCreateOptions { SerializerOptions = WireOptions });
	}

	private static async Task<CallToolResult> InvokeAsync(McpServerTool tool, string toolName, string argsJson) {
		using JsonDocument args = JsonDocument.Parse(argsJson);
		Dictionary<string, JsonElement> arguments = new() { ["args"] = args.RootElement.Clone() };
		RequestContext<CallToolRequestParams> context =
			McpRequestContextTestFactory.CreateCallToolContext(toolName, arguments);
		context.MatchedPrimitive = tool;
		return await tool.InvokeAsync(context, CancellationToken.None);
	}

	private static string TextOf(CallToolResult result) =>
		string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

	private static void JsonDocumentsShouldBeEqual(string actual, string expected, string because) {
		using JsonDocument actualDocument = JsonDocument.Parse(actual);
		using JsonDocument expectedDocument = JsonDocument.Parse(expected);
		JsonElement.DeepEquals(actualDocument.RootElement, expectedDocument.RootElement).Should().BeTrue(
			because: because);
	}

	private sealed class FakeCreateCommand : CreateBusinessProcessCommand {
		public CreateBusinessProcessOptions? CapturedOptions { get; private set; }

		public FakeCreateCommand()
			: base(Substitute.For<ICreateBusinessProcessService>(), Substitute.For<IProcessDescriber>(),
				Substitute.For<ILogger>()) {
		}

		public override int Execute(CreateBusinessProcessOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}

	private sealed class FakeModifyCommand : ModifyBusinessProcessCommand {
		public ModifyBusinessProcessOptions? CapturedOptions { get; private set; }

		public FakeModifyCommand()
			: base(Substitute.For<IModifyBusinessProcessService>(), Substitute.For<IProcessDescriber>(),
				Substitute.For<ILogger>()) {
		}

		public override int Execute(ModifyBusinessProcessOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}

	private sealed class FakeNewVersionCommand : ModifyProcessAsNewVersionCommand {
		public ModifyProcessAsNewVersionOptions? CapturedOptions { get; private set; }

		public FakeNewVersionCommand()
			: base(Substitute.For<IModifyProcessAsNewVersionService>(), Substitute.For<ILogger>()) {
		}

		public override int Execute(ModifyProcessAsNewVersionOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
