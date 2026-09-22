using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Command.ProcessModel;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for the MULTI-INSTANCE Sub-process element (ENG-99856) over the real MCP path. NOT in
/// CI - run manually, gated on the <c>process-designer</c> feature and a reachable environment carrying a
/// CrtProcessBuilder of at least 1.6.6.4.
/// <para>What only a live server can prove here is the PLATFORM's rebuild. Assigning <c>SchemaUId</c> on a
/// converted element makes the platform clear the element's parameters and re-derive them, and every unit
/// test drives that against a substituted schema manager. This is the only place the real
/// <c>ProcessSchemaActivity</c> rebuild runs against a real saved schema - so it is the only place that can
/// show the five service parameters actually replacing the callee's, the callee's contract actually landing
/// one level down as the input collection's item properties, and a dotted per-item mapping actually
/// surviving the save-and-read round trip.</para>
/// <para>The two cases are deliberately asymmetric in what they are FOR. The happy path proves the shape;
/// the refusal proves the one write that would be silently erased is stopped at the door, because a value
/// written into the OUTPUT collection is cleared by the platform on the next synchronization with no error
/// anywhere - the failure mode the whole refusal exists for.</para>
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class SubProcessMultiInstanceToolE2ETests {

	private const string ToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;
	private const string DescribeToolName = DescribeProcessTool.ToolName;

	#region Methods: Tests

	[Test]
	[Description("Over the real MCP path, create-business-process converts a sub-process element to multi-instance, binds a Read data collection onto its input collection and maps ONE per-item value through a dotted path; describe-business-process then reads the whole shape back. This is the only place the platform's own parameter rebuild runs against a real saved schema: the five service parameters replace the callee's, the callee's contract lands one level down as the input collection's item properties, and the dotted mapping survives the round trip.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process builds a multi-instance sub-process and describe reads it back")]
	public async Task CreateBusinessProcess_Should_BuildAMultiInstanceSubProcess_AndReadTheShapeBack() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string calleeName = $"UsrClioBpMiCallee{Guid.NewGuid():N}";
		string callerName = $"UsrClioBpMiCaller{Guid.NewGuid():N}";
		await ArrangeProcessAsync(context, BuildCalleeDescriptor(calleeName), "called process");

		// Act
		CallToolResult callResult = await CreateAsync(context, BuildMultiInstanceCallerDescriptor(callerName, calleeName));

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "converting an element to multi-instance while building it is one call and must not fail "
				+ "at the transport");
		JsonSerializer.Serialize(callResult).Should().Contain("created (UId:",
			because: "only a genuinely successful build logs the created-schema line - the command logs "
				+ "\"Building process\" BEFORE it calls the server, so a name match alone also passes a failure");

		DescribedElement element = (await DescribeAsync(context, callerName)).Elements
			.Single(candidate => candidate.Name == "SubProcess1");
		element.SubProcess.Should().NotBeNull(
			because: "a null block on a successful build is the silent-drop signature of a server that predates "
				+ "the element, and dereferencing it below would report a NullReferenceException instead of the "
				+ "assertion this test was written for");
		element.SubProcess!.MultiInstance.Should().Be(true,
			because: "THE acceptance criterion. Asserted against TRUE rather than falsy, so a server that "
				+ "reported no such field at all fails here instead of reading as single-instance");
		element.SubProcess.MultiInstanceOptions.Should().NotBeNull(
			because: "the options block is emitted on every multi-instance element; its absence beside "
				+ "multiInstance:true is a read that lost the conversion's own configuration");
		element.SubProcess.MultiInstanceOptions!.ExecutionMode.Should().Be("Sequential",
			because: "the EFFECTIVE mode is reported as the STRING the write side spells - the stored metadata "
				+ "omits Sequential entirely, and a caller cannot act on 'absent'");
		element.SubProcess.MultiInstanceOptions.InputCollection.Should().Be("InputRecordCollection",
			because: "a mapping is written in terms of this name, so the read has to report it rather than "
				+ "leaving the caller to know it");

		// The parameter SHAPE is what the conversion changes, and it is the half no in-memory test can show.
		IReadOnlyCollection<string> parameterNames = element.Parameters.Select(parameter => parameter.Name).ToList();
		parameterNames.Should().BeEquivalentTo(new[] {
				"InputRecordCollection", "OutputRecordCollection", "CompletedIterationsCount",
				"TerminatedIterationsCount", "TotalIterationsCount" },
			because: "a converted element carries the five service parameters INSTEAD of the callee's - asserted "
				+ "as the whole set rather than as Contain, because a callee parameter left behind at the root "
				+ "is exactly the half-finished conversion this suite has to catch");

		DescribedParameter input = element.Parameters.Single(parameter => parameter.Name == "InputRecordCollection");
		input.ItemProperties.Should().NotBeNull(
			because: "the callee's contract moves one level down on conversion, and the item properties ARE that "
				+ "contract - a null here means the read never descended");
		input.ItemProperties!.Select(item => item.Name).Should().Contain("ItemName",
			because: "the called process's In parameter has to arrive as an item property of the input "
				+ "collection; it is the name a dotted per-item mapping addresses");
		input.ItemProperties.Single(item => item.Name == "ItemName").Value.Should().NotBeNullOrWhiteSpace(
			because: "the dotted per-item mapping has to survive the save-and-read round trip. An empty value "
				+ "here is the ContainerUId failure: the source metapath is written without its [Element:{...}] "
				+ "segment, resolves at design time and binds to NOTHING at run time - silently");
	}

	[Test]
	[Description("Over the real MCP path, a mapping whose target is inside the OUTPUT collection is refused. The platform derives those values and clears them on every synchronization, so the write is erased with no error at any layer - the caller would see a successful build and an element that quietly delivers nothing. The refusal is the only thing that tells them.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process refuses a mapping into the output collection")]
	public async Task CreateBusinessProcess_Should_RefuseAMappingIntoTheOutputCollection() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync();
		string calleeName = $"UsrClioBpMiOutCallee{Guid.NewGuid():N}";
		string callerName = $"UsrClioBpMiOutCaller{Guid.NewGuid():N}";
		await ArrangeProcessAsync(context, BuildCalleeDescriptor(calleeName), "called process");

		// Act
		CallToolResult callResult = await CreateAsync(context,
			BuildOutputCollectionTargetDescriptor(callerName, calleeName));

		// Assert
		JsonSerializer.Serialize(callResult).Should().NotContain("created (UId:",
			because: "the build has to be refused rather than saved - a process saved with that mapping looks "
				+ "healthy and delivers nothing, which is the state the refusal exists to prevent");
		JsonSerializer.Serialize(callResult).Should().Contain("OutputRecordCollection",
			because: "the refusal has to name the collection it is about, or the caller cannot tell it from any "
				+ "other mapping failure");
		JsonSerializer.Serialize(callResult).Should().Contain("is inside the output collection",
			because: "the parameter NAME alone does not identify which refusal spoke - a direction guard, a "
				+ "type mismatch or a 'no such parameter' would all quote it back - and this assertion is the "
				+ "only thing standing between 'the right refusal reached the caller over the real MCP path' "
				+ "and 'the build failed for some reason and the name happened to appear'");
	}

	#endregion

	#region Methods: Descriptors

	// The callee: one In parameter, which becomes the input collection's single item property after the
	// conversion, and one Out parameter, which becomes the output collection's.
	private static string BuildCalleeDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP multi-instance callee E2E",
		  "packageName": "Custom",
		  "parameters": [
		    { "name": "ItemName", "type": "Text", "direction": "In" },
		    { "name": "Echoed", "type": "Text", "direction": "Out" }
		  ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [ { "source": "StartEvent1", "target": "EndEvent1" } ]
		}
		""";

	// A Read data element in COLLECTION mode supplies the collection to iterate. Its ResultCompositeObjectList
	// is the output whose data value type matches InputRecordCollection - ResultEntityCollection does NOT, and
	// binding that one is refused by the type check.
	//
	// Two mappings, and they are different in kind: the first binds the COLLECTION itself through the ordinary
	// addMapping route (no new operation exists for it), the second addresses ONE PER-ITEM value through a
	// dotted name on both sides.
	private static string BuildMultiInstanceCallerDescriptor(string processName, string calleeName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP multi-instance caller E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "ReadData1", "type": "readData", "caption": "Read contacts",
		      "readData": { "source": "Contact", "mode": "collection", "columns": ["Name"], "numberOfRecords": 3 } },
		    { "name": "SubProcess1", "type": "subProcess", "caption": "Call the callee once per contact",
		      "subProcess": {
		        "processName": "{{calleeName}}",
		        "multiInstanceOptions": { "enabled": true, "executionMode": "Sequential", "ignoreErrors": false }
		      } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "ReadData1" },
		    { "source": "ReadData1", "target": "SubProcess1" },
		    { "source": "SubProcess1", "target": "EndEvent1" }
		  ],
		  "mappings": [
		    { "elementName": "SubProcess1", "elementParameter": "InputRecordCollection",
		      "sourceElement": "ReadData1", "sourceElementParameter": "ResultCompositeObjectList" },
		    { "elementName": "SubProcess1", "elementParameter": "InputRecordCollection.ItemName",
		      "sourceElement": "ReadData1", "sourceElementParameter": "ResultCompositeObjectList.Name" }
		  ]
		}
		""";

	// The same graph with the per-item mapping aimed at the OUTPUT collection instead. Everything else is
	// identical, so a failure here is about the target and nothing else.
	private static string BuildOutputCollectionTargetDescriptor(string processName, string calleeName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP multi-instance output-target E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "ReadData1", "type": "readData", "caption": "Read contacts",
		      "readData": { "source": "Contact", "mode": "collection", "columns": ["Name"], "numberOfRecords": 3 } },
		    { "name": "SubProcess1", "type": "subProcess", "caption": "Call the callee once per contact",
		      "subProcess": {
		        "processName": "{{calleeName}}",
		        "multiInstanceOptions": { "enabled": true }
		      } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "ReadData1" },
		    { "source": "ReadData1", "target": "SubProcess1" },
		    { "source": "SubProcess1", "target": "EndEvent1" }
		  ],
		  "mappings": [
		    { "elementName": "SubProcess1", "elementParameter": "InputRecordCollection",
		      "sourceElement": "ReadData1", "sourceElementParameter": "ResultCompositeObjectList" },
		    { "elementName": "SubProcess1", "elementParameter": "OutputRecordCollection.Echoed",
		      "sourceElement": "ReadData1", "sourceElementParameter": "ResultCompositeObjectList.Name" }
		  ]
		}
		""";

	#endregion

	#region Methods: Arrange

	/// <summary>
	/// Builds a process for an ARRANGE step and fails the test on the spot if it did not build.
	/// <para>It asserts the SUCCESS LINE, not <c>IsError</c>: a refused or failed build comes back as an
	/// ordinary result carrying a non-zero exit code, and nothing on the MCP path turns that into
	/// <c>IsError</c>. An arrange guard that checked only the flag would pass the exact failure it exists to
	/// catch, and the callee's absence would then read as a defect in the conversion.</para>
	/// </summary>
	private static async Task<CallToolResult> ArrangeProcessAsync(ArrangeContext context, string descriptor,
			string what) {
		CallToolResult result = await CreateAsync(context, descriptor);
		result.IsError.Should().NotBeTrue(
			because: $"the {what} is an arrange step - if it did not build, every assertion below is about the "
				+ "wrong failure");
		JsonSerializer.Serialize(result).Should().Contain("created (UId:",
			because: $"only a genuinely successful build logs the created-schema line, and the {what} has to "
				+ "exist before the case below means anything");
		return result;
	}

	private static async Task<CallToolResult> CreateAsync(ArrangeContext context, string descriptor) =>
		await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = descriptor
		});

	private static async Task<DescribeProcessResult> DescribeAsync(ArrangeContext context, string processName) =>
		ParseDescribeGraph(await CallToolAsync(context, DescribeToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName
		}));

	private static DescribeProcessResult ParseDescribeGraph(CallToolResult describeResult) {
		CommandExecutionEnvelope envelope = McpCommandExecutionParser.Extract(describeResult);
		string graphJson = envelope.Output!
			.Select(message => message.Value)
			.First(value => !string.IsNullOrWhiteSpace(value)
				&& value!.TrimStart().StartsWith("{", StringComparison.Ordinal))!;
		return JsonSerializer.Deserialize<DescribeProcessResult>(graphJson,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
	}

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext context, string toolName,
			Dictionary<string, object?> args) {
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		toolNames.Should().Contain(toolName,
			because: "the tool must be discoverable before the end-to-end call");
		return await context.Session.CallToolAsync(
			toolName, new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore(
				"Configure McpE2E:Sandbox:EnvironmentName (with a CrtProcessBuilder of at least 1.6.6.4) to run "
				+ "the multi-instance Sub-process MCP E2E tests.");
		}

		if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName!)) {
			Assert.Ignore(
				$"Multi-instance Sub-process MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
		}

		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(5));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource, environmentName);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string EnvironmentName) : IAsyncDisposable {

		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

	#endregion

}
