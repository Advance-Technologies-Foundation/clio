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
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for <c>create-business-process</c>. NOT in CI — run manually. The advertised-tool test
/// is hermetic; the functional test builds a real (uniquely named) process and is gated on a reachable
/// environment with the ProcessDesignService package and a writable "Custom" package.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
[NonParallelizable]
[Category(ProcessDesignerE2EGate.CategoryName)]
public sealed class CreateBusinessProcessToolE2ETests {

	private const string ToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;

	// ENG-92127 element-output mapping tests need a user task that (a) ships in CrtBase — always installed on any
	// stand — and (b) exposes a scalar OUTPUT parameter (a valid mapping source). CheckCanExecuteOperationUserTask
	// resolves by name via ProcessUserTaskSchemaManager (the builder's full superset, not the palette subset), and
	// its Boolean output CanExecuteOperation is the source. Swap these two constants if a stand lacks it — e.g. the
	// palette task ActivityUserTask (alias performTask) with the Guid output ActivityResult.
	private const string SourceUserTaskName = "CheckCanExecuteOperationUserTask";
	private const string SourceOutputParameter = "CanExecuteOperation";

	// element->element pairing (AC#1): performTask (ActivityUserTask) exposes the Guid output ActivityResult, which
	// flows into CheckCanExecuteOperationUserTask's Guid input UserId (Guid<->Guid). Both tasks and this mapping are
	// live-verified on the stand. performTask is a built-in alias, so it ships wherever the designer does.
	private const string ElementSourceTaskType = "performTask";
	private const string ElementSourceOutput = "ActivityResult";
	private const string ElementTargetInput = "UserId";

	[Test]
	[Description("Starts the real clio MCP server and verifies create-business-process is discoverable via the get-tool-contract compact index (hermetic).")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process is discoverable on the lazy surface")]
	public async Task CreateBusinessProcess_Should_Be_Advertised_By_Mcp_Server() {
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
	[Description("Over the real MCP path, create-business-process builds a uniquely named process from an inline descriptor.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process builds a process from an inline descriptor")]
	public async Task CreateBusinessProcess_Should_BuildProcess_FromInlineDescriptor() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpE2e{Guid.NewGuid():N}";
		string descriptor = BuildDescriptor(processName);

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = descriptor
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a successful build should return a normal MCP tool result, not a transport error");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain(processName,
			because: "a successful build reports the created schema name (run against an environment with the ProcessDesignService package and a writable Custom package)");

		// Readback: describe the built process and confirm the structure is really there — a server that
		// returned success but built nothing would be caught here, unlike the success-echo assertion above.
		string describeJson = JsonSerializer.Serialize(await DescribeAsync(context, processName));
		describeJson.Should().Contain("task1",
			because: "the read-back graph must contain the user-task element that was actually built");
		describeJson.Should().Contain("buildType",
			because: "describe returns the structured element graph (buildType tokens), confirming a real build rather than an echo");
	}

	[Test]
	[Description("Over the real MCP path, create-business-process accepts the ENG-92127 type-mirror (typeFromElement) parameter, and describe-business-process reads the built process back with each parameter's direction surfaced.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process mirrors an element parameter's type and describe surfaces direction")]
	public async Task CreateBusinessProcess_Should_MirrorElementParameterType_AndSurfaceDirectionOnReadback() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpMapE2e{Guid.NewGuid():N}";
		string descriptor = BuildTypeMirrorDescriptor(processName);

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = descriptor
		});

		// Assert — the tool forwards the extended ENG-92127 contract and the server builds the process
		callResult.IsError.Should().NotBeTrue(
			because: "a descriptor using the typeFromElement type-mirror must build without a transport error");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain(processName,
			because: "a successful build reports the created schema name (run against an environment with the ProcessDesignService package and the ActivityUserTask 'Recommendation' parameter)");

		// Readback: the type-mirror process parameter exists, and describe now surfaces each parameter's direction
		// (the clio DescribedParameter DTO no longer strips direction/isResult — ENG-92127 describe enhancement).
		string describeJson = JsonSerializer.Serialize(await DescribeAsync(context, processName));
		describeJson.Should().Contain("MirroredType",
			because: "the typeFromElement type-mirror created a process parameter cloning the element parameter's exact type");
		// The describe graph is embedded as an escaped JSON string in the tool result, so match a quote-free
		// substring (like the sibling readbacks Contain("task1")/Contain("buildType")) — a quoted "direction"
		// would appear as \"direction\" and never match.
		describeJson.Should().Contain("direction",
			because: "describe-business-process now surfaces each parameter's direction over the real MCP path (the clio DescribedParameter DTO no longer strips it), so a caller can tell an element's outputs from its inputs");
	}

	[Test]
	[Description("Over the real MCP path, create-business-process maps a user-task element OUTPUT into a process parameter via a targetProcessParameter mapping (element->process, ENG-92127 AC#2); describe reads the process parameter and the element output back.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process maps an element output into a process parameter")]
	public async Task CreateBusinessProcess_Should_MapElementOutputIntoProcessParameter() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpOutMapE2e{Guid.NewGuid():N}";
		string descriptor = BuildElementOutputToProcessDescriptor(processName);

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = descriptor
		});

		// Assert — the server accepts the element->process mapping (an invalid one would error) and reports the build
		callResult.IsError.Should().NotBeTrue(
			because: "mapping a compatible element output into a process parameter must build without a transport error");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain(processName,
			because: $"a successful build reports the created schema name (run against an environment with CrtBase's {SourceUserTaskName})");

		// Readback: the target process parameter and the source element output are both present in the graph. The
		// binding value on the process parameter is an element-qualified meta-path built from UIds (not names), so we
		// assert the surfaced names + a successful build rather than substring-matching the meta-path.
		string describeJson = JsonSerializer.Serialize(await DescribeAsync(context, processName));
		describeJson.Should().Contain("ProcResult",
			because: "the target process parameter that receives the element output is present in the read-back graph");
		describeJson.Should().Contain(SourceOutputParameter,
			because: "the element's output parameter (the mapping source) is surfaced by describe because it is a result/output");
	}

	[Test]
	[Description("Over the real MCP path, create-business-process REJECTS an element->process mapping whose types are incompatible (a Boolean element output into an Integer process parameter), enforcing the ENG-92127 type-compatibility rule (AC#3).")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process rejects an incompatible-type mapping")]
	public async Task CreateBusinessProcess_Should_RejectMapping_WhenTypesAreIncompatible() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpBadTypeE2e{Guid.NewGuid():N}";
		string descriptor = BuildIncompatibleTypeMappingDescriptor(processName);

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = descriptor
		});

		// Assert — the type-compatibility gate rejects a Boolean source into an Integer target; the server's rejection
		// message (authored in ProcessMappingService) is surfaced through the MCP result (verified live on the stand
		// for the equivalent modify-business-process addMapping: "... incompatible data value types ...").
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("incompatible",
			because: "mapping a Boolean element output into an Integer process parameter must be rejected by the type-compatibility check");
	}

	[Test]
	[Description("Over the real MCP path, create-business-process REJECTS a self-referential parameter mapping (a process parameter mapped to itself) with the platform's circular-dependency validation — the pre-save interpretation-validation gate, which the per-mapping type check cannot catch.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process rejects a self-referential (circular) parameter mapping")]
	public async Task CreateBusinessProcess_Should_RejectSelfReferentialMapping_WithCircularDependency() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpCycleE2e{Guid.NewGuid():N}";
		string descriptor = BuildSelfReferentialMappingDescriptor(processName);

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = descriptor
		});

		// Assert — the pre-save platform interpretation-validation gate rejects the self-referential mapping.
		// Requires a stand whose clioprocessbuilder package includes the pre-save validation gate.
		string callResultJson = JsonSerializer.Serialize(callResult);
		// Primary, culture-stable: the clio-authored prefix that ONLY the gate emits (ProcessSchemaValidator) — proves
		// the gate fired regardless of the stand's profile culture (the platform's own message below is localizable).
		callResultJson.Should().Contain("Process validation failed",
			because: "the pre-save gate rejected the schema (clio-authored, culture-independent marker)");
		// Secondary: the specific platform rule. Platform-localized text, so this holds on an English-culture sandbox.
		callResultJson.Should().Contain("circular dependency",
			because: "a process parameter mapped to itself forms a circular dependency the platform rejects on save (a case the per-mapping type check does not detect)");
		// The rejected build must leave NO orphaned schema — describe reports the clio-owned 'was not found' message.
		string describeJson = JsonSerializer.Serialize(await DescribeAsync(context, processName));
		describeJson.Should().Contain("was not found",
			because: "a rejected build is rolled back, leaving no orphaned schema on the stand");
	}

	[Test]
	[Description("Over the real MCP path, create-business-process maps one element's OUTPUT into ANOTHER element's INPUT (element->element, ENG-92127 AC#1): performTask's Guid output ActivityResult into CheckCanExecuteOperationUserTask's Guid input UserId; describe reads the target element's input back bound to the source element's output.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process maps one element's output into another element's input")]
	public async Task CreateBusinessProcess_Should_MapElementOutputIntoAnotherElementInput() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpElemMapE2e{Guid.NewGuid():N}";
		string descriptor = BuildElementToElementDescriptor(processName);

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = descriptor
		});

		// Assert — the server accepts the element->element mapping (an invalid one would error) and reports the build
		callResult.IsError.Should().NotBeTrue(
			because: "mapping one element's output into another element's compatible input must build without a transport error");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain(processName,
			because: "a successful build reports the created schema name");

		// Readback: the target element's input parameter and the source element's output are both present in the
		// graph. The binding on the input is an element-qualified meta-path built from UIds (not names), so we assert
		// the surfaced names + a successful build rather than substring-matching the meta-path.
		string describeJson = JsonSerializer.Serialize(await DescribeAsync(context, processName));
		describeJson.Should().Contain(ElementTargetInput,
			because: "the target element's input parameter that receives the mapping is present in the read-back graph");
		describeJson.Should().Contain(ElementSourceOutput,
			because: "the source element's output parameter (the mapping source) is surfaced by describe because it is a result/output");
	}

	[Test]
	[Description("Over the real MCP path: a parameter built with a constant default value reads back with source ConstValue.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process persists a constant default value on a parameter")]
	public async Task CreateBusinessProcess_Should_PersistConstantDefault_OnParameter() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpDefaultE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptorWithDefault(processName)
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "building a process with a constant default must succeed");
		string describeJson = JsonSerializer.Serialize(await DescribeAsync(context, processName));
		describeJson.Should().Contain("ConstValue",
			because: "the parameter's constant default is persisted and reads back as a ConstValue source");
		describeJson.Should().Contain("Retries",
			because: "the defaulted parameter is present in the read-back graph");
	}

	[Test]
	[Description("Over the real MCP path: a parameter built with a caption and description reads both back via describe.")]
	[AllureTag(ToolName)]
	[AllureName("create-business-process persists caption and description on a parameter")]
	public async Task CreateBusinessProcess_Should_PersistCaptionAndDescription_OnParameter() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpDescE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptorWithDescription(processName)
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "building a process with a parameter caption + description must succeed");
		string describeJson = JsonSerializer.Serialize(await DescribeAsync(context, processName));
		describeJson.Should().Contain("Customer note",
			because: "the parameter caption is persisted and read back by describe-business-process");
		describeJson.Should().Contain("Free-text note about the customer",
			because: "the parameter description is persisted and read back by describe-business-process");
	}

	private static string BuildDescriptorWithDescription(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Desc E2E",
		  "packageName": "Custom",
		  "parameters": [ { "name": "Note", "type": "Text", "direction": "In", "caption": "Customer note", "description": "Free-text note about the customer" } ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "EndEvent1" }
		  ]
		}
		""";

	private static string BuildDescriptorWithDefault(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Default E2E",
		  "packageName": "Custom",
		  "parameters": [ { "name": "Retries", "type": "Integer", "direction": "In", "value": "3" } ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "EndEvent1" }
		  ]
		}
		""";

	// Reads the built process back as a structured graph via describe-business-process (for build readback).
	private static async Task<CallToolResult> DescribeAsync(ArrangeContext context, string processCode) =>
		await context.Session.CallToolAsync(
			DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["process-name"] = processCode
				}
			},
			context.CancellationTokenSource.Token);

	private static string BuildDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP E2E",
		  "packageName": "Custom",
		  "parameters": [ { "name": "MyText", "type": "Text", "direction": "In" } ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "task1", "type": "performTask" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "task1" },
		    { "source": "task1", "target": "EndEvent1" }
		  ],
		  "mappings": [
		    { "elementName": "task1", "elementParameter": "Recommendation", "processParameter": "MyText" }
		  ]
		}
		""";

	// ENG-92127: a process parameter whose type mirrors task1's "Recommendation" element parameter via
	// typeFromElement/typeFromElementParameter (the type is copied verbatim, no conversion), alongside a
	// process-parameter -> element-input mapping. Exercises the extended create-business-process contract.
	private static string BuildTypeMirrorDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Mapping E2E",
		  "packageName": "Custom",
		  "parameters": [
		    { "name": "MyText", "type": "Text", "direction": "In" },
		    { "name": "MirroredType", "typeFromElement": "task1", "typeFromElementParameter": "Recommendation", "direction": "Out" }
		  ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "task1", "type": "performTask" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "task1" },
		    { "source": "task1", "target": "EndEvent1" }
		  ],
		  "mappings": [
		    { "elementName": "task1", "elementParameter": "Recommendation", "processParameter": "MyText" }
		  ]
		}
		""";

	// ENG-92127 (AC#2): a Boolean process parameter (ProcResult) fed by the user-task element's OUTPUT via a
	// targetProcessParameter mapping — the element->process shape. Source/target share the Boolean type group.
	private static string BuildElementOutputToProcessDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Output Mapping E2E",
		  "packageName": "Custom",
		  "parameters": [
		    { "name": "ProcResult", "type": "Boolean", "direction": "Out" }
		  ],
		  "elements": [
		    { "name": "Start1", "type": "startEvent" },
		    { "name": "check", "type": "userTask", "userTaskName": "{{SourceUserTaskName}}" },
		    { "name": "End1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "Start1", "target": "check" },
		    { "source": "check", "target": "End1" }
		  ],
		  "mappings": [
		    { "targetProcessParameter": "ProcResult", "sourceElement": "check", "sourceElementParameter": "{{SourceOutputParameter}}" }
		  ]
		}
		""";

	// ENG-92127 (AC#3): an INCOMPATIBLE element->process mapping — the Boolean element output into an Integer
	// process parameter — which the type-compatibility gate must reject (Boolean and Number are different kinds).
	private static string BuildIncompatibleTypeMappingDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Bad-Type Mapping E2E",
		  "packageName": "Custom",
		  "parameters": [
		    { "name": "BadNum", "type": "Integer", "direction": "Out" }
		  ],
		  "elements": [
		    { "name": "Start1", "type": "startEvent" },
		    { "name": "check", "type": "userTask", "userTaskName": "{{SourceUserTaskName}}" },
		    { "name": "End1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "Start1", "target": "check" },
		    { "source": "check", "target": "End1" }
		  ],
		  "mappings": [
		    { "targetProcessParameter": "BadNum", "sourceElement": "check", "sourceElementParameter": "{{SourceOutputParameter}}" }
		  ]
		}
		""";

	// A self-referential parameter mapping — a process parameter mapped to ITSELF — forms a
	// circular dependency the platform interpretation validator rejects on save; the pre-save gate
	// (ProcessSchemaValidator -> GetProcessValidationResult) surfaces that rejection instead of persisting it.
	private static string BuildSelfReferentialMappingDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Self-Map E2E",
		  "packageName": "Custom",
		  "parameters": [
		    { "name": "SelfRef", "type": "Text", "direction": "Variable" }
		  ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "EndEvent1" }
		  ],
		  "mappings": [
		    { "targetProcessParameter": "SelfRef", "processParameter": "SelfRef" }
		  ]
		}
		""";

	// ENG-92127 (AC#1): one element's OUTPUT into ANOTHER element's INPUT. performTask (ActivityUserTask) exposes
	// the Guid output ActivityResult, which flows into CheckCanExecuteOperationUserTask's Guid input UserId
	// (Guid<->Guid). The source element precedes the target in the flow so its output exists first.
	private static string BuildElementToElementDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Element-to-Element E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "Start1", "type": "startEvent" },
		    { "name": "task1", "type": "{{ElementSourceTaskType}}" },
		    { "name": "check", "type": "userTask", "userTaskName": "{{SourceUserTaskName}}" },
		    { "name": "End1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "Start1", "target": "task1" },
		    { "source": "task1", "target": "check" },
		    { "source": "check", "target": "End1" }
		  ],
		  "mappings": [
		    { "elementName": "check", "elementParameter": "{{ElementTargetInput}}", "sourceElement": "task1", "sourceElementParameter": "{{ElementSourceOutput}}" }
		  ]
		}
		""";

	private static async Task<CallToolResult> CallToolAsync(ArrangeContext context, Dictionary<string, object?> args) {
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		toolNames.Should().Contain(ToolName,
			because: "the create-business-process tool must be discoverable via the get-tool-contract compact index before the end-to-end call");
		return await context.Session.CallToolAsync(
			ToolName, new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync(bool requireReachableEnvironment) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		ProcessDesignerE2EGate.SkipIfFeatureDisabled(settings);
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (requireReachableEnvironment) {
			if (string.IsNullOrWhiteSpace(environmentName)) {
				Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName (with the ProcessDesignService package) to run create-business-process MCP E2E.");
			}
			if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName!)) {
				Assert.Ignore($"create-business-process MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
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
