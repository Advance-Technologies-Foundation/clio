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
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Client;
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
public sealed class ModifyBusinessProcessToolE2ETests {

	private const string ToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;
	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;

	[Test]
	[Description("Starts the real clio MCP server and verifies modify-business-process is advertised (hermetic).")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process is advertised by the clio MCP server")]
	public async Task ModifyBusinessProcess_Should_Be_Advertised_By_Mcp_Server() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: false);

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);

		// Assert
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the modify-business-process tool must be discoverable on the real clio MCP server");
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
		  { "op": "addElement", "element": { "name": "SignalStart1", "type": "signalStart", "signal": { "entity": "UsrTestRunButton", "on": "save" } } },
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
		string describeJson = JsonSerializer.Serialize(await CallToolAsync(context, DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			}));
		describeJson.Should().Contain("ConstValue",
			because: "the parameter still carries a constant value source after setParameter");
		// Assert the direction field/value PAIRING, not a bare "Out" substring (which could match unrelated
		// tokens such as element names). Normalize away quote-escaping (" / \") and pretty-print spacing first.
		string normalizedDescribe = describeJson
			.Replace("\\", string.Empty).Replace("u0022", string.Empty)
			.Replace("\"", string.Empty).Replace(" ", string.Empty);
		normalizedDescribe.Should().Contain("direction:Out",
			because: "setParameter changed the direction to Out and describe reads it back paired with the direction field");
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

	private static string SetParameterOperations() =>
		"""
		[ { "op": "setParameter", "parameterName": "Amount", "parameterUpdate": { "value": "7", "caption": "Amount due", "direction": "Out" } } ]
		""";

	private static string SetParameterDescriptionOperations() =>
		"""
		[ { "op": "setParameter", "parameterName": "Amount", "parameterUpdate": { "description": "How much to charge the customer", "caption": "Amount due" } } ]
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

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext context, string toolName,
		Dictionary<string, object?> args) {
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(toolName,
			because: $"the {toolName} tool must be advertised before the end-to-end call");
		return await context.Session.CallToolAsync(
			toolName, new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync(bool requireReachableEnvironment) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string environmentName = settings.Sandbox.EnvironmentName;
		if (requireReachableEnvironment) {
			if (string.IsNullOrWhiteSpace(environmentName)) {
				Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName (with the ProcessDesignService package) to run modify-business-process MCP E2E.");
			}
			if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName)) {
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
		string EnvironmentName) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
