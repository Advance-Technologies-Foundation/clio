using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the server-side refusal of undeclared request keys (ENG-95244): CrtProcessBuilder refuses a
/// build, modify or save-as-new-version request carrying a key its contract does not declare, naming the path and the
/// key that was meant, and saves nothing. NOT in CI - the process-designer fixtures need CrtProcessBuilder on a stand.
/// </summary>
/// <remarks>
/// <para>The refusal lives in the PACKAGE, not in clio: clio forwards the payload and relays the server's message. So
/// these tests need CrtProcessBuilder 1.6.6.36 or later on the stand - an older package drops such a key in silence
/// exactly as before - and they are ignored, with the reason, when it is behind.</para>
/// <para>They are also the only check that a HOST really keeps undeclared keys: the package can prove its own reader
/// works, but not that the platform binds the request with a serializer that fills the extension data. Run them on
/// both a .NET Framework and a .NET Core stand before a release.</para>
/// <para>The describe-echo case feeds a described element back unchanged: the refusal must name its read-only fields
/// as such, once per place, and list none of them as a typo. That a refusal saves nothing is the package handler
/// tests' claim (the repository is never reached); a start event offers no declared field whose change would prove it
/// here.</para>
/// <para><c>lable</c> on a flow and on a <c>setFlow</c> operation are the misspellings the ENG-95244 stand probe
/// measured (the operation used to answer "1 operation(s) applied" and change nothing); <c>tpye</c> is the parameter
/// case of the package's own unit tests.</para>
/// </remarks>
[TestFixture]
[AllureNUnit]
[AllureFeature("process-request-unknown-keys")]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class ProcessRequestUnknownKeyE2ETests {

	private const string CreateToolName = "create-business-process";
	private const string ModifyToolName = "modify-business-process";
	private const string AsNewVersionToolName = "modify-business-process-as-new-version";
	private const string DescribeToolName = "describe-business-process";
	private const string MinimumPackageVersion = "1.6.6.36";
	private const string Subject = "unknown request keys";
	private const string Refused = "The request was refused and nothing was saved";

	[Test]
	[Description("Over the real MCP path, a create descriptor carrying flows[0].lable is refused by CrtProcessBuilder with the path and 'label', and no process is created: describe cannot find it afterwards.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process is refused for an undeclared descriptor key and creates nothing")]
	public async Task CreateBusinessProcess_Should_BeRefused_WhenADescriptorCarriesAnUndeclaredKey() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync(Subject, MinimumPackageVersion);
		string processName = $"UsrClioBpUnknownKeyE2e{Guid.NewGuid():N}";

		// Act
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(
			await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = TwoElementDescriptor(processName, "lable")
			}));

		// Assert
		string output = OutputOf(execution);
		execution.ExitCode.Should().Be(1, because: "the server refuses a request with an undeclared key");
		output.Should().Contain(Refused, because: "the caller must learn that nothing happened");
		output.Should().Contain("flows[0].lable - did you mean 'label'?",
			because: "the refusal names the path and the key that was meant");
		JsonSerializer.Serialize(await DescribeAsync(context, processName)).Should().Contain("was not found",
			because: "a refused build creates no process at all");
	}

	[Test]
	[Description("The CONTROL for the test above: the same descriptor with the correctly spelled 'label' builds, so the refusal is about the key and not about the descriptor.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process builds a descriptor whose keys are all declared")]
	public async Task CreateBusinessProcess_Should_Build_WhenEveryKeyIsDeclared() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync(Subject, MinimumPackageVersion);
		string processName = $"UsrClioBpUnknownKeyOkE2e{Guid.NewGuid():N}";

		// Act
		CommandExecutionEnvelope execution = await CreateAsync(context, processName, "label");

		// Assert
		string output = OutputOf(execution);
		execution.ExitCode.Should().Be(0, because: "a descriptor of declared keys is an ordinary build");
		output.Should().Contain("created (UId:", because: "the build reports the schema it created");
		output.Should().NotContain(Refused, because: "a declared key must never be refused");
	}

	[Test]
	[Description("Over the real MCP path, a setFlow operation carrying 'lable' - measured to answer '1 operation(s) applied' and change nothing - is refused naming operations[0].lable, and the flow keeps its label.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process is refused for an undeclared operation key and changes nothing")]
	public async Task ModifyBusinessProcess_Should_BeRefused_WhenAnOperationCarriesAnUndeclaredKey() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync(Subject, MinimumPackageVersion);
		string processName = $"UsrClioBpUnknownKeyModE2e{Guid.NewGuid():N}";
		(await CreateAsync(context, processName, "label")).ExitCode.Should().Be(0,
			because: "the process to modify must exist");

		// Act
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(
			await CallToolAsync(context, ModifyToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = """[{"op":"setFlow","source":"Start1","target":"End1","kind":"sequence","lable":"Renamed"}]"""
			}));

		// Assert
		string output = OutputOf(execution);
		execution.ExitCode.Should().Be(1, because: "the server refuses an operation with an undeclared key");
		output.Should().Contain(Refused, because: "the caller must learn that nothing happened");
		output.Should().Contain("operations[0].lable - did you mean 'label'?",
			because: "the refusal names the operation key and the key that was meant");
		JsonObject graph = DescribedProcessGraph.Read(await DescribeAsync(context, processName));
		graph["flows"]![0]!["label"]!.GetValue<string>().Should().Be("Go",
			because: "a refused edit leaves the flow label as it was");
	}

	[Test]
	[Description("Over the real MCP path, an element copied verbatim from describe into a setElement elementUpdate is refused as a whole: its read-only fields are named once per place under 'Read-only fields copied from describe', and no key is listed one by one as a typo.")]
	[AllureTag(ModifyToolName)]
	[AllureTag(DescribeToolName)]
	[AllureName("modify-business-process refuses a described element fed back verbatim and names its read-only fields")]
	public async Task ModifyBusinessProcess_Should_NameReadOnlyFields_WhenADescribedElementIsFedBack() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync(Subject, MinimumPackageVersion);
		string processName = $"UsrClioBpUnknownKeyEchoE2e{Guid.NewGuid():N}";
		(await CreateAsync(context, processName, "label")).ExitCode.Should().Be(0,
			because: "the process to modify must exist");
		JsonObject described = DescribedProcessGraph.Read(await DescribeAsync(context, processName));
		var operations = new JsonArray {
			new JsonObject {
				["op"] = "setElement",
				["elementName"] = "Start1",
				["elementUpdate"] = ElementNamed(described, "Start1").DeepClone()
			}
		};

		// Act
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(
			await CallToolAsync(context, ModifyToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = operations.ToJsonString()
			}));

		// Assert
		string output = OutputOf(execution);
		execution.ExitCode.Should().Be(1, because: "a described element carries fields a write does not take");
		output.Should().Contain(Refused, because: "the caller must learn that nothing happened");
		output.Should().Contain("Read-only fields copied from describe",
			because: "the fields describe added are named as read-only, not as typos");
		output.Should().MatchRegex(@"operations\[\]\.elementUpdate: [^;.]*\buid\b",
			because: "the read-only fields are named once per place, the element's uid among them");
		output.Should().NotMatchRegex(@"operations\[0\]\.\S+ - ",
			because: "a verbatim describe echo holds no misspelled key, so no key may be listed one by one with a hint");
		output.Should().NotContain("more not listed",
			because: "nothing a describe echo carries is pushed past the listing cap");
	}

	[Test]
	[Description("Over the real MCP path, modify-business-process-as-new-version with an undeclared operation key is refused, and the family still holds only the source version afterwards.")]
	[AllureTag(AsNewVersionToolName)]
	[AllureName("modify-business-process-as-new-version is refused for an undeclared key and creates no version")]
	public async Task ModifyAsNewVersion_Should_BeRefusedWithoutAVersion_WhenAnOperationCarriesAnUndeclaredKey() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync(Subject, MinimumPackageVersion);
		string processName = $"UsrClioBpUnknownKeyVerE2e{Guid.NewGuid():N}";
		(await CreateAsync(context, processName, "label")).ExitCode.Should().Be(0,
			because: "the source process must exist");

		// Act
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(
			await CallToolAsync(context, AsNewVersionToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = """[{"op":"addParameter","parameter":{"name":"Amount","tpye":"Integer"}}]"""
			}));

		// Assert
		string output = OutputOf(execution);
		execution.ExitCode.Should().Be(1, because: "the server refuses an operation with an undeclared key");
		output.Should().Contain(Refused, because: "the caller must learn that nothing happened");
		output.Should().Contain("operations[0].parameter.tpye - did you mean 'type'?",
			because: "the refusal names the parameter key and the key that was meant");
		JsonObject graph = DescribedProcessGraph.Read(await DescribeAsync(context, processName));
		JsonArray versions = graph["versions"].Should().BeOfType<JsonArray>(
			because: "describe reports the version family of the process").Subject;
		versions.Count.Should().Be(1, because: "no version was persisted, so the family still holds only the source");
	}

	private static JsonObject ElementNamed(JsonObject graph, string name) {
		JsonArray elements = graph["elements"].Should().BeOfType<JsonArray>(
			because: "describe reports the elements of the process").Subject;
		return elements.OfType<JsonObject>().Should().ContainSingle(
			element => element["name"] != null && element["name"]!.GetValue<string>() == name,
			because: $"the described process holds the element {name}").Subject;
	}

	private static string TwoElementDescriptor(string processName, string labelKey) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio unknown keys E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "Start1", "type": "startEvent" },
		    { "name": "End1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "Start1", "target": "End1", "{{labelKey}}": "Go" }
		  ]
		}
		""";

	// The DECODED log lines: serializing the raw result would escape the apostrophes in "did you mean 'label'?".
	private static string OutputOf(CommandExecutionEnvelope execution) =>
		string.Join("\n", (execution.Output ?? []).Select(message => message.Value));

	private static async Task<CommandExecutionEnvelope> CreateAsync(ProcessDesignerArrangeContext context,
			string processName, string labelKey) =>
		McpCommandExecutionParser.Extract(await CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = TwoElementDescriptor(processName, labelKey)
			}));

	private static Task<CallToolResult> DescribeAsync(ProcessDesignerArrangeContext context, string processName) =>
		CallToolAsync(context, DescribeToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName
		});

	private static async Task<CallToolResult> CallToolAsync(ProcessDesignerArrangeContext context, string toolName,
			Dictionary<string, object?> args) =>
		await context.Session.CallToolAsync(toolName, new Dictionary<string, object?> { ["args"] = args },
			context.CancellationTokenSource.Token);
}
