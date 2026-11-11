using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Common;
using Clio.Mcp.E2E.Support;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using Clio.Package;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using static Clio.Mcp.E2E.Support.Mcp.ProcessDesignerE2ESupport;
using static Clio.Mcp.E2E.Support.Mcp.ProcessDesignerStoredReads;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the Call web service element (<c>type:"webService"</c>, the <c>webService</c> block) over
/// the real MCP path. NOT in CI - run manually against stand <c>Creatio</c>, one framework at a time
/// (<c>-f net10.0</c>, then <c>-f net8.0</c>).
/// <para>What only this level can show is the wire and the platform: the block travels as JSON members the server
/// contract must declare, the service and method resolve against the stand's registered services, the element is
/// stored in the designer's shape with its result-mapping rows, and describe reads the stored tree back from a real
/// saved schema. Every unit suite constructs these in C#.</para>
/// <para>Stand prerequisites, created on stand <c>Creatio</c> on 2026-11-03: the services
/// <c>UsrWscEchoService</c> (method <c>UsrWscEcho</c>), <c>UsrWscDriftService</c> (methods <c>UsrWscDriftGone</c>
/// and <c>UsrWscOrders2</c>) and <c>UsrWscDeadService</c> (method <c>UsrWscDead</c>), and the designer-built process
/// <c>UsrWscEm2CaptureA</c>. A case that needs an absent one is Ignored with the build instructions. Every other case
/// builds its own processes and removes them, newest first, in <c>finally</c>. Strictly sequential: schema writes in a
/// parallel burst take a .NET Framework stand's app pool down.</para>
/// <para>Every create, modify, describe and run reply goes through a helper that asserts the <c>X-Wsc-Probe</c>
/// header default is absent from it: no data stored in the web service itself reaches builder output.</para>
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class WebServiceElementToolE2ETests {

	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;
	private const string ModifyToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;
	private const string NewVersionToolName = ModifyProcessAsNewVersionTool.ModifyProcessAsNewVersionToolName;

	/// <summary>
	/// The CrtProcessBuilder the fixture runs against: 1.6.6.150, the first cut that ships the <c>webService</c> block
	/// and the create and modify floor when it was set (1.6.6.146-1.6.6.149 were cuts of this work that were never released). Below it
	/// every case is Ignored, because an older package does not declare the block and refuses it as an unknown key.
	/// Raise it when a case starts asserting newer package behaviour, not on every rebundle.
	/// </summary>
	private const string MinimumPackageVersion = "1.6.6.150";

	/// <summary>What the skip message calls these tests.</summary>
	private const string Subject = "Call web service element";

	private const string EchoService = "UsrWscEchoService";
	private const string EchoMethod = "UsrWscEcho";
	private const string DriftService = "UsrWscDriftService";
	private const string DriftMethod = "UsrWscDriftGone";
	private const string OrdersMethod = "UsrWscOrders2";
	private const string DeadService = "UsrWscDeadService";
	private const string DeadMethod = "UsrWscDead";

	/// <summary>The designer-built fixture of TC-I-07 (measurement EM2).</summary>
	private const string DesignerFixtureName = "UsrWscEm2CaptureA";

	/// <summary>
	/// The constant default of the <c>X-Wsc-Probe</c> header on the test services: service-side data that must never
	/// reach builder output.
	/// </summary>
	private const string HeaderDefault = "wsc-probe";

	/// <summary>The build token describe reports for a Call web service element.</summary>
	private const string WebServiceBuildType = "webservice";

	/// <summary>The platform type of a result-mapping row (<c>BK15</c>).</summary>
	private const string MappingRowType = "Terrasoft.Core.Process.ProcessSchemaMapping";

	/// <summary>The four untagged result parameters, in the order the designer writes them.</summary>
	private static readonly string[] ResultParameters = ["Success", "Response", "StatusCode", "RequestBody"];

	#region Methods: Tests - create and describe

	[Test]
	[Description("TC-I-01: one create builds a Call web service element on UsrWscEcho with the whole block (timeout 7 s, ignore errors) and four binding kinds in the same request - a process parameter, a constant, a nested item path and a RequestBody formula; describe reads the block back (service name and uid, method, timeout, ignoreErrors, both resolved flags) with every stored parameter, unbound inputs and the four result parameters included; the stored element carries IX1-IX4 and the nested items, the process carries one result-mapping row per result parameter, and the element's captions are stored as resources.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process stores the web service element in the designer's shape and describe reads it back")]
	public async Task CreateBusinessProcess_Should_StoreTheWebServiceElementInTheDesignersShape_AndDescribeItBack() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync(EchoService);
		List<string> created = [];
		try {
			string processName = NewProcessName("Shape");
			string descriptor = LinearDescriptor(processName, TextParametersJson,
				[("WS1", WebServiceElementJson("WS1", EchoService, EchoMethod, timeoutSeconds: 7, ignoreErrors: true))],
				"""
				[ { "elementName": "WS1", "elementParameter": "UsrWscText_In", "processParameter": "Title" },
				  { "elementName": "WS1", "elementParameter": "UsrWscReq_In", "value": "required" },
				  { "elementName": "WS1", "elementParameter": "UsrWscOrders_In.UsrWscOrdNo_In", "value": "N-1" },
				  { "elementName": "WS1", "elementParameter": "RequestBody", "expression": "\"{}\"" } ]
				""");

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName, descriptor);

			// Assert
			ShouldHaveBuilt(callResult, "a registered service and method with bindings on the generated names is an ordinary create");
			ShouldCarryNoServiceData(callResult);
			CallToolResult described = await ProcessDesignerE2EArrange.DescribeAsync(context, processName);
			ShouldCarryNoServiceData(described);
			JsonObject graph = DescribedProcessGraph.Read(described);
			JsonObject element = Element(graph, "WS1");
			Text(element, "buildType").Should().Be(WebServiceBuildType,
				because: "describe reports the build token of the Call web service element");
			JsonObject block = WebServiceBlock(element);
			Text(block, "service").Should().Be(EchoService, because: "the service reads back as the code the write side takes");
			Text(block, "serviceUId").Should().NotBeNullOrEmpty(because: "the stored manager-item UId reads back");
			Text(block, "method").Should().Be(EchoMethod, because: "the method reads back canonically spelled");
			block["timeoutSeconds"]!.GetValue<decimal>().Should().Be(7m, because: "the timeout reads back in seconds");
			block["ignoreErrors"]!.GetValue<bool>().Should().BeTrue(because: "the ignore-errors flag reads back");
			block["serviceResolved"]!.GetValue<bool>().Should().BeTrue(because: "the stored service resolves");
			block["methodResolved"]!.GetValue<bool>().Should().BeTrue(because: "the stored method resolves");
			IReadOnlyList<JsonObject> parameters = Parameters(block);
			parameters.Select(parameter => Text(parameter, "name")).Should().Contain(
				["UsrWscText_In", "UsrWscReq_In", "UsrWscOrders_In", "UsrWscFlag_In"],
				because: "every tagged input is reported, the unbound UsrWscFlag_In included");
			parameters.TakeLast(ResultParameters.Length).Select(parameter => Text(parameter, "name")).Should().Equal(
				ResultParameters, because: "the four result parameters come last, in the designer's order");
			JsonObject flag = Parameter(parameters, "UsrWscFlag_In");
			flag["bound"]!.GetValue<bool>().Should().BeFalse(because: "an unbound input is reported with bound false");
			Parameter(parameters, "UsrWscOrders_In")["items"].Should().BeOfType<JsonArray>(
				because: "an object array input carries its nested items");
			string processUId = Text(graph, "schemaUId")!;
			string elementUId = UId(element);
			string node = await ReadLastElementNodeAsync(context, processName, elementUId);
			JsonObject stored = StoredNode(node);
			StoredMember(stored, "IX2").GetValue<string>().Should().Be(EchoMethod, because: "IX2 stores the canonical method name");
			StoredMember(stored, "IX3").GetValue<int>().Should().Be(7000, because: "IX3 stores the timeout in milliseconds");
			StoredMember(stored, "IX4").GetValue<bool>().Should().BeTrue(because: "IX4 stores the ignore-errors flag directly");
			StoredMember(stored, "IX1").GetValue<string>().Should().Be(Text(block, "serviceUId"),
				because: "IX1 stores the service's manager-item UId that describe reports");
			node.Should().Contain("\"L18\"", because: "nested items are stored the way the designer stores them");
			node.Should().Contain("UsrWscIdsItemValue_In", because: "a scalar array carries its item value child");
			JsonObject metadata = await ReadLastMetaDataAsync(context, processName);
			MappingRowsOf(metadata, elementUId).Should().HaveCount(ResultParameters.Length,
				because: "the package writes one result-mapping row per result parameter, as the designer does");
			MappingRowsOf(metadata, elementUId).Should().OnlyContain(row => Text(row, "A3") == processUId,
				because: "a row owned by another schema is read by the designer as an inherited-element marker");
			IReadOnlyList<string> resources = await ReadElementResourceRowsAsync(context, processName, "WS1");
			resources.Should().Contain(row => row.Contains("Parameters.UsrWscText_In.Caption"),
				because: "the generated parameters carry their captions as resources");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-02: a create reads the two-level object-array response of UsrWscOrders2 into a declared Collection with item mappings on both levels; the build lands, and describe decodes the declared parameter's binding to the element output and its nested items.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process reads a web service response into a declared collection")]
	public async Task CreateBusinessProcess_Should_ReadTheResponseIntoADeclaredCollection() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync(DriftService);
		List<string> created = [];
		try {
			string processName = NewProcessName("Collection");
			string descriptor = LinearDescriptor(processName, OrdersParametersJson,
				[("WS1", WebServiceElementJson("WS1", DriftService, OrdersMethod))],
				"""
				[ { "targetProcessParameter": "Orders.No", "sourceElement": "WS1",
				    "sourceElementParameter": "UsrWscOrd2_Out.UsrWscOrd2No_Out" },
				  { "targetProcessParameter": "Orders.Lines.Sku", "sourceElement": "WS1",
				    "sourceElementParameter": "UsrWscOrd2_Out.UsrWscOrd2Lines_Out.UsrWscOrd2Sku_Out" } ]
				""");

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName, descriptor);

			// Assert
			ShouldHaveBuilt(callResult, "a declared two-level Collection read from a two-level response is the supported route");
			CallToolResult described = await ProcessDesignerE2EArrange.DescribeAsync(context, processName);
			ShouldCarryNoServiceData(described);
			JsonObject orders = ProcessParameter(DescribedProcessGraph.Read(described), "Orders");
			Text(orders, "sourceElement").Should().Be("WS1",
				because: "the declared Collection itself is bound to the element: an item mapping binds the collections above it");
			(Text(orders, "sourceElementParameter") ?? string.Empty).Should().Contain("UsrWscOrd2_Out",
				because: "the declared Collection reads the element's response list, not the element's own parameter tree");
			JsonObject lines = ItemProperty(orders, "Lines");
			Text(lines, "sourceElement").Should().Be("WS1", because: "the second level is bound to the element as well");
			(Text(lines, "sourceElementParameter") ?? string.Empty).Should().Contain("UsrWscOrd2Lines_Out",
				because: "the nested collection reads the nested response list");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	// The refusals are the package's own wording; each case asserts only what the AC promises: the name sent and the
	// list or the pointer that lets the caller pick a real one.
	[TestCase("""{ "name": "WS1", "type": "webService", "webService": { "service": "UsrWscNoSuchService", "method": "Any" } }""",
		new[] { "UsrWscNoSuchService", EchoService }, true)]
	[TestCase("""{ "name": "WS1", "type": "webService", "webService": { "service": "UsrWscEchoService", "method": "UsrWscNoSuchMethod" } }""",
		new[] { "UsrWscNoSuchMethod", EchoMethod }, true)]
	[TestCase("""{ "name": "WS1", "type": "userTask", "userTaskName": "WebServiceUserTask" }""",
		new[] { "'WebServiceUserTask' is a legacy SOAP task", "with type webService" }, false)]
	[TestCase("""{ "name": "WS1", "type": "userTask", "userTaskName": "EmailTemplateUserTask", "webService": { "service": "UsrWscEchoService", "method": "UsrWscEcho" } }""",
		new[] { "carries a 'webService' configuration, which is only valid on a 'webservice' element" }, false)]
	[TestCase("""{ "name": "WS1", "type": "webService", "webService": { "service": "UsrWscEchoService", "method": "UsrWscEcho", "multiInstanceOptions": { "enabled": true } } }""",
		new[] { "Nothing was sent", "'webService.multiInstanceOptions'" }, false)]
	[TestCase("""{ "name": "WS1", "type": "webService", "webService": { "service": "UsrWscEchoService", "method": "UsrWscEcho", "timeoutSeconds": 1.5 } }""",
		new[] { "Nothing was sent", "'webService.timeoutSeconds' is 1.5" }, false)]
	[Description("TC-I-03: an unknown service (refused with the registered services), an unknown method (refused with the service's methods), a generic userTask naming WebServiceUserTask (refused with a pointer to the block) and a webService block on another element type are refused by the server; webService.multiInstanceOptions and a fractional timeoutSeconds are refused by clio's key check before anything is sent. Nothing is created in any case: describe of the process name answers that it was not found. Only the two cases that name a real service need it on the stand.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process refuses an unusable web service element and creates nothing")]
	public async Task CreateBusinessProcess_Should_RefuseAndCreateNothing(string elementJson, string[] expectedFragments,
			bool needsEchoService) {
		// Arrange
		await using ProcessDesignerArrangeContext context = needsEchoService ? await StartAsync(EchoService) : await StartAsync();
		List<string> created = [];
		try {
			string processName = NewProcessName("Refuse");
			string descriptor = LinearDescriptor(processName, TextParametersJson, [("WS1", elementJson)], "[]");

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName, descriptor);

			// Assert
			ShouldBeRefused(callResult, "an element that cannot call a registered service is refused before anything is saved");
			ShouldCarryNoServiceData(callResult);
			string log = LogText(callResult);
			foreach (string fragment in expectedFragments) {
				log.Should().Contain(fragment, because: "the refusal names what was sent and what the caller can use instead");
			}
			CallToolResult described = await ProcessDesignerE2EArrange.DescribeAsync(context, processName);
			JsonSerializer.Serialize(described).Should().Contain("was not found",
				because: "a refused build creates nothing, so describing it finds no process");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-04: a method sent in lower case is matched case-insensitively and stored in its canonical spelling, so the generated parameters are present: run time and the platform look the method up ordinally, and a wrong-case name would save green with no parameters and fail at run time.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process stores the canonical method name")]
	public async Task CreateBusinessProcess_Should_StoreTheCanonicalMethodName() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync(EchoService);
		List<string> created = [];
		try {
			string processName = NewProcessName("Case");
			string descriptor = LinearDescriptor(processName, TextParametersJson,
				[("WS1", WebServiceElementJson("WS1", EchoService.ToLowerInvariant(), EchoMethod.ToLowerInvariant()))], "[]");

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName, descriptor);

			// Assert
			ShouldHaveBuilt(callResult, "a service and a method named in another letter case resolve");
			JsonObject element = Element(await DescribeGraphAsync(context, processName), "WS1");
			JsonObject block = WebServiceBlock(element);
			Text(block, "method").Should().Be(EchoMethod, because: "the method is stored in its canonical spelling");
			Text(block, "service").Should().Be(EchoService, because: "the service is stored canonically too");
			block["methodResolved"]!.GetValue<bool>().Should().BeTrue(because: "the stored name matches ordinally");
			Parameters(block).Should().Contain(parameter => Text(parameter, "name") == "UsrWscText_In",
				because: "the canonical name keeps the generated parameters, which a wrong-case name loses on load");
			StoredMember(StoredNode(await ReadLastElementNodeAsync(context, processName, UId(element))), "IX2").GetValue<string>()
				.Should().Be(EchoMethod, because: "IX2 holds the canonical spelling");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-07: read-only on the designer-built fixture UsrWscEm2CaptureA: describe reports its Call web service element as a webService block with both resolved flags true and the stored parameter tree, and no reply carries the X-Wsc-Probe header default. Nothing is written. Ignored, with the build instructions, when the stand lacks the fixture.")]
	[AllureTag(DescribeProcessTool.ToolName)]
	[AllureName("describe-business-process reads the designer-built web service fixture")]
	public async Task DescribeBusinessProcess_Should_ReadTheDesignerBuiltFixture() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync(EchoService);
		await IgnoreUnlessDesignerFixtureExistsAsync(context);

		// Act
		CallToolResult described = await ProcessDesignerE2EArrange.DescribeAsync(context, DesignerFixtureName);

		// Assert
		ShouldCarryNoServiceData(described);
		JsonObject graph = DescribedProcessGraph.Read(described);
		JsonObject element = (graph["elements"] as JsonArray ?? []).OfType<JsonObject>()
			.Should().ContainSingle(candidate => Text(candidate, "buildType") == WebServiceBuildType,
				because: "the fixture carries one Call web service element").Which;
		JsonObject block = WebServiceBlock(element);
		block["serviceResolved"]!.GetValue<bool>().Should().BeTrue(because: "the fixture calls the registered echo service");
		block["methodResolved"]!.GetValue<bool>().Should().BeTrue(because: "the fixture's method is a method of that service");
		Text(block, "method").Should().Be(EchoMethod, because: "the fixture calls UsrWscEcho");
		Parameters(block).Should().Contain(parameter => Text(parameter, "name") == "UsrWscProbe_In",
			because: "the header parameter is reported as an element parameter, without the service's default");
	}

	[Test]
	[Description("TC-I-12: a create that leaves a nested list of the request without a source builds and warns about it (the N5 notice): the platform before the nested-list fix sends such a list wrong at run time, so the build must say so. Text only, no run.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process warns about a nested list request binding")]
	public async Task CreateBusinessProcess_Should_WarnAboutANestedListRequestBinding() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync(EchoService);
		List<string> created = [];
		try {
			string processName = NewProcessName("Nested");
			string descriptor = LinearDescriptor(processName, TextParametersJson,
				[("WS1", WebServiceElementJson("WS1", EchoService, EchoMethod))],
				"""
				[ { "elementName": "WS1", "elementParameter": "UsrWscReq_In", "value": "required" },
				  { "elementName": "WS1", "elementParameter": "UsrWscOrders_In.UsrWscOrdNo_In", "value": "N-1" } ]
				""");

			// Act
			CallToolResult callResult = await CreateAsync(context, created, processName, descriptor);

			// Assert
			ShouldHaveBuilt(callResult, "a sourceless nested list is a warning, not a refusal");
			WarningText(callResult).Should().Contain("UsrWscLines_In",
				because: "the notice names the nested list that has no source");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	#endregion

	#region Methods: Tests - modify

	[Test]
	[Description("TC-I-05: a service and method change on a built element is refused while a process parameter reads a parameter of the element other than Success, Response or StatusCode, and nothing is written; once the reader is removed the change builds, the element's parameters are replaced by the new method's, and the notice lists the dropped input bindings with their values in the write shape.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process replaces the parameters on a method change and guards their readers")]
	public async Task ModifyBusinessProcess_Should_ReplaceOnMethodChange_AndGuardDependents() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync(EchoService, DriftService);
		List<string> created = [];
		try {
			string processName = NewProcessName("Retarget");
			await ArrangeProcessAsync(context, created, processName, LinearDescriptor(processName, TextParametersJson,
				[("WS1", WebServiceElementJson("WS1", EchoService, EchoMethod))],
				"""
				[ { "elementName": "WS1", "elementParameter": "UsrWscReq_In", "value": "required" },
				  { "targetProcessParameter": "Title", "sourceElement": "WS1", "sourceElementParameter": "UsrWscEchoText_Out" } ]
				"""), "the process with a web service element and a reader of its output");
			string before = await ReadLastElementNodeAsync(context, processName,
				UId(Element(await DescribeGraphAsync(context, processName), "WS1")));
			string retarget = """
				[ { "op": "setElement", "elementName": "WS1", "elementUpdate": { "webService": { "service": "UsrWscDriftService", "method": "UsrWscDriftGone" } } } ]
				""";

			// Act
			CallToolResult refused = await ModifyAsync(context, processName, retarget);
			string afterRefusal = await ReadLastElementNodeAsync(context, processName,
				UId(Element(await DescribeGraphAsync(context, processName), "WS1")));
			await ArrangeModifyAsync(context, processName,
				"""[ { "op": "removeParameter", "parameterName": "Title" } ]""",
				"the reader is removed");
			CallToolResult changed = await ModifyAsync(context, processName, retarget);

			// Assert
			ShouldBeRefused(refused, "a process parameter reads UsrWscEchoText_Out, which the new method does not produce");
			LogText(refused).Should().Contain("UsrWscEchoText_Out", because: "the refusal names the parameter that is read");
			afterRefusal.Should().Be(before, because: "a refused change writes nothing");
			ShouldHaveLanded(changed, "with no reader left the change replaces the parameters");
			WarningText(changed).Should().MatchRegex(@"Dropped input bindings:[^}]*UsrWscReq_In[^}]*required",
				because: "the notice lists the dropped input binding together with its value, in one write-shape entry");
			JsonObject block = WebServiceBlock(Element(await DescribeGraphAsync(context, processName), "WS1"));
			Text(block, "method").Should().Be(DriftMethod, because: "the element now calls the new method");
			Parameters(block).Should().NotContain(parameter => Text(parameter, "name") == "UsrWscReq_In",
				because: "the old method's parameters are replaced");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-06: describe, then replay the described webService block through setElement unchanged - the describe-only serviceUId, serviceResolved, methodResolved, issues and parameters included - changes no stored byte of the element, and the reply carries one warning about those members, the server's.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process changes nothing when the described block is sent back")]
	public async Task ModifyBusinessProcess_Should_ChangeNoStoredByte_WhenTheDescribedBlockIsSentBack() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync(EchoService);
		List<string> created = [];
		try {
			string processName = NewProcessName("Replay");
			await ArrangeProcessAsync(context, created, processName, LinearDescriptor(processName, TextParametersJson,
				[("WS1", WebServiceElementJson("WS1", EchoService, EchoMethod, timeoutSeconds: 7, ignoreErrors: true))],
				"""[ { "elementName": "WS1", "elementParameter": "UsrWscReq_In", "value": "required" } ]"""),
				"the process to replay");
			JsonObject element = Element(await DescribeGraphAsync(context, processName), "WS1");
			string before = await ReadLastElementNodeAsync(context, processName, UId(element));
			JsonObject replay = (JsonObject)WebServiceBlock(element).DeepClone();
			string operations = new JsonArray(new JsonObject {
				["op"] = "setElement",
				["elementName"] = "WS1",
				["elementUpdate"] = new JsonObject { ["webService"] = replay }
			}).ToJsonString();

			// Act
			CallToolResult callResult = await ModifyAsync(context, processName, operations);

			// Assert
			ShouldHaveLanded(callResult, "the described block is a valid setElement as is; its read-back keys draw warnings only");
			(await ReadLastElementNodeAsync(context, processName, UId(element))).Should().Be(before,
				because: "the same service, method, timeout and flag are a no-op, so no stored byte changes");
			WarningText(callResult).Should().Contain("Read-only fields copied from describe were ignored",
				because: "the server names the describe-only members it ignored");
			WarningText(callResult).Should().NotContain("Ignored describe-business-process read-back fields",
				because: "one round trip carries one warning about those members, the server's, not a second one from clio");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-10: modify-business-process-as-new-version clones a process with a Call web service element, and the clone's element keeps its block, its parameters and its result-mapping rows, every row owned by the NEW version. The version is deleted in finally.")]
	[AllureTag(NewVersionToolName)]
	[AllureName("modify-business-process-as-new-version keeps the web service element")]
	public async Task ModifyProcessAsNewVersion_Should_KeepTheWebServiceElement() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync(EchoService);
		List<string> created = [];
		try {
			string processName = NewProcessName("Version");
			await ArrangeProcessAsync(context, created, processName, LinearDescriptor(processName, TextParametersJson,
				[("WS1", WebServiceElementJson("WS1", EchoService, EchoMethod))],
				"""[ { "elementName": "WS1", "elementParameter": "UsrWscReq_In", "value": "required" } ]"""),
				"the source process");
			IReadOnlyList<string> sourceNames = Parameters(WebServiceBlock(Element(
				await DescribeGraphAsync(context, processName), "WS1"))).Select(parameter => Text(parameter, "name")!).ToList();

			// Act
			CallToolResult saved = await ProcessDesignerE2EArrange.CallToolAsync(context, NewVersionToolName,
				new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["process-name"] = processName,
					["package-name"] = "Custom",
					["operations"] = """[ { "op": "setElement", "elementName": "WS1", "elementUpdate": { "webService": { "timeoutSeconds": 9 } } } ]"""
				});
			IgnoreWhenProcessBuilderIsBehind(ToolText(saved), MinimumPackageVersion);
			if (ProcessDesignerE2EArrange.TryReadCreatedVersionName(saved, out string createdVersion)) {
				created.Add(createdVersion);
			}
			ShouldHaveLanded(saved, "a version of a process with a web service element is an ordinary clone");
			ShouldCarryNoServiceData(saved);
			string versionName = ProcessDesignerE2EArrange.CreatedVersionName(saved);
			JsonObject version = await DescribeGraphAsync(context, versionName);
			JsonObject element = Element(version, "WS1");
			JsonObject metadata = await ReadLastMetaDataAsync(context, versionName);

			// Assert
			Parameters(WebServiceBlock(element)).Select(parameter => Text(parameter, "name")).Should().Equal(sourceNames,
				because: "the clone carries every parameter of the source element");
			WebServiceBlock(element)["timeoutSeconds"]!.GetValue<decimal>().Should().Be(9m,
				because: "the operation applied to the clone");
			MappingRowsOf(metadata, UId(element)).Should().HaveCount(ResultParameters.Length,
				because: "the clone keeps one result-mapping row per result parameter");
			MappingRowsOf(metadata, UId(element)).Should().OnlyContain(row => Text(row, "A3") == Text(version, "schemaUId"),
				because: "every row of the clone is owned by the new version, not by the source");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	#endregion

	#region Methods: Tests - validation and run time

	[Test]
	[Description("TC-I-11: validate-process-graph over a described Call web service element. The element's build type is read from describe and validated in a Start -> element -> End graph: the graph validates and carries no UNKNOWN finding.")]
	[AllureTag(ValidateProcessGraphTool.ToolName)]
	[AllureName("validate-process-graph accepts a described web service element")]
	public async Task ValidateProcessGraph_Should_AcceptADescribedWebServiceElement() {
		// Arrange
		await using ProcessDesignerArrangeContext context = await StartAsync(EchoService);
		List<string> created = [];
		try {
			string processName = NewProcessName("Validate");
			await ArrangeProcessAsync(context, created, processName, LinearDescriptor(processName, TextParametersJson,
				[("WS1", WebServiceElementJson("WS1", EchoService, EchoMethod))], "[]"),
				"the process with a web service element");
			string buildType = Text(Element(await DescribeGraphAsync(context, processName), "WS1"), "buildType") ?? string.Empty;

			// Act
			CallToolResult callResult = await ProcessDesignerE2EArrange.CallToolAsync(context, ValidateProcessGraphTool.ToolName,
				new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["nodes"] = new[] { GraphNode("s", "startEvent"), GraphNode("w", buildType), GraphNode("e", "endEvent") },
					["edges"] = new[] { GraphEdge("s", "w"), GraphEdge("w", "e") }
				});

			// Assert
			buildType.Should().Be(WebServiceBuildType,
				because: "the node type validated is the one describe reported, so the graph is the described one");
			ValidateProcessGraphResponse response =
				EntitySchemaStructuredResultParser.Extract<ValidateProcessGraphResponse>(callResult);
			response.Success.Should().BeTrue(because: "the graph is well formed; the error was: {0}", response.Error);
			(response.Findings ?? []).Where(finding => finding.RuleId == "UNKNOWN").Should().BeEmpty(
				because: "webservice is a known build type");
			response.HasErrors.Should().NotBe(true, because: "Start -> Call web service -> End violates no connection rule");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[Test]
	[Description("TC-I-08: a built process calls UsrWscEcho at run time and its result parameters land: Success, StatusCode and Response are mapped into process parameters and come back as the run's result values. Reduced to the result parameters, because the service's base URI is the stand root and no endpoint echoes the request. Ignored without McpE2E:AllowDestructiveMcpTests.")]
	[AllureTag(RunProcessTool.ToolName)]
	[AllureName("run-process calls the web service and reads its result parameters")]
	public async Task RunProcess_Should_CallTheService_AndReadTheResponse() {
		// Arrange
		IgnoreUnlessDestructiveTestsAreAllowed("the run-time web service case");
		await using ProcessDesignerArrangeContext context = await StartAsync(EchoService);
		List<string> created = [];
		try {
			string processName = NewProcessName("Run");
			await ArrangeProcessAsync(context, created, processName, LinearDescriptor(processName, ResultParametersJson,
				[("WS1", WebServiceElementJson("WS1", EchoService, EchoMethod, ignoreErrors: true))], ResultMappingsJson("WS1")),
				"the process that calls the service");

			// Act
			RunProcessEnvelope run = await RunAsync(context, processName, "CallSucceeded", "CallStatus", "CallResponse");

			// Assert
			run.Error.Should().BeNull(because: "the call reaches the stand and the element completes: {0}", run.Error);
			run.Status.Should().Be("completed", because: "a Call web service element runs straight through");
			(run.ResultParameterValues ?? []).Should().ContainKeys(["CallSucceeded", "CallStatus", "CallResponse"],
				because: "Success, StatusCode and Response land in the process parameters mapped from them");
			int.TryParse(Convert.ToString(run.ResultParameterValues!["CallStatus"], CultureInfo.InvariantCulture),
					NumberStyles.Integer, CultureInfo.InvariantCulture, out int statusCode)
				.Should().BeTrue(because: "StatusCode lands as an integer");
			statusCode.Should().BePositive(
				because: "an HTTP status code is written only when the call reached a web server; with ignoreErrors a call with no answer still completes, so the run's status alone proves nothing");
			JsonSerializer.Serialize(run).Should().NotContain(HeaderDefault,
				because: "the header default stored in the service never reaches builder output");
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	[TestCase(true, "completed")]
	[TestCase(false, "error")]
	[Description("TC-I-09: a call that gets no answer - UsrWscDeadService, whose base URI has no listener - reaches the end of the process when ignoreErrors is true and ends the instance in Error when it is false, as the designer-built twins do. A 404 never fails the element; it shows only as the Success = false branch. Ignored without McpE2E:AllowDestructiveMcpTests.")]
	[AllureTag(RunProcessTool.ToolName)]
	[AllureName("run-process continues past a failed call only when errors are ignored")]
	public async Task RunProcess_Should_Continue_WhenTheCallFailsAndErrorsAreIgnored(bool ignoreErrors, string expectedStatus) {
		// Arrange
		IgnoreUnlessDestructiveTestsAreAllowed("the ignore-errors run case");
		await using ProcessDesignerArrangeContext context = await StartAsync(DeadService);
		List<string> created = [];
		try {
			string processName = NewProcessName(ignoreErrors ? "Ignore" : "Strict");
			await ArrangeProcessAsync(context, created, processName, LinearDescriptor(processName, TextParametersJson,
				[("WS1", WebServiceElementJson("WS1", DeadService, DeadMethod, ignoreErrors: ignoreErrors))], "[]"),
				"the process that calls the dead service");

			// Act
			RunProcessEnvelope run = await RunAsync(context, processName);

			// Assert
			run.Status.Should().Be(expectedStatus,
				because: (ignoreErrors
					? "an ignored transport failure lets the instance reach the end event"
					: "an unignored transport failure ends the instance in Error") + " (run error: {0})", run.Error);
		} finally {
			await DeleteProcessesAsync(context, created);
		}
	}

	#endregion

	#region Methods: Descriptors

	// The process parameter the scalar bindings read.
	private const string TextParametersJson = """
		[ { "name": "Title", "type": "Text", "direction": "In" } ]
		""";

	// A declared two-level Collection the UsrWscOrders2 response is read into.
	private const string OrdersParametersJson = """
		[ { "name": "Orders", "type": "Collection", "direction": "Out",
		    "itemProperties": [ { "name": "No", "type": "Text" },
		      { "name": "Lines", "type": "Collection", "itemProperties": [ { "name": "Sku", "type": "Text" } ] } ] } ]
		""";

	// The process parameters the run-time case reads the result parameters into.
	private const string ResultParametersJson = """
		[ { "name": "CallSucceeded", "type": "Boolean", "direction": "Out" },
		  { "name": "CallStatus", "type": "Integer", "direction": "Out" },
		  { "name": "CallResponse", "type": "Long text", "direction": "Out" } ]
		""";

	private static string ResultMappingsJson(string elementName) =>
		$$"""
		[ { "targetProcessParameter": "CallSucceeded", "sourceElement": "{{elementName}}", "sourceElementParameter": "Success" },
		  { "targetProcessParameter": "CallStatus", "sourceElement": "{{elementName}}", "sourceElementParameter": "StatusCode" },
		  { "targetProcessParameter": "CallResponse", "sourceElement": "{{elementName}}", "sourceElementParameter": "Response" },
		  { "elementName": "{{elementName}}", "elementParameter": "UsrWscReq_In", "value": "required" } ]
		""";

	/// <summary>A Call web service element with the given block; the optional members are sent only when given.</summary>
	private static string WebServiceElementJson(string name, string service, string method, int? timeoutSeconds = null,
			bool? ignoreErrors = null) {
		JsonObject block = new() { ["service"] = service, ["method"] = method };
		if (timeoutSeconds.HasValue) {
			block["timeoutSeconds"] = timeoutSeconds.Value;
		}
		if (ignoreErrors.HasValue) {
			block["ignoreErrors"] = ignoreErrors.Value;
		}
		return new JsonObject {
			["name"] = name,
			["type"] = "webService",
			["caption"] = name,
			["webService"] = block
		}.ToJsonString();
	}

	/// <summary>Start -> the elements in the order given -> End, with the given descriptor-level mappings.</summary>
	private static string LinearDescriptor(string processName, string parametersJson,
			(string Name, string Json)[] elements, string mappingsJson) {
		List<string> names = ["StartEvent1", .. elements.Select(element => element.Name), "EndEvent1"];
		string elementsJson = string.Join(",\n", new[] {
				"""{ "name": "StartEvent1", "type": "startEvent" }"""
			}
			.Concat(elements.Select(element => element.Json))
			.Append("""{ "name": "EndEvent1", "type": "endEvent" }"""));
		string flowsJson = string.Join(",\n", names.Zip(names.Skip(1),
			(source, target) => $$"""{ "source": "{{source}}", "target": "{{target}}" }"""));
		return $$"""
			{
			  "name": "{{processName}}",
			  "caption": "Clio BP Call web service E2E",
			  "packageName": "Custom",
			  "parameters": {{parametersJson}},
			  "elements": [ {{elementsJson}} ],
			  "flows": [ {{flowsJson}} ],
			  "mappings": {{mappingsJson}}
			}
			""";
	}

	private static Dictionary<string, object?> GraphNode(string name, string type) =>
		new() { ["name"] = name, ["type"] = type };

	private static Dictionary<string, object?> GraphEdge(string source, string target) =>
		new() { ["source"] = source, ["target"] = target, ["flow-kind"] = "sequence" };

	#endregion

	#region Methods: Arrange and cleanup

	/// <summary>
	/// Opens a session, or ignores the test when the stand is missing, unreachable, behind the cut, or lacks the
	/// service the case calls.
	/// </summary>
	private static async Task<ProcessDesignerArrangeContext> StartAsync(params string[] requiredServices) {
		ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync(Subject, MinimumPackageVersion, TimeSpan.FromMinutes(8));
		foreach (string service in requiredServices) {
			await IgnoreUnlessServiceExistsAsync(context, service);
		}
		return context;
	}

	/// <summary>A process code unique to this run, in the <c>UsrClioBpWs&lt;Case&gt;E2e</c> family.</summary>
	private static string NewProcessName(string caseName) => $"UsrClioBpWs{caseName}E2e{Guid.NewGuid():N}";

	/// <summary>
	/// Builds a process, recording its name for cleanup BEFORE the call: a build the server saved but whose answer
	/// never arrived must still be removed. Ignores the test when clio refused the call because the stand's
	/// CrtProcessBuilder is behind.
	/// </summary>
	private static async Task<CallToolResult> CreateAsync(ProcessDesignerArrangeContext context,
			List<string> created, string processName, string descriptor) {
		created.Add(processName);
		CallToolResult result = await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = descriptor
			});
		IgnoreWhenProcessBuilderIsBehind(ToolText(result), MinimumPackageVersion);
		ShouldCarryNoServiceData(result);
		return result;
	}

	/// <summary>Builds a process for an ARRANGE step and fails the test on the spot unless it was created.</summary>
	private static async Task ArrangeProcessAsync(ProcessDesignerArrangeContext context, List<string> created,
			string processName, string descriptor, string what) {
		CallToolResult result = await CreateAsync(context, created, processName, descriptor);
		ShouldHaveBuilt(result, $"{what} is an arrange step, so every assertion below is about the wrong failure "
			+ "if it was not built");
	}

	private static async Task<CallToolResult> ModifyAsync(ProcessDesignerArrangeContext context, string processName,
			string operations) {
		CallToolResult result = await ProcessDesignerE2EArrange.CallToolAsync(context, ModifyToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = operations
			});
		IgnoreWhenProcessBuilderIsBehind(ToolText(result), MinimumPackageVersion);
		ShouldCarryNoServiceData(result);
		return result;
	}

	/// <summary>A modify for an ARRANGE step, failing the test on the spot unless it landed.</summary>
	private static async Task ArrangeModifyAsync(ProcessDesignerArrangeContext context, string processName,
			string operations, string what) {
		ShouldHaveLanded(await ModifyAsync(context, processName, operations),
			$"{what} is an arrange step, so every assertion below is about the wrong failure if it did not land");
	}

	/// <summary>Reads a process back in full and returns its graph.</summary>
	private static async Task<JsonObject> DescribeGraphAsync(ProcessDesignerArrangeContext context,
			string processName) {
		CallToolResult described = await ProcessDesignerE2EArrange.DescribeAsync(context, processName);
		ShouldCarryNoServiceData(described);
		return DescribedProcessGraph.Read(described);
	}

	/// <summary>Removes every process a test built, newest first; a failed delete is reported, not thrown.</summary>
	private static async Task DeleteProcessesAsync(ProcessDesignerArrangeContext context, List<string> created) {
		for (int index = created.Count - 1; index >= 0; index--) {
			await DeleteProcessAsync(context.EnvironmentName, created[index]);
		}
	}

	private static void IgnoreUnlessDestructiveTestsAreAllowed(string what) {
		if (!TestConfiguration.Load().AllowDestructiveMcpTests) {
			Assert.Ignore($"Set McpE2E:AllowDestructiveMcpTests=true to run {what}.");
		}
	}

	/// <summary>
	/// Ignores the test, with the build instructions, when the stand does not carry the web service the case calls.
	/// Read from <c>VwWebServiceV2</c> by name, so a broken build still fails instead of reading as an absent service.
	/// </summary>
	private static async Task IgnoreUnlessServiceExistsAsync(ProcessDesignerArrangeContext context, string service) {
		JsonElement rows = await SelectInlineAsync(context, SelectQueryHelper.BuildSelectQuery("VwWebServiceV2",
			[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
			[new SelectQueryHelper.SelectQueryFilterDefinition("Name", service, SelectQueryHelper.TextDataValueType)],
			rowCount: 1), $"the web service {service}");
		if (rows.GetArrayLength() == 0) {
			Assert.Ignore($"Web service {service} not found on '{context.EnvironmentName}'. Register it in Studio > Web "
				+ "services as the analysis measurements file describes (section 3.2: REST, base URI the stand root - "
				+ "UsrWscDeadService http://127.0.0.1:1 - methods UsrWscEcho, UsrWscDriftGone and UsrWscOrders2, "
				+ "UsrWscDead, codes with the Usr prefix).");
		}
	}

	/// <summary>Ignores the test, with the build instructions, when the stand does not carry the designer fixture.</summary>
	private static async Task IgnoreUnlessDesignerFixtureExistsAsync(ProcessDesignerArrangeContext context) {
		JsonElement rows = await SelectInlineAsync(context, SelectQueryHelper.BuildSelectQuery("SysSchema",
			[new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id")],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition("Name", DesignerFixtureName,
					SelectQueryHelper.TextDataValueType),
				new SelectQueryHelper.SelectQueryFilterDefinition("ManagerName", "ProcessSchemaManager",
					SelectQueryHelper.TextDataValueType)
			], rowCount: 1), "the designer fixture's SysSchema row");
		if (rows.GetArrayLength() == 0) {
			Assert.Ignore($"Designer fixture {DesignerFixtureName} not found on '{context.EnvironmentName}'. Build it in "
				+ $"the classic designer: one Call web service element on {EchoService}.{EchoMethod} with a 7 s timeout "
				+ "and ignore errors on, the request values bound as measurement EM2 records, and save.");
		}
	}

	/// <summary>
	/// Runs the process and waits for it. <paramref name="resultParameters"/> must be parameters the process declares:
	/// run-process refuses an unknown one before starting anything, which would read as a run with no status.
	/// </summary>
	private static async Task<RunProcessEnvelope> RunAsync(ProcessDesignerArrangeContext context, string processName,
			params string[] resultParameters) {
		Dictionary<string, object?> args = new() {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["wait-timeout"] = 60
		};
		if (resultParameters.Length > 0) {
			args["result-parameters"] = resultParameters;
		}
		CallToolResult result = await context.Session.CallToolAsync(ClioRunTool.ToolName,
			new Dictionary<string, object?> { ["command"] = RunProcessTool.ToolName, ["args"] = args },
			context.CancellationTokenSource.Token);
		ShouldCarryNoServiceData(result);
		return EntitySchemaStructuredResultParser.Extract<RunProcessEnvelope>(result);
	}

	/// <summary>A member of a stored element node; a missing one fails the test rather than skipping the check.</summary>
	private static JsonNode StoredMember(JsonObject stored, string key) {
		stored[key].Should().NotBeNull(because: $"the stored element node carries {key}");
		return stored[key]!;
	}

	/// <summary>
	/// Parses a stored element node. The platform stores process metadata indented, so a member is compared by value,
	/// never by searching the text for its compact form.
	/// </summary>
	private static JsonObject StoredNode(string node) =>
		JsonNode.Parse(node) as JsonObject
		?? throw new InvalidOperationException("A stored element node is a JSON object.");

	/// <summary>The metadata JSON of the LAST stored <c>SysSchema</c> row of <paramref name="schemaName"/>.</summary>
	private static async Task<JsonObject> ReadLastMetaDataAsync(ProcessDesignerArrangeContext context,
			string schemaName) {
		JsonElement rows = await SelectToFileAsync(context, SelectQueryHelper.BuildSelectQuery("SysSchema",
			[
				new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id", OrderDirection: 1, OrderPosition: 0),
				new SelectQueryHelper.SelectQueryColumnDefinition("MetaData", "MetaData")
			],
			[
				new SelectQueryHelper.SelectQueryFilterDefinition("Name", schemaName, SelectQueryHelper.TextDataValueType),
				new SelectQueryHelper.SelectQueryFilterDefinition("ManagerName", "ProcessSchemaManager",
					SelectQueryHelper.TextDataValueType)
			]), $"the SysSchema rows of '{schemaName}'");
		rows.GetArrayLength().Should().BePositive(because: $"'{schemaName}' must be stored for its metadata to be read");
		return JsonNode.Parse(DecodeMetaData(rows[rows.GetArrayLength() - 1].GetProperty("MetaData")))!.AsObject();
	}

	#endregion

	#region Methods: Graph reading

	/// <summary>
	/// Fails when a reply carries the constant default the test services store on their <c>X-Wsc-Probe</c> header:
	/// service-side data never reaches builder output.
	/// </summary>
	private static void ShouldCarryNoServiceData(CallToolResult result) =>
		JsonSerializer.Serialize(result).Should().NotContain(HeaderDefault,
			because: "no data stored in the web service itself is returned in builder output");

	/// <summary>The described <c>webService</c> block of an element; absent fails the test.</summary>
	private static JsonObject WebServiceBlock(JsonObject element) => Group(element, "webService");

	/// <summary>The described stored parameter tree of a <c>webService</c> block.</summary>
	private static IReadOnlyList<JsonObject> Parameters(JsonObject block) {
		block["parameters"].Should().BeOfType<JsonArray>(
			because: "describe reports every stored parameter of the element; what was read: {0}", block.ToJsonString());
		return block["parameters"]!.AsArray().OfType<JsonObject>().ToList();
	}

	private static JsonObject Parameter(IReadOnlyList<JsonObject> parameters, string name) =>
		parameters.Should().ContainSingle(parameter => Text(parameter, "name") == name,
			because: $"the element stores one parameter named {name}").Which;

	/// <summary>Every result-mapping row of the process metadata whose target is a parameter of the element.</summary>
	private static IReadOnlyList<JsonObject> MappingRowsOf(JsonNode metadata, string elementUId) =>
		Objects(metadata)
			.Where(node => Text(node, "BL1") == MappingRowType
				&& (Text(node, "GT2") ?? string.Empty).Contains(elementUId, StringComparison.OrdinalIgnoreCase))
			.ToList();

	private static IEnumerable<JsonObject> Objects(JsonNode? node) {
		switch (node) {
			case JsonObject value:
				yield return value;
				foreach (KeyValuePair<string, JsonNode?> property in value) {
					foreach (JsonObject nested in Objects(property.Value)) {
						yield return nested;
					}
				}
				break;
			case JsonArray list:
				foreach (JsonNode? item in list) {
					foreach (JsonObject nested in Objects(item)) {
						yield return nested;
					}
				}
				break;
		}
	}

	private static JsonObject ProcessParameter(JsonObject graph, string name) =>
		(graph["parameters"]?.AsArray() ?? []).OfType<JsonObject>().Single(parameter => Text(parameter, "name") == name);

	private static JsonObject ItemProperty(JsonObject parameter, string name) =>
		(parameter["itemProperties"]?.AsArray() ?? []).OfType<JsonObject>().Single(item => Text(item, "name") == name);

	private static JsonObject Element(JsonObject graph, string name) =>
		(graph["elements"] as JsonArray ?? []).OfType<JsonObject>()
			.Should().ContainSingle(element => Text(element, "name") == name,
				because: $"the process carries one element named {name}").Which;

	private static JsonObject Group(JsonObject owner, string key) {
		owner[key].Should().BeOfType<JsonObject>(
			because: $"'{key}' must be reported as an object; what was read: {{0}}", owner.ToJsonString());
		return owner[key]!.AsObject();
	}

	private static string UId(JsonObject node) {
		string? uid = Text(node, "uid");
		uid.Should().NotBeNullOrEmpty(because: "describe reports every element's uid");
		return uid!;
	}

	#endregion

}
