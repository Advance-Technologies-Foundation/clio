using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Command.ProcessModel;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for <c>modify-business-process</c>. NOT in CI — run manually. The advertised-tool test
/// is hermetic; the functional test builds a uniquely named process and then edits it (replace the start event
/// with a record-signal start), gated on a reachable environment with the ProcessDesignService package and a
/// writable "Custom" package.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(ModifyBusinessProcessTool.ModifyBusinessProcessToolName)]
[NonParallelizable]
[Category(ProcessDesignerE2EGate.CategoryName)]
public sealed class ModifyBusinessProcessToolE2ETests {

	private const string ToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;
	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;

	[Test]
	[Description("Starts the real clio MCP server and verifies modify-business-process is discoverable via the get-tool-contract compact index (hermetic).")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process is discoverable on the lazy surface of the clio MCP server")]
	public async Task ModifyBusinessProcess_Should_Be_Advertised_By_Mcp_Server() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: false);

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: $"the {ToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
	}

	[Test]
	[Description("Over the real MCP path, builds a process then edits it (replace start with a record-signal start).")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process edits an existing process")]
	public async Task ModifyBusinessProcess_Should_EditExistingProcess() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpModifyE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptor(processName)
		});

		// Act
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = BuildOperations()
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a successful edit should return a normal MCP tool result, not a transport error");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain(processName,
			because: "a successful edit reports the edited schema name (run against an environment with the ProcessDesignService package)");

		// Readback: describe the edited process and confirm the signal start really replaced the simple start —
		// a server that returned success but applied nothing would be caught here, unlike the success echo above.
		string describeJson = JsonSerializer.Serialize(await CallToolAsync(context, DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			}));
		describeJson.Should().Contain("signalstart",
			because: "the edit added a signalStart element, which must appear in the read-back structured graph");
	}

	[Test]
	[Description("Over the real MCP path, builds a process then adds process parameters via addParameter, including a Lookup referenceSchema; identifies the process by name only (exercises the optional processUid path).")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process adds parameters including a lookup referenceSchema")]
	public async Task ModifyBusinessProcess_Should_AddParametersIncludingLookup() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpAddParamE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptor(processName)
		});

		// Act — processName only (processUid omitted) also exercises the optional-identity path
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = BuildAddParameterOperations()
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "addParameter (including a Lookup referenceSchema) must succeed over the real MCP path");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain(processName,
			because: "a successful edit reports the edited schema name (run against an environment with the ProcessDesignService package and a 'City' object)");
	}

	[Test]
	[Description("Over the real MCP path, builds a signal-start process then sets a data source filter via modify-business-process setFilter (describe confirms the distinctive value round-trips), then removes it via clearFilter (describe confirms it is gone). Covers the setFilter/clearFilter modify ops end-to-end (mandatory MCP e2e gate).")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process setFilter then clearFilter round-trips through describe")]
	public async Task ModifyBusinessProcess_Should_SetThenClearSignalStartFilter() {
		// Arrange — a signal-start process with NO filter yet.
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpSetFilterE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildSignalStartDescriptor(processName)
		});

		// Act 1 — setFilter with a distinctive constant.
		CallToolResult setResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = BuildSetFilterOperations()
		});

		// Assert 1 — set succeeds and describe reads the filter back (the distinctive value proves it was applied,
		// not merely echoed — a server that returned success but serialized nothing is caught here).
		setResult.IsError.Should().NotBeTrue(
			because: "setFilter on a signalStart must apply without a transport error");
		JsonSerializer.Serialize(setResult).Should().Contain(processName,
			because: "a successful setFilter reports the edited schema name");
		string afterSet = JsonSerializer.Serialize(await CallToolAsync(context, DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			}));
		afterSet.Should().Contain("ClioModifyFilterProbe",
			because: "setFilter serialized the signalStart EntityFilters and describe decodes the distinctive value back");

		// Act 2 — clearFilter.
		CallToolResult clearResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = BuildClearFilterOperations()
		});

		// Assert 2 — clear succeeds and the distinctive value is gone on read-back.
		clearResult.IsError.Should().NotBeTrue(
			because: "clearFilter must remove the filter without a transport error");
		string afterClear = JsonSerializer.Serialize(await CallToolAsync(context, DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			}));
		afterClear.Should().NotContain("ClioModifyFilterProbe",
			because: "clearFilter removed the signalStart filter, so its distinctive value must be gone on read-back");
	}

	// A signal-start process with NO filter — the base for the setFilter/clearFilter e2e (setFilter targets a
	// signalStart or a DataSourceFilters-exposing data element). Contact.Name is a base column on every stand.
	private static string BuildSignalStartDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP SetFilter E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "SignalStart1", "type": "signalStart", "signal": { "entity": "Contact", "on": "modified" } },
		    { "name": "task1", "type": "performTask" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "SignalStart1", "target": "task1" },
		    { "source": "task1", "target": "EndEvent1" }
		  ]
		}
		""";

	private static string BuildSetFilterOperations() =>
		"""
		[
		  { "op": "setFilter", "elementName": "SignalStart1",
		    "filter": { "object": "Contact", "logicalOperation": "and",
		      "conditions": [ { "column": "Name", "comparison": "contains", "value": "ClioModifyFilterProbe" } ] } }
		]
		""";

	private static string BuildClearFilterOperations() =>
		"""
		[
		  { "op": "clearFilter", "elementName": "SignalStart1" }
		]
		""";

	private static string BuildDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Modify E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "task1", "type": "performTask" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "task1" },
		    { "source": "task1", "target": "EndEvent1" }
		  ]
		}
		""";

	private static string BuildOperations() =>
		"""
		[
		  { "op": "removeElement", "elementName": "StartEvent1" },
		  { "op": "addElement", "element": { "name": "SignalStart1", "type": "signalStart", "signal": { "entity": "Contact", "on": "save" } } },
		  { "op": "addFlow", "source": "SignalStart1", "target": "task1" }
		]
		""";

	private static string BuildAddParameterOperations() =>
		"""
		[
		  { "op": "addParameter", "parameter": { "name": "RecordId", "type": "Guid", "direction": "In", "caption": "Record Id" } },
		  { "op": "addParameter", "parameter": { "name": "City", "referenceSchema": "City", "direction": "In" } }
		]
		""";

	[Test]
	[Description("Over the real MCP path: builds a process with a constant-default parameter, then setParameter changes its value, caption and direction in place; describe-business-process reads back the constant value and the new direction.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process sets a parameter value/direction and the read-back reflects it")]
	public async Task ModifyBusinessProcess_Should_SetParameter_AndReadBackValueAndDirection() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpSetParamE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptorWithParameter(processName)
		});

		// Act — setParameter updates value, caption and direction in place
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = SetParameterOperations()
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "setParameter must succeed over the real MCP path");
		DescribedParameter amount = ParseDescribeResult(await CallToolAsync(context, DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			})).Parameters.Single(p => p.Name == "Amount");
		amount.Source.Should().Be("ConstValue",
			because: "the parameter still carries a constant value source after setParameter");
		amount.Value.Should().Be("7", because: "setParameter updated the constant default value to 7");
		amount.Direction.Should().Be("Out",
			because: "setParameter changed the direction to Out, which describe reads back on the parameter");
	}

	[Test]
	[Description("Over the real MCP path: setParameter updates a parameter's description (and caption) in place; describe-business-process reads both back.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process sets a parameter description and the read-back reflects it")]
	public async Task ModifyBusinessProcess_Should_SetParameterDescription_AndReadBack() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpSetDescE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptorWithParameter(processName)
		});

		// Act — setParameter updates the description and caption in place
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = SetParameterDescriptionOperations()
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "setParameter updating the description must succeed over the real MCP path");
		string describeJson = JsonSerializer.Serialize(await CallToolAsync(context, DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			}));
		describeJson.Should().Contain("How much to charge the customer",
			because: "setParameter updated the description and describe-business-process reads it back");
		describeJson.Should().Contain("Amount due",
			because: "setParameter also updated the caption and describe-business-process reads it back");
	}

	[Test]
	[Description("Over the real MCP path: the modify addParameter op carries a caption + description on a newly added parameter; describe-business-process reads them back.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process adds a parameter with a description")]
	public async Task ModifyBusinessProcess_Should_AddParameterWithDescription_AndReadBack() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpAddDescE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptor(processName)
		});

		// Act — addParameter carrying a caption + description on the new parameter
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = AddParameterWithDescriptionOperations()
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "addParameter carrying a description must succeed over the real MCP path");
		string describeJson = JsonSerializer.Serialize(await CallToolAsync(context, DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			}));
		describeJson.Should().Contain("Note added via the addParameter op",
			because: "the added parameter's description reads back via describe-business-process");
		describeJson.Should().Contain("Added note",
			because: "the added parameter's caption reads back via describe-business-process");
	}

	[Test]
	[Description("Over the real MCP path: removeParameter is hard-blocked when an element mapping still references the parameter, with an error naming the usage site (mirrors the visual designer).")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process blocks removing a parameter an element mapping still references")]
	public async Task ModifyBusinessProcess_Should_BlockRemoveParameter_WhenReferenced() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpRemoveParamE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptorWithMappedParameter(processName)
		});

		// Act — attempt to remove a parameter that the task mapping references
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = """[ { "op": "removeParameter", "parameterName": "Linked" } ]"""
		});

		// Assert — the dependency block surfaces; the parameter must NOT be silently removed
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("Cannot remove",
			because: "removing a referenced parameter is hard-blocked, not applied");
		callResultJson.Should().Contain("Linked",
			because: "the block message names the parameter that is still referenced");
	}

	[Test]
	[Description("Over the real MCP path: setParameter rejects an actual data-type change with a clear error; the parameter is not migrated.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process rejects a parameter data-type change")]
	public async Task ModifyBusinessProcess_Should_RejectDataTypeChange() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpTypeChangeE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptorWithParameter(processName)
		});

		// Act — try to change the Integer 'Amount' to Text
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = """[ { "op": "setParameter", "parameterName": "Amount", "parameterUpdate": { "type": "Text" } } ]"""
		});

		// Assert
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("data type",
			because: "changing a parameter's data type is rejected, not applied");
		callResultJson.Should().Contain("Amount",
			because: "the rejection names the parameter whose type change was refused");
	}

	[Test]
	[Description("Over the real MCP path: addParameter rejects an unsupported (complex) type — Binary — with a clear error, even though the platform resolves that type name.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process rejects an unsupported parameter type")]
	public async Task ModifyBusinessProcess_Should_RejectUnsupportedParameterType() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpBadTypeE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptor(processName)
		});

		// Act — try to add a Binary parameter (a deferred complex type)
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = """[ { "op": "addParameter", "parameter": { "name": "Blob", "type": "Binary" } } ]"""
		});

		// Assert
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("not supported",
			because: "only the supported scalar / lookup types may be created");
		callResultJson.Should().Contain("Binary",
			because: "the rejection names the unsupported type");
	}

	[Test]
	[Description("Over the real MCP path: setParameter rejects a constant value that cannot convert to the parameter's data type (a non-numeric string for an Integer), using the platform value converter.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process rejects a type-invalid constant parameter value")]
	public async Task ModifyBusinessProcess_Should_RejectTypeInvalidConstantValue() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpBadValueE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptorWithParameter(processName)
		});

		// Act — set the Integer 'Amount' default to a non-numeric string
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = """[ { "op": "setParameter", "parameterName": "Amount", "parameterUpdate": { "value": "not-a-number" } } ]"""
		});

		// Assert
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("not valid",
			because: "a constant value that cannot convert to the parameter's type is rejected");
		callResultJson.Should().Contain("Amount",
			because: "the rejection names the parameter whose value was invalid");
	}

	[Test]
	[Description("Over the real MCP path: modify-business-process REJECTS an addMapping that maps a process parameter to itself, via the platform's pre-save interpretation validation (circular dependency); the edit is not persisted and the process survives.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process rejects a self-referential (circular) parameter mapping")]
	public async Task ModifyBusinessProcess_Should_RejectSelfReferentialMapping_WithCircularDependency() {
		// Arrange — build a valid process carrying a mappable process parameter
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpModCycleE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptorWithSelfMappableParameter(processName)
		});

		// Act — map the process parameter to itself (a circular dependency), which validates a design-session instance
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = """[ { "op": "addMapping", "mapping": { "targetProcessParameter": "SelfRef", "processParameter": "SelfRef" } } ]"""
		});

		// Assert — the pre-save gate rejects the edit on the design instance (the build-path E2E covers a freshly built
		// schema; this covers the modify path's GetDesignInstance state, a different schema state).
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("Process validation failed",
			because: "the pre-save gate rejected the edit (clio-authored, culture-independent marker)");
		callResultJson.Should().Contain("circular dependency",
			because: "mapping a process parameter to itself is a circular dependency the platform rejects on save (English-culture sandbox)");
		// The rejected edit is discarded and the design session released — the process itself still exists and reads back.
		DescribeProcessResult described = ParseDescribeResult(await CallToolAsync(context, DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			}));
		// ParseDescribeResult already throws unless the process reads back (proving it survived); assert a DISCRIMINATING
		// value — the parameter is still unbound (source "None"), so the rejected self-mapping was NOT persisted.
		DescribedParameter selfRef = described.Parameters.Single(parameter => parameter.Name == "SelfRef");
		selfRef.Source.Should().Be("None",
			because: "a rejected modify discards the edit — the process survives and SelfRef stays unbound (the self-mapping was not persisted)");
	}

	private static string BuildDescriptorWithSelfMappableParameter(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Modify Cycle E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "EndEvent1" }
		  ],
		  "parameters": [
		    { "name": "SelfRef", "type": "Text", "direction": "Variable" }
		  ]
		}
		""";

	private static string SetParameterOperations() =>
		"""
		[ { "op": "setParameter", "parameterName": "Amount", "parameterUpdate": { "value": "7", "caption": "Amount due", "direction": "Out" } } ]
		""";

	private static string SetParameterDescriptionOperations() =>
		"""
		[ { "op": "setParameter", "parameterName": "Amount", "parameterUpdate": { "description": "How much to charge the customer", "caption": "Amount due" } } ]
		""";

	private static string AddParameterWithDescriptionOperations() =>
		"""
		[ { "op": "addParameter", "parameter": { "name": "AddedNote", "type": "Text", "direction": "In", "caption": "Added note", "description": "Note added via the addParameter op" } } ]
		""";

	private static string BuildDescriptorWithParameter(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP SetParam E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "task1", "type": "performTask" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "task1" },
		    { "source": "task1", "target": "EndEvent1" }
		  ],
		  "parameters": [
		    { "name": "Amount", "type": "Integer", "direction": "In", "caption": "Amount", "value": "1" }
		  ]
		}
		""";

	private static string BuildDescriptorWithMappedParameter(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP RemoveParam E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "task1", "type": "performTask" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "task1" },
		    { "source": "task1", "target": "EndEvent1" }
		  ],
		  "parameters": [
		    { "name": "Linked", "type": "Integer", "direction": "In" }
		  ],
		  "mappings": [
		    { "elementName": "task1", "elementParameter": "Duration", "processParameter": "Linked" }
		  ]
		}
		""";

	// Extracts the described graph from the MCP tool result and deserializes it into the typed DescribeProcessResult,
	// so a test can assert a parameter's fields (direction/source/value) directly instead of substring-matching the
	// serialized envelope. The graph is the Info log-message value inside the clio command envelope.
	private static DescribeProcessResult ParseDescribeResult(CallToolResult callResult) {
		JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
		JsonElement content = JsonSerializer.SerializeToElement(callResult.Content);
		foreach (JsonElement block in content.EnumerateArray()) {
			if (!block.TryGetProperty("text", out JsonElement textElement)
					|| textElement.ValueKind != JsonValueKind.String) {
				continue;
			}
			string? envelopeJson = textElement.GetString();
			if (string.IsNullOrWhiteSpace(envelopeJson) || !envelopeJson.TrimStart().StartsWith("{", StringComparison.Ordinal)) {
				continue;
			}
			using JsonDocument envelope = JsonDocument.Parse(envelopeJson);
			if (!envelope.RootElement.TryGetProperty("execution-log-messages", out JsonElement messages)
					|| messages.ValueKind != JsonValueKind.Array) {
				continue;
			}
			foreach (JsonElement message in messages.EnumerateArray()) {
				if (!message.TryGetProperty("value", out JsonElement value) || value.ValueKind != JsonValueKind.String) {
					continue;
				}
				string? graphJson = value.GetString();
				if (string.IsNullOrWhiteSpace(graphJson) || !graphJson.TrimStart().StartsWith("{", StringComparison.Ordinal)) {
					continue;
				}
				try {
					DescribeProcessResult? graph = JsonSerializer.Deserialize<DescribeProcessResult>(graphJson, options);
					if (graph is { SchemaUId: not null }) {
						return graph;
					}
				} catch (JsonException) {
					// Not the structured-graph log message; keep scanning.
				}
			}
		}
		throw new InvalidOperationException("The describe-business-process MCP result did not contain a structured graph.");
	}

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext context, string toolName,
		Dictionary<string, object?> args) {
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		toolNames.Should().Contain(toolName,
			because: $"the {toolName} tool must be discoverable via the get-tool-contract compact index before the end-to-end call");
		return await context.Session.CallToolAsync(
			toolName, new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync(bool requireReachableEnvironment) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		ProcessDesignerE2EGate.SkipIfFeatureDisabled(settings);
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (requireReachableEnvironment) {
			if (string.IsNullOrWhiteSpace(environmentName)) {
				Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName (with the ProcessDesignService package) to run modify-business-process MCP E2E.");
			}
			if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName!)) {
				Assert.Ignore($"modify-business-process MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
			}
		}
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource, environmentName);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string? EnvironmentName) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
