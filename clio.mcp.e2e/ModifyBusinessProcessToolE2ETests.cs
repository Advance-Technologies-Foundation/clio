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
using Clio.Mcp.E2E.Support.Results;
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
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class ModifyBusinessProcessToolE2ETests {

	private const string ToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;
	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;
	private const string DescribeToolName = DescribeProcessTool.ToolName;

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
	[Description("Over the real MCP path, setFlow re-kinds an existing flow in place — the operation ENG-91853 added and the one nothing else in this suite sends. Two directions in one call, because they fail differently: sequence -> conditional must store the condition, and conditional -> sequence is the clear-condition route. The source is an ORDINARY element, and that is the correction: an earlier version of this test asked for kind sequence on a flow out of a GATEWAY, which the builder refuses whenever a conditional sibling exists, so its expected outcome was unreachable and the operation before it could never have been committed either. Unit tests build the operation record positionally in C#, so the JSON binder for op/kind/condition is exercised nowhere else.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process re-kinds a flow with setFlow in both directions")]
	public async Task ModifyBusinessProcess_Should_ReKindAFlowWithSetFlow() {
		// Arrange — an ordinary element with one plain and two conditional branches. Both re-kinds below
		// stay legal on it: neither leaves a conditional flow beside two unconditional ones, and neither
		// drops the LAST conditional flow off an element that still has other outgoing flows.
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpSetFlowE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildThreeBranchTaskDescriptor(processName)
		});

		// Act — make the plain branch conditional, and clear the condition off one that already is.
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = """
				[
				  { "op": "setFlow", "source": "Decide", "target": "EndA", "kind": "conditional",
				    "condition": "1 > 0" },
				  { "op": "setFlow", "source": "Decide", "target": "EndC", "kind": "sequence" }
				]
				"""
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "re-kinding a flow in both directions must complete without a transport error");

		DescribeProcessResult described = ParseDescribeResult(await CallToolAsync(context, DescribeToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName, ["process-name"] = processName
			}));
		DescribedFlow toA = described.Flows.Single(f => f.Source == "Decide" && f.Target == "EndA");
		DescribedFlow toC = described.Flows.Single(f => f.Source == "Decide" && f.Target == "EndC");

		toA.Kind.Should().Be("conditional",
			because: "setFlow's kind has to reach the builder through the JSON binder - dropped, the flow stays "
				+ "plain and the operation reports success on an edit that did nothing");
		toA.Condition.Should().Be("1 > 0",
			because: "kind and condition are separate fields on the same operation and are dropped separately");
		toC.Kind.Should().Be("sequence",
			because: "this is the clear-condition route off an ordinary element - a flow that stayed "
				+ "conditional here means the op was accepted and ignored");
	}

	[Test]
	[Description("Off a deciding GATEWAY, setFlow kind sequence is refused whenever a conditional sibling exists - the designer cannot draw a plain flow out of a gateway either. This is the fork the tool description and the guidance now state, and it is asserted here because the previous version of the test above assumed the opposite and could never have passed.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process refuses a plain flow out of a gateway that has a conditional branch")]
	public async Task ModifyBusinessProcess_Should_RefuseAPlainFlowOutOfADecidingGateway() {
		// Arrange — a gateway with a default branch and a conditional one.
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpGatewayPlainE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildTwoBranchDescriptor(processName)
		});

		// Act — ask for the kind the designer has no palette entry for.
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = """
				[
				  { "op": "setFlow", "source": "Decide", "target": "EndA", "kind": "sequence" }
				]
				"""
		});

		// Assert - on the call text rather than IsError, which this surface measures null on a refusal.
		JsonSerializer.Serialize(callResult).Should().Contain("chooses between its branches",
			because: "the refusal must say WHY a plain flow cannot leave a gateway, or the caller retries it");

		DescribeProcessResult described = ParseDescribeResult(await CallToolAsync(context, DescribeToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName, ["process-name"] = processName
			}));
		described.Flows.Single(f => f.Source == "Decide" && f.Target == "EndA").Kind.Should().Be("default",
			because: "a refused operation must leave the flow exactly as it was");
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
		callResultJson.Should().Contain(CommandExecutionResult.CompileNotRequiredNote,
			because: "an edited process stays interpreted and needs no compile; the success result carries the compile-not-required note over the real MCP path so an agent does not force compile-creatio (ENG-95706)");

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
	[Description("Over the real MCP path, setElement's sendEmail recipient semantics are MATCH-OR-APPEND against a real server: re-sending an address the line already carries is a no-op (no duplicate), while a genuinely new address appends. Unit tests assert only that the operation JSON is passed through, so this is the only coverage that can catch the merge behaviour drifting.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process match-or-appends sendEmail recipients")]
	public async Task ModifyBusinessProcess_Should_MatchOrAppendSendEmailRecipients() {
		// Arrange — a sendEmail element that already carries one To address.
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpEmailRcptE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildSendEmailWithRecipientDescriptor(processName)
		});

		// Act 1 — re-send the SAME address. Match-or-append means this must change nothing.
		CallToolResult repeat = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = BuildRecipientOperations("first@example.com")
		});
		repeat.IsError.Should().NotBeTrue(because: "re-applying an identical recipient is a valid no-op, not an error");

		DescribedEmail afterRepeat = await ReadEmailAsync(context, processName);
		afterRepeat.To.Should().NotBeNull().And.HaveCount(1,
			because: "an entry whose resolved source and value the line already carries must NOT be appended again — "
				+ "a duplicate would make the process email that address twice, and there is no removal path to undo it");

		// Act 2 — send a genuinely different address. That must append rather than replace.
		CallToolResult appended = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = BuildRecipientOperations("second@example.com")
		});
		appended.IsError.Should().NotBeTrue(because: "adding a new recipient through setElement is supported");

		// Assert
		DescribedEmail afterAppend = await ReadEmailAsync(context, processName);
		afterAppend.To.Should().NotBeNull().And.HaveCount(2,
			because: "a new address APPENDS to the existing line — it must not replace what was already there");
		afterAppend.To!.Select(recipient => recipient.Value).Should()
			.Contain("first@example.com").And.Contain("second@example.com",
				because: "both addresses must survive: append semantics, not overwrite");
	}

	// Reads the process back and returns the sendEmail element's email block, so a recipient assertion can be made
	// against typed fields instead of substring-matching the escaped MCP envelope.
	private static async Task<DescribedEmail> ReadEmailAsync(ArrangeContext context, string processName) {
		CallToolResult describeResult = await CallToolAsync(context, DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			});
		CommandExecutionEnvelope envelope = McpCommandExecutionParser.Extract(describeResult);
		string graphJson = envelope.Output!
			.Select(message => message.Value)
			.First(value => !string.IsNullOrWhiteSpace(value)
				&& value!.TrimStart().StartsWith("{", StringComparison.Ordinal))!;
		DescribeProcessResult graph = JsonSerializer.Deserialize<DescribeProcessResult>(graphJson,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
		return graph.Elements.Single(element => element.Name == "SendEmail1").Email!;
	}

	// A sendEmail element seeded with exactly one To recipient, so the modify calls that follow are measured against
	// a known starting count.
	private static string BuildSendEmailWithRecipientDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Email Recipients E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "SendEmail1", "type": "sendEmail",
		      "email": { "mode": "manual", "subject": "Recipient merge probe",
		        "to": [ { "value": "first@example.com" } ] } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "SendEmail1" },
		    { "source": "SendEmail1", "target": "EndEvent1" }
		  ]
		}
		""";

	// A setElement op whose email block carries a single To recipient — the same shape for both the duplicate and the
	// genuinely-new case, so the only variable between the two acts is the address itself.
	private static string BuildRecipientOperations(string address) =>
		$$"""
		[ { "op": "setElement", "elementName": "SendEmail1",
		    "elementUpdate": { "email": { "to": [ { "value": "{{address}}" } ] } } } ]
		""";

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
	[Description("Over the real MCP path: a Lookup PROCESS PARAMETER default set through addParameter's 'value' gets the referenced record's NAME resolved into valueDisplay exactly like a mapping does - and, on that same route, an already-composed [#Lookup...#] macro is REFUSED rather than decoded. Pins the documented distinction between the two paths: name resolution is shared (ProcessParameterService and ProcessMappingService both call the validator), the macro decode is the mapping route only (NormalizeConstantValue has no counterpart in ProcessParameterService). A refactor collapsing either path fails here.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process resolves a Lookup default's display name on addParameter and refuses the macro there")]
	public async Task ModifyBusinessProcess_Should_ResolveLookupDefaultName_OnAddParameter_AndRefuseTheMacroThere() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpLookupDefaultE2e{Guid.NewGuid():N}";
		await CreateProcessAsync(context, processName, BuildPerformTaskDescriptor(processName));

		// Act 1 - a Lookup process parameter whose default is a bare record Guid
		await ModifyExpectingSuccessAsync(context, processName, $$"""
			[ { "op": "addParameter", "parameter": { "name": "DefaultCategory", "referenceSchema": "ActivityCategory",
			    "direction": "In", "value": "{{ToDoActivityCategoryId}}" } } ]
			""");
		DescribeProcessResult described = ParseDescribeResult(await CallToolAsync(context, DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			}));
		DescribedParameter category = described.Parameters.Single(parameter => parameter.Name == "DefaultCategory");

		// Assert 1 - the default is the ConstValue the runtime reads, and it is NAMED, on this route too
		category.Source.Should().Be("ConstValue",
			because: "a process parameter's lookup default is stored in the same encoding as an element-parameter constant");
		category.Value.Should().BeEquivalentTo(ToDoActivityCategoryId,
			because: "the stored value stays the bare record id - the name is added beside it, never in its place");
		AssertResolvedDisplayName(category.ValueDisplay, category.Value);

		// Act 2 - the macro form on THIS route: setParameter has no decode, so it must be refused, not stored
		CallToolResult refusal = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = $$"""[ { "op": "setParameter", "parameterName": "DefaultCategory", "parameterUpdate": { "value": "[#Lookup.{{ActivityCategoryObjectUId}}.{{ToDoActivityCategoryId}}#]" } } ]"""
		});

		// Assert 2 - refused with the bare-Guid route named, and the earlier default untouched
		SerializeToolText(refusal).Should().Contain("expression",
			because: "addParameter/setParameter take a bare Guid only; the macro decode is the MAPPING route, so here the "
				+ "macro must fall through to the standard non-Guid refusal that names the routes");
		DescribedParameter after = ParseDescribeResult(await CallToolAsync(context, DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			})).Parameters.Single(parameter => parameter.Name == "DefaultCategory");
		after.Value.Should().BeEquivalentTo(ToDoActivityCategoryId,
			because: "a refused setParameter leaves the parameter exactly as it was - the macro was neither decoded nor stored");
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

	[Test]
	[Description("Over the real MCP path, builds a signal-start process then restricts it to a tracked-change column via modify-business-process setSignal (describe confirms changedColumns round-trips), then clears column tracking via setSignal with no changedColumns (describe confirms it is gone). Covers the setSignal modify op end-to-end (mandatory MCP e2e gate).")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process setSignal sets then clears tracked-change columns through describe")]
	public async Task ModifyBusinessProcess_Should_SetThenClearSignalTrackedColumns() {
		// Arrange — a signal-start process firing on ANY change (no tracked columns yet).
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpSetSignalE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildSignalStartDescriptor(processName)
		});

		// Act 1 — setSignal restricts the trigger to the Name column.
		CallToolResult setResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = BuildSetSignalColumnsOperations()
		});

		// Assert 1 — set succeeds and describe reads the tracked column back (the distinctive changedColumns field
		// proves the op applied, not merely that a signalStart still exists).
		setResult.IsError.Should().NotBeTrue(
			because: "setSignal restricting a signalStart to a tracked column must apply without a transport error");
		JsonSerializer.Serialize(setResult).Should().Contain(processName,
			because: "a successful setSignal reports the edited schema name");
		string afterSet = JsonSerializer.Serialize(await CallToolAsync(context, DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			}));
		afterSet.Should().Contain("changedColumns",
			because: "setSignal stored the tracked column and describe decodes the signal's changedColumns back");
		afterSet.Should().Contain("Name",
			because: "the tracked column Name round-trips: setSignal resolved it to a column UId and describe decoded it back to the name");

		// Act 2 — setSignal with no changedColumns clears column tracking.
		CallToolResult clearResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = BuildClearSignalColumnsOperations()
		});

		// Assert 2 — clear succeeds and changedColumns is gone on read-back (fires on any change again).
		clearResult.IsError.Should().NotBeTrue(
			because: "setSignal without changedColumns must clear column tracking without a transport error");
		string afterClear = JsonSerializer.Serialize(await CallToolAsync(context, DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			}));
		afterClear.Should().NotContain("changedColumns",
			because: "setSignal cleared column tracking, so the signal fires on any change and describe emits no changedColumns");
	}

	[Test]
	[Description("Over the real MCP path: setFilter with a date-only `equal` on a DateTime column (Contact.CreatedOn), then describe reads the value back as the BARE date (2026-05-01), not a full ISO midnight. Proves the whole-day-trim round-trip fix end-to-end. Self-diagnosing: a full-ISO (…T00:00:00) read-back means an older CrtProcessBuilder package is deployed on the stand.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process setFilter date-only equal round-trips as a bare date")]
	public async Task ModifyBusinessProcess_Should_RoundTripDateOnlyFilterAsBareDate() {
		// Arrange — a signal-start process with NO filter yet.
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpDateTrimE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildSignalStartDescriptor(processName)
		});

		// Act — setFilter: CreatedOn (a DateTime column) equal a BARE date (no time).
		CallToolResult setResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = BuildSetDateOnlyFilterOperations()
		});

		// Assert — the date-only value round-trips through describe as a bare date, not a full ISO midnight.
		setResult.IsError.Should().NotBeTrue(
			because: "setFilter with a date-only value on a DateTime column must apply without a transport error");
		string afterSet = JsonSerializer.Serialize(await CallToolAsync(context, DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			}));
		afterSet.Should().Contain("2026-05-01",
			because: "the date-only filter value round-trips through describe");
		afterSet.Should().NotContain("2026-05-01T00:00:00",
			because: "a whole-day-trimmed date-only equal reads back as the BARE date (2026-05-01), NOT a full ISO midnight — proving today's reader round-trip fix is on the stand; a full-ISO read-back means an older CrtProcessBuilder package is deployed");
	}

	[Test]
	[Description("Over the real MCP path: setFilter on a signalStart REJECTS a condition whose right-hand side is a processParameter reference (a signal is evaluated before any process instance exists). Asserts the friendly rejection surfaces over MCP and that describe afterwards shows the signalStart still carries NO filter (the rejected edit was not persisted). Env-gated coverage for the promised negative case.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process rejects a parameter-reference filter on a signalStart")]
	public async Task ModifyBusinessProcess_Should_RejectSignalStartParameterReferenceFilter() {
		// Arrange — a signal-start process carrying a process parameter, so the filter references a REAL parameter and
		// the ONLY reason for rejection is the signalStart restriction (not an unresolved parameter name).
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpSignalRefE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildSignalStartWithParameterDescriptor(processName)
		});

		// Act — setFilter comparing Contact.Name to a process parameter on the signalStart (not allowed there).
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = BuildSignalStartParameterReferenceFilterOperations()
		});

		// Assert — the friendly rejection surfaces over MCP (same envelope pattern as the other reject tests).
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("process/element parameter",
			because: "a signalStart filter cannot compare a column to a process/element parameter, and the friendly server message must surface over the real MCP path");
		callResultJson.Should().Contain("SignalStart1",
			because: "the rejection names the offending element so the agent can locate it");

		// Readback: the rejected edit was discarded — the signalStart still carries NO filter (discriminating: the
		// referenced parameter legitimately appears in the params list, so absence of the element filter is the proof).
		DescribeProcessResult described = ParseDescribeResult(await CallToolAsync(context, DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			}));
		DescribedElement signalStart = described.Elements.Single(element => element.Name == "SignalStart1");
		signalStart.Filter.Should().BeNull(
			because: "the rejected setFilter was discarded (any failure aborts the edit) — the signalStart carries no filter on read-back");
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

	// setSignal restricting the existing signalStart to a tracked-change column (Contact.Name, a base column on every
	// stand). setSignal resolves the name to a column UId in place; describe decodes it back — proving the tracked
	// column round-trips through the setSignal op, not merely that the signal still exists.
	private static string BuildSetSignalColumnsOperations() =>
		"""
		[
		  { "op": "setSignal", "elementName": "SignalStart1",
		    "signal": { "on": "modified", "changedColumns": ["Name"] } }
		]
		""";

	[Test]
	[Description("Over the real MCP path, modify-business-process setElement switches an existing element's useBackgroundMode IN PLACE (off on a signalStart whose kind default is on, then back on), and describe reports the change — the element-level equivalent of toggling the checkbox on an existing element in the visual designer.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process setElement toggles useBackgroundMode on an existing element")]
	public async Task ModifyBusinessProcess_Should_ToggleElementBackgroundMode() {
		// Arrange — a signal-start process; the signalStart kind defaults to background mode.
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpSetElementE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildSignalStartDescriptor(processName)
		});

		// Act 1 — switch background mode OFF on the existing element.
		CallToolResult offResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = BuildSetElementBackgroundModeOperations(enabled: false)
		});

		// Assert 1
		offResult.IsError.Should().NotBeTrue(
			because: "setElement on an existing element must apply without a transport error");
		ParseDescribeResult(await CallToolAsync(context, DescribeProcessTool.ToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			}))
			.Elements.Single(element => element.Name == "SignalStart1").UseBackgroundMode.Should().BeFalse(
				because: "setElement turned background mode off on the existing signal start and the change persisted");

		// Act 2 — switch it back ON, proving the field is fully changeable, not one-way.
		CallToolResult onResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = BuildSetElementBackgroundModeOperations(enabled: true)
		});

		// Assert 2
		onResult.IsError.Should().NotBeTrue(because: "the reverse setElement must apply too");
		ParseDescribeResult(await CallToolAsync(context, DescribeProcessTool.ToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			}))
			.Elements.Single(element => element.Name == "SignalStart1").UseBackgroundMode.Should().BeTrue(
				because: "background mode is switchable both ways on an existing element");
	}

	// setElement toggling the element-level useBackgroundMode on the existing signal start.
	private static string BuildSetElementBackgroundModeOperations(bool enabled) =>
		$$"""
		[
		  { "op": "setElement", "elementName": "SignalStart1",
		    "elementUpdate": { "useBackgroundMode": {{(enabled ? "true" : "false")}} } }
		]
		""";

	// setSignal with NO changedColumns clears column tracking, so the signal fires on any change again.
	private static string BuildClearSignalColumnsOperations() =>
		"""
		[
		  { "op": "setSignal", "elementName": "SignalStart1",
		    "signal": { "on": "modified" } }
		]
		""";

	// A date-only equal on Contact.CreatedOn (a base DateTime column on every stand). The server sets
	// trimDateTimeParameterToDate so the whole day matches; describe must read the value back as the bare date.
	private static string BuildSetDateOnlyFilterOperations() =>
		"""
		[
		  { "op": "setFilter", "elementName": "SignalStart1",
		    "filter": { "object": "Contact", "logicalOperation": "and",
		      "conditions": [ { "column": "CreatedOn", "comparison": "equal", "value": "2026-05-01" } ] } }
		]
		""";

	// A signal-start process carrying a process parameter — the base for the negative test that a signalStart filter
	// may NOT reference a parameter. The parameter exists so the rejection is unambiguously the signalStart
	// restriction, not an unresolved parameter name.
	private static string BuildSignalStartWithParameterDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP SignalRef E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "SignalStart1", "type": "signalStart", "signal": { "entity": "Contact", "on": "modified" } },
		    { "name": "task1", "type": "performTask" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "SignalStart1", "target": "task1" },
		    { "source": "task1", "target": "EndEvent1" }
		  ],
		  "parameters": [
		    { "name": "NameFilter", "type": "Text", "direction": "In", "caption": "Name filter" }
		  ]
		}
		""";

	// A signalStart setFilter whose right-hand side is a process parameter — not allowed on a signalStart (evaluated
	// before a process instance exists); the server's FilterParameterGuard rejects it.
	private static string BuildSignalStartParameterReferenceFilterOperations() =>
		"""
		[
		  { "op": "setFilter", "elementName": "SignalStart1",
		    "filter": { "object": "Contact", "logicalOperation": "and",
		      "conditions": [ { "column": "Name", "comparison": "equal", "processParameter": "NameFilter" } ] } }
		]
		""";

	[Test]
	[Description("Over the real MCP path, setFlowCondition turns an existing plain flow into a conditional one and the condition reads back through describe. Unit tests cannot reach this: the platform's SaveSchema is non-virtual, so persisting the re-kinded flow and reading it back is only provable against a real server.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process sets a flow condition that reads back")]
	public async Task ModifyBusinessProcess_Should_SetFlowCondition_ThatReadsBack() {
		// Arrange - a linear start -> task -> end process, whose task->end flow is a plain sequence flow.
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpFlowCondE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptor(processName)
		});

		// Act
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = BuildSetFlowConditionOperations("1 == 1")
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "setting a condition on an existing flow is a supported edit; no gateway is needed, the platform "
				+ "synthesizes one for a conditional flow whose source is an activity");
		DescribedFlow branch = await ReadFlowAsync(context, processName, "task1", "EndEvent1");
		branch.Kind.Should().Be("conditional",
			because: "the flow must be re-kinded to a real conditional flow, not merely carry the condition text");
		branch.Condition.Should().Be("1 == 1",
			because: "the condition has to survive the save AND clio's own re-serialize - DescribedFlow has no "
				+ "typed property, and a caller reads it by NAME - the extension-data bag added later carries an "
				+ "undeclared field but does not make it addressable, so a missing property still costs the "
				+ "caller the answer");
	}

	[Test]
	[Description("Over the real MCP path, a conditional flow reads back with branchesOnActivityResult present and FALSE. The field's whole purpose is to tell a caller that a branch's condition text will be ignored at run time, and a property lost in clio's re-serialize does not surface as an error, it surfaces as a flow that silently claims its condition is live - DescribedFlow has an extension-data bag now, which keeps an UNDECLARED field alive but does not make it addressable by name. The TRUE case cannot be arranged here: only the designer populates a flow's activity-result map, and setFlowCondition refuses to write a condition onto one, so it stays a manual case against a hand-authored process.")]
	[AllureTag(ToolName)]
	[AllureTag(DescribeToolName)]
	[AllureName("describe reports branchesOnActivityResult on a conditional flow")]
	public async Task ModifyBusinessProcess_Should_ReportBranchesOnActivityResult_OnAConditionalFlow() {
		// Arrange - the same linear process the condition round-trip uses, so the only new thing under test
		// is the field itself.
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpBranchFlagE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptor(processName)
		});
		await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = BuildSetFlowConditionOperations("1 == 1")
		});

		// Act
		CallToolResult describeResult = await CallToolAsync(context, DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			});

		// Assert
		string describeJson = JsonSerializer.Serialize(describeResult);
		describeJson.Should().Contain("branchesOnActivityResult",
			because: "the property has to reach the caller - a flow whose condition the platform ignores is "
				+ "indistinguishable from one it evaluates unless describe says so");
		DescribedFlow branch = await ReadFlowAsync(context, processName, "task1", "EndEvent1");
		branch.BranchesOnActivityResult.Should().BeFalse(
			because: "a condition written through setFlowCondition is a FORMULA branch; reporting it as "
				+ "result-driven would tell the caller its own condition will never be evaluated");
		branch.Condition.Should().Be("1 == 1",
			because: "the two fields are read from the same flow and both have to survive the round trip - "
				+ "asserting the flag alone would pass on a describe that dropped the condition");
	}

	[Test]
	[Description("Over the real MCP path, an unrecognised macro family is REFUSED by the platform's own pre-save validation. This test asserted a WARNING until it was first run against a stand; the correction is the point, and it is also what removed the package's own accept-with-a-notice for such a family, since the notice was raised and then dropped on every shape anyone measured. Measured at 1.4.0.38 over three families - a fictional one and the two REAL ones the package deliberately did not allow-list, [#ColumnValue...#] and [#SamplingColumnValue...#] - all three refused with 'Process validation failed'; the same holds on a CONDITION, where [#Price#] > 100 is refused with 'Expression expected (at index 0)'. From 1.4.0.41 there is no package-side check on this path at all, so this test now asserts the only thing that ever refused. The Warning channel is real on other paths (see BuildProcessResponse.Warnings) but a macro family cannot demonstrate it on any shape.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process refuses an unrecognised macro family at the platform gate")]
	public async Task ModifyBusinessProcess_Should_RefuseAnUnrecognisedMacroFamily_AtThePlatformGate() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpWarnE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			// The FORMULA-TARGET descriptor, which declares the Sum parameter the mapping below targets.
			// With the plain one this test arranged a mapping onto a parameter that does not exist, so the
			// operation was refused with "Process parameter 'Sum' was not found." before the macro-family
			// notice could be raised - a test that never reached the guard it names. It had never been run
			// against a stand, because this tier is not in CI.
			["descriptor"] = BuildFormulaTargetDescriptor(processName)
		});

		// Act - a macro family no converter resolves, on a MAPPING. The platform's pre-save validation is what
		// refuses it, and since 1.4.0.41 it is the only thing that looks at the expression at all.
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = BuildFormulaMappingOperations("[#UsrUnknownDialect.Something#]")
		});

		// Assert
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("Process validation failed",
			because: "the refusal comes from the PLATFORM's pre-save validation, not from this package's "
				+ "validator - naming the phrase is what distinguishes the two, and an earlier version of this "
				+ "test asserted a Warning that no stand ever produced");
		callResultJson.Should().Contain("UsrUnknownDialect",
			because: "the refusal has to quote the expression, or the caller cannot see which macro the platform "
				+ "could not convert. It survives VERBATIM here for a reason worth knowing: the platform quotes "
				+ "the CONVERTED text, and an unrecognised family is exactly the text no converter touches - a "
				+ "fractional literal would come back as 1.5m and a parameter reference as the parameter name");
	}

	[Test]
	[Description("Over the real MCP path, an invalid condition is refused BY THE SERVER and nothing is written. This is the check that proves validation is server-side rather than a client-side convenience an agent could route around.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process refuses an invalid flow condition")]
	public async Task ModifyBusinessProcess_Should_RefuseInvalidFlowCondition() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpBadCondE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptor(processName)
		});

		// Act - references an identifier that does not exist in this process.
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = BuildSetFlowConditionOperations("NoSuchThing == 1")
		});

		// Assert
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("NoSuchThing",
			because: "the refusal must NAME the offending identifier - that is what makes it actionable");
		callResultJson.Should().Contain("Formula value error",
			because: "the refusal has to come from the PLATFORM's formula validation and be recognisable as such. "
				+ "This assertion used to pin \"which does not exist\", the wording of the package's own "
				+ "unknown-identifier arm; that validator is gone, the platform writes 'Formula value error: "
				+ "Parameter \"NoSuchThing\" not found' instead, and pinning the platform's phrase is what keeps "
				+ "this from passing on a generic failure that never reached formula validation at all");
		callResultJson.Should().Contain("not found",
			because: "the ARM has to be pinned, not just the identifier: every platform formula refusal carries "
				+ "the expression text, so the identifier alone would be present whichever fault ran, and only the "
				+ "unknown-identifier arm says 'not found'");
		DescribedFlow branch = await ReadFlowAsync(context, processName, "task1", "EndEvent1");
		branch.Kind.Should().Be("sequence",
			because: "a refused edit is atomic: the flow must be left exactly as it was, not half-converted");
		branch.Condition.Should().BeNull(
			because: "nothing may be stored when validation refused the condition");
	}

	[Test]
	[Description("Over the real MCP path, a condition whose parameter reference does not resolve is refused with a SENTENCE rather than with a serialised error object. This is the one class where the platform's own text is materially worse than the validator CrtProcessBuilder 1.4.0.41 deleted: the flow-schema generator throws ProcessParameterValidateException carrying ProcessParameterErrorInfo.ToString(), which is Json.Serialize, so the caller was handed 'Internal error: \"{ErrorType:2,ErrorData:{ParameterUId:\"…\"}}\"' and no remedy. PlatformValidationMessage rewrites that one blob server-side; the rewrite is FORMATTING of the platform's verdict, so nothing here decides validity. Only a real server can prove it: the blob is produced inside the platform's generator, which no unit test reaches.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process explains an unresolvable parameter reference in a condition")]
	public async Task ModifyBusinessProcess_Should_ExplainAnUnresolvableParameterReference_InACondition() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpRefBlobE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptor(processName)
		});

		// Act - a well-formed parameter metapath whose UId is on no parameter of this process. The SHAPE has to
		// be valid, or the fault is a parse error and the generator's error-info path is never reached.
		const string missingParameterUId = "11111111-1111-1111-1111-111111111111";
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = BuildSetFlowConditionOperations(
				$"[#[Parameter:{{{missingParameterUId}}}]#] > 0")
		});

		// Assert
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain(missingParameterUId,
			because: "the UId is the only thing that says WHICH reference is wrong, so the rewrite must keep it");
		callResultJson.Should().Contain("is not in this process",
			because: "the caller has to be told what is wrong; that is the whole reason the rewrite exists");
		callResultJson.Should().NotContain("ErrorType",
			because: "the serialised ProcessParameterErrorInfo must be REPLACED, not annotated - leaving it "
				+ "beside the sentence would report the same fault twice and reads as a defect in clio");
		DescribedFlow branch = await ReadFlowAsync(context, processName, "task1", "EndEvent1");
		branch.Kind.Should().Be("sequence",
			because: "a refused edit is atomic - a message change must not have made the refusal non-atomic");
	}

	// Reads the process back and returns one flow, so a condition assertion can be made against typed fields
	// instead of substring-matching the escaped MCP envelope.
	private static async Task<DescribedFlow> ReadFlowAsync(ArrangeContext context, string processName,
		string source, string target) {
		CallToolResult describeResult = await CallToolAsync(context, DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			});
		CommandExecutionEnvelope envelope = McpCommandExecutionParser.Extract(describeResult);
		string graphJson = envelope.Output!
			.Select(message => message.Value)
			.First(value => !string.IsNullOrWhiteSpace(value)
				&& value!.TrimStart().StartsWith("{", StringComparison.Ordinal))!;
		DescribeProcessResult graph = JsonSerializer.Deserialize<DescribeProcessResult>(graphJson,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
		return graph.Flows.Single(flow => flow.Source == source && flow.Target == target);
	}

	private static string BuildSetFlowConditionOperations(string condition) =>
		$$"""
		[
		  { "op": "setFlowCondition", "source": "task1", "target": "EndEvent1", "condition": "{{condition}}" }
		]
		""";

	[Test]
	[Description("Over the real MCP path, an 'expression' mapping is validated, stored and read back. This is the OTHER use site of a formula. The check is the PLATFORM's, at the pre-save gate, on any parameter value whose Source is Script - which is every one of these. Unit tests cannot reach it: SaveSchema is non-virtual, so persisting a Script value and reading it back is only provable against a real server.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process stores a formula mapping that reads back")]
	public async Task ModifyBusinessProcess_Should_StoreAndReadBackAFormulaMapping() {
		// Arrange - a Float parameter, so a decimal-valued formula fits it.
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpFormulaE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildFormulaTargetDescriptor(processName)
		});

		// Act
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = BuildFormulaMappingOperations("FormulaUtilities.Max(1, 2, 3)")
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "FormulaUtilities.Max is one of the four Creatio formula functions and fits a Float parameter");
		DescribedParameter parameter = await ReadParameterAsync(context, processName, "Sum");
		parameter.Source.Should().Be("Script",
			because: "a formula is stored as a Script source, not a constant - that is how the runtime knows to evaluate it");
		parameter.Value.Should().Be("FormulaUtilities.Max(1, 2, 3)",
			because: "the formula text must survive the save verbatim; the platform, not clio, decides its meaning");
	}

	[Test]
	[Description("Over the real MCP path, a formula whose result cannot become the target's declared type is refused BY THE SERVER and nothing is stored. This check must agree with the platform's own pre-save gate: accepting here would only defer the same failure to save time with a worse message.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process refuses a formula that does not fit the target type")]
	public async Task ModifyBusinessProcess_Should_RefuseFormulaMapping_WhenResultTypeDoesNotFitTheTarget() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpFormulaTypeE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildFormulaTargetDescriptor(processName)
		});

		// Act - a fractional literal into the INTEGER parameter. Conversion retypes it as decimal, which an
		// Integer target cannot hold; the same expression into the Float parameter is legitimate.
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = BuildAmountMappingOperations("1.5")
		});

		// Assert
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("Int32",
			because: "the refusal must name the TARGET type, so a caller can tell a type failure from a syntax one");
		callResultJson.Should().Contain("1.5",
			because: "the refusal has to echo the expression at all, and the bare '1.5' is deliberately the "
				+ "substring BOTH forms share: the platform quotes it as its own converter left it, so what "
				+ "comes back is '1.5m' - asserting on the converted form would pin a platform detail, and "
				+ "asserting it is quoted 'as written' would assert something measurably untrue");
		DescribedParameter parameter = await ReadParameterAsync(context, processName, "Amount");
		parameter.Source.Should().NotBe("Script",
			because: "a refused mapping must leave the parameter unbound, not half-applied");
	}

	[Test]
	[Description("Over the real MCP path, a formula referencing a parameter that is not in the process is refused and the offending token is NAMED - the ticket's AC5. The reference layer is what a caller cannot check for itself, so this is the refusal that carries the most weight.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process refuses a formula with a dangling parameter reference")]
	public async Task ModifyBusinessProcess_Should_RefuseFormulaMapping_WhenItReferencesAMissingParameter() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpFormulaRefE2e{Guid.NewGuid():N}";
		string missing = Guid.NewGuid().ToString();
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildFormulaTargetDescriptor(processName)
		});

		// Act
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = BuildFormulaMappingOperations("[#[Parameter:{" + missing + "}]#]")
		});

		// Assert
		JsonSerializer.Serialize(callResult).Should().Contain(missing,
			because: "AC5 requires the refusal to NAME the reference that does not resolve, not merely to refuse");
		DescribedParameter parameter = await ReadParameterAsync(context, processName, "Sum");
		parameter.Source.Should().NotBe("Script",
			because: "nothing may be stored when the reference layer refused the formula");
	}

	// Reads one process parameter back, so a formula assertion can be made against typed fields instead of
	// substring-matching the escaped MCP envelope.
	private static async Task<DescribedParameter> ReadParameterAsync(ArrangeContext context, string processName,
		string parameterName) {
		CallToolResult describeResult = await CallToolAsync(context, DescribeProcessTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			});
		CommandExecutionEnvelope envelope = McpCommandExecutionParser.Extract(describeResult);
		string graphJson = envelope.Output!
			.Select(message => message.Value)
			.First(value => !string.IsNullOrWhiteSpace(value)
				&& value!.TrimStart().StartsWith("{", StringComparison.Ordinal))!;
		DescribeProcessResult graph = JsonSerializer.Deserialize<DescribeProcessResult>(graphJson,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
		return graph.Parameters.Single(parameter => parameter.Name == parameterName);
	}

	// A process carrying one Integer and one Float process parameter - the pair that makes the type rule
	// observable: the same fractional formula is refused for Integer and accepted for Float.
	private static string BuildFormulaTargetDescriptor(string processName) =>
		"{\"name\":\"" + processName + "\",\"caption\":\"Clio BP Formula E2E\",\"packageName\":\"Custom\","
		+ "\"elements\":[{\"name\":\"StartEvent1\",\"type\":\"startEvent\"},"
		+ "{\"name\":\"EndEvent1\",\"type\":\"endEvent\"}],"
		+ "\"flows\":[{\"source\":\"StartEvent1\",\"target\":\"EndEvent1\"}],"
		+ "\"parameters\":[{\"name\":\"Amount\",\"type\":\"Integer\",\"direction\":\"Variable\"},"
		+ "{\"name\":\"Sum\",\"type\":\"Float\",\"direction\":\"Variable\"}]}";

	private static string BuildFormulaMappingOperations(string expression) =>
		"[{\"op\":\"addMapping\",\"mapping\":{\"targetProcessParameter\":\"Sum\",\"expression\":\""
		+ expression.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}}]";

	private static string BuildAmountMappingOperations(string expression) =>
		"[{\"op\":\"addMapping\",\"mapping\":{\"targetProcessParameter\":\"Amount\",\"expression\":\""
		+ expression.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}}]";

	// A gateway with two branches, so both directions of a setFlow re-kind are legal on it: a lone
	// unconditional flow out of a deciding gateway would be normalised to the default branch instead.
	// An ORDINARY element as the branch source, which is where both re-kind directions are legal. One plain
	// flow and two conditional ones: re-kinding the plain one leaves no unconditional sibling behind, and
	// re-kinding EndC leaves EndB conditional, so neither operation trips a rule.
	private static string BuildThreeBranchTaskDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP SetFlow E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "Start1", "type": "startEvent" },
		    { "name": "Decide", "type": "performTask" },
		    { "name": "EndA", "type": "endEvent" },
		    { "name": "EndB", "type": "endEvent" },
		    { "name": "EndC", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "Start1", "target": "Decide" },
		    { "source": "Decide", "target": "EndA" },
		    { "source": "Decide", "target": "EndB", "kind": "conditional", "condition": "2 > 1" },
		    { "source": "Decide", "target": "EndC", "kind": "conditional", "condition": "3 > 1" }
		  ]
		}
		""";

	private static string BuildTwoBranchDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP SetFlow E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "Start1", "type": "startEvent" },
		    { "name": "Decide", "type": "exclusiveGateway" },
		    { "name": "EndA", "type": "endEvent" },
		    { "name": "EndB", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "Start1", "target": "Decide" },
		    { "source": "Decide", "target": "EndA", "kind": "default" },
		    { "source": "Decide", "target": "EndB", "kind": "conditional", "condition": "2 > 1" }
		  ]
		}
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
	[Description("Over the real MCP path: removeParameter is blocked when a conditional flow's CONDITION references the parameter, and the refusal names the flow. This is the arm a review found advertised and unverified - the sibling test above is scoped to the element-mapping arm in its own [Description], and no other test here touches the delete guard. It matters more than a routine coverage gap because the condition scan is load-bearing for a decision made in the same change: describe reports a condition on every flow that carries the text, INCLUDING one whose branch the platform decides from an activity result, precisely because the guards scan it - so hiding it would leave a caller refused over something no read API shows. The package pins the scan in unit tests; this pins that the whole path, through the MCP surface, still refuses.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process blocks removeParameter referenced by a flow condition")]
	public async Task ModifyBusinessProcess_Should_BlockRemoveParameter_WhenAFlowConditionReferencesIt() {
		// Arrange - a process with a parameter, then a conditional flow whose condition references it by
		// the meta-path form describe reports.
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpCondGuardE2e{Guid.NewGuid():N}";
		await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = BuildDescriptorWithMappedParameter(processName)
		});
		CallToolResult describeResult = await CallToolAsync(context, DescribeToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName
		});
		DescribeProcessResult described = ParseDescribeResult(describeResult);
		string guardedUId = described.Parameters.Single(parameter => parameter.Name == "Linked").UId;

		CallToolResult conditionResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = $$"""
				[ { "op": "setFlowCondition", "source": "task1", "target": "EndEvent1",
				    "condition": "[#[Parameter:{{{guardedUId}}}]#] > 0" } ]
				"""
		});
		JsonSerializer.Serialize(conditionResult).Should().NotContain("\"isError\":true",
			because: "the condition has to be STORED for the guard to have anything to find - a failure here "
				+ "would make the assertion below pass for the wrong reason");

		// Act - remove the parameter the condition references.
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = """[ { "op": "removeParameter", "parameterName": "Linked" } ]"""
		});

		// Assert
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("Cannot remove",
			because: "a parameter a live condition references is hard-blocked, not applied - the alternative is "
				+ "a dangling reference the platform reports later as a raw GUID");
		callResultJson.Should().Contain("condition on flow",
			because: "the refusal has to name the SITE, not just refuse: the caller cannot re-point a reference "
				+ "it is not shown, and 'names each usage site' is what the contract promises");
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
	// --- Connections ("Connected to") -------------------------------------------------------------------
	// These are the rows of the feature's verification matrix that only a real stand can answer. Everything
	// below the wire is unit-tested in the package; what these prove is that the operations are reachable
	// over the real MCP path, that the platform accepts the shape we write, and that the read-back a caller
	// gets is re-appliable.
	//
	// Every assertion here goes through the TYPED describe model rather than a substring of the serialized
	// envelope, and that is deliberate: a `Contain("AccountRef")` passes on the process-parameter list alone,
	// before any connection exists, and a `Contain("record")` matches the word in an unrelated error. Both
	// were written that way first and both were green for the wrong reason. Every modify call is also checked
	// for success, because a refusal that goes unchecked turns "the operation left the sibling alone" into
	// "the operation never ran".
	//
	// A perform task is used because it is the one connection-capable user task with no CreateActivity gate,
	// so a failure here can never be the effectiveness rule.

	[Test]
	[Description("Over the real MCP path: setConnections binds a connection on a perform task, and describe-business-process reads it back with BOTH the raw persisted macro and a decoded source in the shape setConnections accepts.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process setConnections binds a connection that describe reads back decoded")]
	public async Task ModifyBusinessProcess_Should_BindConnection_AndDescribeShouldDecodeIt() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpConnBindE2e{Guid.NewGuid():N}";
		await CreateProcessAsync(context, processName, BuildPerformTaskDescriptor(processName));

		// Act — ONE describe, so the connection and the element-level verdict below are read from the same
		// snapshot. Two round trips would look like one to a reader and would put live I/O in the Assert block.
		await ModifyExpectingSuccessAsync(context, processName,
			SetAccountConnectionFromProcessParameterOperations());
		DescribedElement task = await ReadTaskAsync(context, processName);
		DescribedConnection connection = (task.Connections ?? []).Single(c => c.Column == "Account");

		// Assert
		connection.ProcessParameter.Should().Be("AccountRef",
			because: "the decoded source names the PROCESS PARAMETER, not a platform metapath — that decode is the whole point of the hybrid read-back, and it is what makes the output re-appliable");
		connection.Value.Should().Contain("Parameter:",
			because: "the raw persisted macro travels alongside the decoded form, which is what lets an unrecognised future macro survive a round trip");
		connection.Registered.Should().BeTrue(
			because: "Account carries a shipped connection-registry row, so the connection is a full citizen rather than the invisible half-citizen case");
		// The element-level capability verdict, asserted on the wire rather than in the package. Its own unit
		// tests prove the RULE; only a real describe proves the answer survives serialization at all — and a
		// silently dropped member is exactly the failure this feature already shipped once, where four new
		// fields were promised by the tool description and dropped by clio's DTO. `true` is the only correct
		// answer for a perform task: it has no CreateActivity gate, so the connection cannot be inert.
		task.WritesConnectionsAtRuntime.Should().BeTrue(
			because: "a perform task writes its connections unconditionally, and null here would mean the verdict never reached the caller who is told to read it before trusting a binding");
	}

	[Test]
	[Description("Over the real MCP path: a fixed-record connection needs NO entity-schema UId — the caller sends a bare recordId and the platform macro is synthesised from the target column's own reference entity.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process setConnections synthesises the lookup macro from a bare recordId")]
	public async Task ModifyBusinessProcess_Should_SynthesiseLookupMacro_FromBareRecordId() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpConnRecordE2e{Guid.NewGuid():N}";
		await CreateProcessAsync(context, processName, BuildPerformTaskDescriptor(processName));
		Guid recordId = Guid.NewGuid();

		// Act
		await ModifyExpectingSuccessAsync(context, processName,
			SetConnectionFromRecordIdOperations("Account", recordId));
		DescribedConnection connection = await ReadConnectionAsync(context, processName, "Account");

		// Assert
		Guid.Parse(connection.RecordId!).Should().Be(recordId,
			because: "the record the caller named must be the record that was bound (parsed, so a casing or brace difference in the round trip is not read as a behavioural failure)");
		connection.ReferenceSchema.Should().Be("Account",
			because: "the entity-schema half of the macro is composed server-side and read back as a NAME, so the decoded source is re-appliable as-is");
		// On Activity every connection column is named after the entity it references, so the assertion above
		// cannot by itself distinguish a resolved entity from the requested column echoed back. What discriminates
		// is the macro: its first half must be a GUID the caller never sent — the reference-entity UId the server
		// looked up — and it must not be the record id. (The package's own unit tests pin the resolution itself,
		// with an entity deliberately named unlike its column.)
		string[] macroParts = connection.Value!.Replace("[#Lookup.", string.Empty).Replace("#]", string.Empty)
			.Split('.');
		macroParts.Should().HaveCount(2,
			because: "the fixed-record macro is exactly [#Lookup.<entitySchemaUId>.<recordId>#]");
		Guid.TryParse(macroParts[0], out Guid macroSchemaUId).Should().BeTrue(
			because: "the entity half is a schema UId the server composed from the target column, and the caller sent no UId at all");
		macroSchemaUId.Should().NotBe(recordId,
			because: "the two halves must be different values, or the 'synthesised from the column' claim would hold for a macro that just repeated the record id");
		connection.Value.Should().StartWith("[#Lookup.",
			because: "the persisted form is the platform's lookup macro, which the caller never had to write");
	}

	[Test]
	[Description("Over the real MCP path: setConnections is an UPSERT keyed on column — setting one connection leaves another already-bound one intact. Replace semantics would silently clear the sibling, and a cleared connection is filtered out of describe, so the damage would be invisible in the artefact meant to verify it.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process setConnections leaves an unlisted connection alone")]
	public async Task ModifyBusinessProcess_Should_LeaveUnlistedConnectionAlone_WhenSettingAnother() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpConnUpsertE2e{Guid.NewGuid():N}";
		await CreateProcessAsync(context, processName, BuildPerformTaskDescriptor(processName));
		Guid contactRecordId = Guid.NewGuid();
		Guid accountRecordId = Guid.NewGuid();
		await ModifyExpectingSuccessAsync(context, processName,
			SetConnectionFromRecordIdOperations("Contact", contactRecordId));

		// Act — a second call that names ONLY Account
		await ModifyExpectingSuccessAsync(context, processName,
			SetConnectionFromRecordIdOperations("Account", accountRecordId));

		// Assert — both columns, because "the sibling survived" is only meaningful if the listed one was written
		IReadOnlyList<DescribedConnection> connections = await ReadConnectionsAsync(context, processName);
		Guid.Parse(connections.Single(c => c.Column == "Contact").RecordId!).Should().Be(contactRecordId,
			because: "a column absent from the request must be left exactly as it was — the one behaviour a replace reading of set* would break, invisibly");
		Guid.Parse(connections.Single(c => c.Column == "Account").RecordId!).Should().Be(accountRecordId,
			because: "and the column that WAS listed must have been written, or the test would pass for a request that did nothing");
	}

	[Test]
	[Description("Over the real MCP path: clearConnections unbinds a connection, the connection then DISAPPEARS from describe, and the operation reports that it cleared something — which is the only way a caller can tell 'cleared' from 'never bound' afterwards.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process clearConnections unbinds and reports it")]
	public async Task ModifyBusinessProcess_Should_ClearConnection_AndReportIt() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpConnClearE2e{Guid.NewGuid():N}";
		await CreateProcessAsync(context, processName, BuildPerformTaskDescriptor(processName));
		Guid recordId = Guid.NewGuid();
		await ModifyExpectingSuccessAsync(context, processName,
			SetConnectionFromRecordIdOperations("Account", recordId));
		// Positive control: without it, "the connection is gone" is equally satisfied by "it was never bound".
		(await ReadConnectionsAsync(context, processName)).Should().ContainSingle(c => c.Column == "Account",
			because: "the connection must be present BEFORE the clear, or its later absence proves nothing");

		// Act
		CallToolResult clearResult = await ModifyExpectingSuccessAsync(context, processName,
			ClearConnectionOperations("Account"));

		// Assert
		SerializeToolText(clearResult).Should().Contain("CLEARED",
			because: "the result is the only place a caller learns the unbind happened, since the read-back can only show what remains");
		(await ReadConnectionsAsync(context, processName)).Should().NotContain(c => c.Column == "Account",
			because: "an unbound connection is filtered out of the read-back");
	}

	[Test]
	[Description("Over the real MCP path: a column the host entity does not have is REFUSED with the data-model diagnosis rather than written, and the refusal says the change belongs outside this operation.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process setConnections refuses a column the host entity lacks")]
	public async Task ModifyBusinessProcess_Should_RefuseConnection_WhenHostHasNoSuchColumn() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpConnNoColumnE2e{Guid.NewGuid():N}";
		await CreateProcessAsync(context, processName, BuildPerformTaskDescriptor(processName));

		// Act
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = SetConnectionFromRecordIdOperations("UsrClioNoSuchSection", Guid.NewGuid())
		});

		// Assert
		string text = SerializeToolText(callResult);
		text.Should().Contain("UsrClioNoSuchSection",
			because: "the refusal must name the column the caller asked for");
		text.Should().Contain("has no",
			because: "the diagnosis has to say WHICH precondition failed — the host entity lacking the column needs a data-model change this operation deliberately does not make");
		(await ReadConnectionsAsync(context, processName)).Should().BeEmpty(
			because: "a refused operation aborts the whole edit, so nothing may have been written");
	}

	[Test]
	[Description("Over the real MCP path: an expression whose macro family cannot hold a record reference is refused. Without this the platform stores it verbatim, the process compiles, and the column is silently never written.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process setConnections refuses a mistyped macro family")]
	public async Task ModifyBusinessProcess_Should_RefuseConnection_WhenMacroFamilyCannotHoldARecord() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpConnBadMacroE2e{Guid.NewGuid():N}";
		await CreateProcessAsync(context, processName, BuildPerformTaskDescriptor(processName));

		// Act
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = SetAccountConnectionFromDateConstantOperations()
		});

		// Assert
		string text = SerializeToolText(callResult);
		text.Should().Contain("DateValue",
			because: "the refusal quotes the offending macro family, which is what makes it actionable rather than a generic rejection");
		(await ReadConnectionsAsync(context, processName)).Should().BeEmpty(
			because: "the named failure mode is a value that persists and writes nothing, so proving the refusal means proving nothing was written");
	}

	// --- Perform task parameter families (ENG-91846) ------------------------------------------------------
	// The rows of the ticket's verification matrix that only a real stand can answer: the scheduling pairs,
	// the booleans, the performer expression route, the Recommendation constant (materialized into the schema
	// RESOURCE by SaveSchema — live-verified to reach Activity.Title), and the bare-Guid ActivityCategory
	// ConstValue the S2 validator relaxation admits (the encoding the runtime's allowed-results derivation
	// actually reads). Every assertion goes through the TYPED describe model, per this file's own rationale.

	[Test]
	[Description("Over the real MCP path: one modify-business-process call configures a Perform task's three scheduling pairs, both booleans, the performer (expression route), Recommendation, InformationOnStep, plus ActivityCategory AND ActivityPriority as bare-Guid ConstValues (ENG-91846 relaxation), and the typed describe reads every written parameter back with its source and value.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process configures perform-task parameter families and reads them back")]
	public async Task ModifyBusinessProcess_Should_ConfigurePerformTaskParameterFamilies() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpPerformTaskE2e{Guid.NewGuid():N}";
		await CreateProcessAsync(context, processName, BuildPerformTaskDescriptor(processName));

		// Act — one edit covering every parameter family the ticket certifies.
		await ModifyExpectingSuccessAsync(context, processName, ConfigurePerformTaskOperations());
		DescribedElement task = await ReadTaskAsync(context, processName);

		// Assert — typed read-back per family.
		DescribedParameter category = task.Parameters.Single(parameter => parameter.Name == "ActivityCategory");
		category.Source.Should().Be("ConstValue",
			because: "a bare record Guid on a Lookup parameter must persist as the ConstValue the runtime's allowed-results derivation reads — the whole point of the ENG-91846 relaxation");
		category.Value.Should().BeEquivalentTo(ToDoActivityCategoryId,
			because: "the record id is stored verbatim, so the runtime resolves exactly the requested category");
		AssertResolvedDisplayName(category.ValueDisplay, category.Value);
		category.ValueDisplay.Should().NotBe(category.Value,
			because: "a display value equal to the id is the defect itself, not a fix — the two must differ");
		DescribedParameter owner = task.Parameters.Single(parameter => parameter.Name == "OwnerId");
		owner.Source.Should().Be("Script",
			because: "the [#SysVariable.CurrentUserContact#] performer route is an expression and stores as a formula source");
		owner.Value.Should().Be("[#SysVariable.CurrentUserContact#]",
			because: "the macro is stored verbatim with zero rewriting");
		task.Parameters.Single(parameter => parameter.Name == "Recommendation").Value.Should()
			.Be("Call the client about the renewal",
				because: "the Recommendation constant is materialized into the process schema resource by SaveSchema and read back as the parameter value");
		DescribedParameter priority = task.Parameters.Single(parameter => parameter.Name == "ActivityPriority");
		priority.Source.Should().Be("ConstValue",
			because: "ActivityPriority rides the same bare-Guid relaxation as the category — the second parameter the route exists for");
		priority.Value.Should().BeEquivalentTo(HighActivityPriorityId,
			because: "the non-default priority proves the write took effect rather than reading back the shipped Medium default");
		task.Parameters.Single(parameter => parameter.Name == "InformationOnStep").Value.Should()
			.Be("Check the last invoice before calling",
				because: "the hint constant is materialized into the process schema resource exactly like Recommendation");
		task.Parameters.Single(parameter => parameter.Name == "Duration").Value.Should().Be("2",
			because: "the scheduling constants persist as plain integer ConstValues");
		task.Parameters.Single(parameter => parameter.Name == "DurationPeriod").Value.Should().Be("2",
			because: "period 2 selects Days in the shared 0=minutes/1=hours/2=days/3=weeks/4=months enum");
		task.Parameters.Single(parameter => parameter.Name == "StartIn").Value.Should().Be("1",
			because: "the third scheduling pair (start delay) persists as written");
		task.Parameters.Single(parameter => parameter.Name == "StartInPeriod").Value.Should().Be("1",
			because: "period 1 selects Hours in the shared period enum");
		task.Parameters.Single(parameter => parameter.Name == "RemindBefore").Value.Should().Be("30",
			because: "the reminder offset persists as written");
		task.Parameters.Single(parameter => parameter.Name == "RemindBeforePeriod").Value.Should().Be("0",
			because: "period 0 selects Minutes in the shared period enum");
		task.Parameters.Single(parameter => parameter.Name == "ShowExecutionPage").Value.Should().Be("true",
			because: "the auto-open flag persists as written");
		task.Parameters.Single(parameter => parameter.Name == "ShowInScheduler").Value.Should().Be("true",
			because: "the calendar flag persists as written (the designer exposes it as the \"Show in calendar\" checkbox inherited from the base user-task properties page; addMapping sets the same parameter)");
	}

	[Test]
	[Description("Over the real MCP path: a Perform task's OUTPUTS resolve as mapping SOURCES — CurrentActivityId (invisible in describe: no default, isResult=false) and ActivityResult both map into Guid PROCESS parameters and read back with the server-built [Element:{uid}] metapath (process parameters are the guidance-recommended target: mapping into a later task's own CurrentActivityId makes it ADOPT that activity, the documented wait-forever trap) — and the equivalent two-task graph shape validates clean.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process maps perform-task outputs into process parameters")]
	public async Task ModifyBusinessProcess_Should_MapPerformTaskOutputsIntoDownstreamElement() {
		// Arrange — two perform tasks in sequence plus two Guid process parameters to receive the outputs.
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpOutputSourceE2e{Guid.NewGuid():N}";
		await CreateProcessAsync(context, processName, BuildTwoTaskDescriptor(processName));

		// Act — map both Task1 outputs into the process parameters, then read the graph back.
		await ModifyExpectingSuccessAsync(context, processName, MapOutputsDownstreamOperations());
		CallToolResult describeResult = await CallToolAsync(context, DescribeToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName
		});
		DescribeProcessResult described = ParseDescribeResult(describeResult);
		DescribedElement task1 = described.Elements.Single(element => element.Name == "Task1");

		// Assert — the sources resolved even though CurrentActivityId is invisible in describe on Task1 itself.
		task1.Parameters.Should().NotContain(parameter => parameter.Name == "CurrentActivityId",
			because: "the unbound output has no default and no isResult, so describe omits it — absence is not non-existence, which is exactly what this test proves");
		DescribedParameter activityId = described.Parameters.Single(parameter => parameter.Name == "FirstTaskActivityId");
		activityId.Source.Should().Be("Script",
			because: "an element-output source is stored as a server-built metapath formula");
		activityId.Value.Should().Contain("[Element:{" + task1.Uid,
			because: "the metapath must reference the SOURCE element's UId — that is what makes the mapping resolvable at run time");
		DescribedParameter resultId = described.Parameters.Single(parameter => parameter.Name == "FirstTaskResultId");
		resultId.Value.Should().Contain("[Element:{" + task1.Uid,
			because: "the IsResult output rides the same server-built [Element:{uid}] metapath");

		// And the same two-task SHAPE (validate-process-graph takes an inline graph, not the saved process)
		// violates no BPMN connection rule.
		CallToolResult validateResult = await CallToolAsync(context, ValidateProcessGraphTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["nodes"] = new[] {
					new ProcessGraphNodeArg("s", "startEvent"), new ProcessGraphNodeArg("t1", "userTask"),
					new ProcessGraphNodeArg("t2", "userTask"), new ProcessGraphNodeArg("e", "endEvent")
				},
				["edges"] = new[] {
					new ProcessGraphEdgeArg("s", "t1", "sequence"), new ProcessGraphEdgeArg("t1", "t2", "sequence"),
					new ProcessGraphEdgeArg("t2", "e", "sequence")
				}
			});
		ValidateProcessGraphResponse validation =
			EntitySchemaStructuredResultParser.Extract<ValidateProcessGraphResponse>(validateResult);
		validation.Success.Should().BeTrue(because: "the graph validation call must succeed on a reachable environment");
		validation.HasErrors.Should().BeFalse(
			because: "start -> task -> task -> end over sequence flows violates no connection rule");
	}

	[Test]
	[Description("Over the real MCP path: a NON-Guid 'value' on a Lookup element parameter is still rejected with the message naming both 'expression' and the [#Lookup…#] macro — the ENG-91846 Guid relaxation must not widen to display names — and the rejected edit is not persisted.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process rejects a non-Guid lookup value and names the expression fallback")]
	public async Task ModifyBusinessProcess_Should_RejectNonGuidLookupValue_AndNameExpressionFallback() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpLookupRejectE2e{Guid.NewGuid():N}";
		await CreateProcessAsync(context, processName, BuildPerformTaskDescriptor(processName));

		// Act — the classic AI mistake: the lookup's display name instead of its record id.
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = """[ { "op": "addMapping", "mapping": { "elementName": "Task1", "elementParameter": "ActivityCategory", "value": "To do" } } ]"""
		});

		// Assert — the instructive message survived the relaxation, and nothing was saved.
		string text = SerializeToolText(callResult);
		text.Should().Contain("expression",
			because: "the message leads with the bare-Guid route and must keep naming the expression fallback");
		text.Should().Contain("[#Lookup",
			because: "the fallback's macro shape must survive rewording, or a caller on the expression path cannot self-correct");
		DescribedElement task = await ReadTaskAsync(context, processName);
		task.Parameters.Should().NotContain(parameter => parameter.Name == "ActivityCategory",
			because: "a rejected mapping aborts the edit, so the parameter stays unbound and invisible in describe");
	}

	[Test]
	[Description("Over the real MCP path: a MALFORMED lookup macro - the [#Lookup...#] wrapper around something that is not a record id - on a Lookup parameter is REJECTED with the same instructive message as any other non-Guid value, not decoded to garbage and not stored. The decode recognises exactly the stored shape; anything else falls through to the existing refusal.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process rejects a malformed fixed-lookup macro on a Lookup parameter")]
	public async Task ModifyBusinessProcess_Should_RejectMalformedLookupMacro_OnLookupParameter() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpBadMacroE2e{Guid.NewGuid():N}";
		await CreateProcessAsync(context, processName, BuildPerformTaskDescriptor(processName));

		// Act - the wrapper is right, the record segment is a word: nothing here is a record id.
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = """[ { "op": "addMapping", "mapping": { "elementName": "Task1", "elementParameter": "ActivityCategory", "value": "[#Lookup.ActivityCategory.To do#]" } } ]"""
		});

		// Assert - refused the way a bare display name is, and nothing was saved.
		string text = SerializeToolText(callResult);
		text.Should().Contain("expression",
			because: "an unrecognised macro shape must fall through to the standard non-Guid refusal, which names the route");
		DescribedElement task = await ReadTaskAsync(context, processName);
		task.Parameters.Should().NotContain(parameter => parameter.Name == "ActivityCategory",
			because: "a rejected mapping aborts the edit, so nothing is stored - a malformed macro must never land as a value");
	}

	[Test]
	[Description("Over the real MCP path: a well-formed lookup macro whose record id resolves to NO record of the parameter's reference object is REJECTED naming that object, exactly as the bare id would be - the decode hands the id to the same existence guard, so the macro form cannot smuggle a dangling reference past it.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process rejects a fixed-lookup macro whose record does not exist")]
	public async Task ModifyBusinessProcess_Should_RejectLookupMacro_WhenRecordDoesNotExist() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpGhostMacroE2e{Guid.NewGuid():N}";
		await CreateProcessAsync(context, processName, BuildPerformTaskDescriptor(processName));

		// Act - shape-perfect, but no ActivityCategory row carries this id on any stand.
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = $$"""[ { "op": "addMapping", "mapping": { "elementName": "Task1", "elementParameter": "ActivityCategory", "value": "[#Lookup.{{ActivityCategoryObjectUId}}.11111111-2222-3333-4444-555555555555#]" } } ]"""
		});

		// Assert - the existence guard answered, naming the reference object, and nothing was saved.
		string text = SerializeToolText(callResult);
		text.Should().Contain("no ActivityCategory record has this id",
			because: "a decoded macro is checked against the reference object like a bare id; a dangling id must be refused naming that object");
		DescribedElement task = await ReadTaskAsync(context, processName);
		task.Parameters.Should().NotContain(parameter => parameter.Name == "ActivityCategory",
			because: "a rejected mapping aborts the edit, so a reference to nothing is never stored");
	}

	[Test]
	[Description("Over the real MCP path: an already-composed [#Lookup.{objectUId}.{recordId}#] passed as a Lookup 'value' is decoded to the bare record id and stored as a ConstValue with the record's NAME as its display value — the round trip clio, its tests and the guide rely on, proven by feeding describe's own output straight back and reading an identical result.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process decodes a fixed-lookup macro value and round-trips describe's output")]
	public async Task ModifyBusinessProcess_Should_DecodeLookupMacroValue_AndRoundTripDescribeOutput() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpLookupMacroE2e{Guid.NewGuid():N}";
		await CreateProcessAsync(context, processName, BuildPerformTaskDescriptor(processName));

		// Act — the macro form a caller may legitimately hold, rather than the bare id.
		await ModifyExpectingSuccessAsync(context, processName, $$"""
			[ { "op": "addMapping", "mapping": { "elementName": "Task1", "elementParameter": "ActivityCategory",
			    "value": "[#Lookup.{{ActivityCategoryObjectUId}}.{{ToDoActivityCategoryId}}#]" } } ]
			""");
		DescribedParameter category = (await ReadTaskAsync(context, processName)).Parameters
			.Single(parameter => parameter.Name == "ActivityCategory");

		// Assert — decoded to the bare id in the encoding the allowed-results derivation reads, and named.
		category.Source.Should().Be("ConstValue",
			because: "a decoded macro must land in the SAME encoding a bare id does — a Script macro here silently "
				+ "degrades the Perform task's result list to the default");
		category.Value.Should().BeEquivalentTo(ToDoActivityCategoryId,
			because: "describe must return a bare Guid whatever form the caller wrote, or the round trip breaks");
		AssertResolvedDisplayName(category.ValueDisplay, category.Value);

		// And feeding describe's OWN output back is a no-op — the round trip the contract advertises.
		await ModifyExpectingSuccessAsync(context, processName, $$"""
			[ { "op": "addMapping", "mapping": { "elementName": "Task1", "elementParameter": "ActivityCategory",
			    "value": "{{category.Value}}" } } ]
			""");
		DescribedParameter reapplied = (await ReadTaskAsync(context, processName)).Parameters
			.Single(parameter => parameter.Name == "ActivityCategory");
		reapplied.Value.Should().BeEquivalentTo(category.Value,
			because: "re-submitting a described value must reproduce it exactly — that is what round-tripping means");
		reapplied.ValueDisplay.Should().Be(category.ValueDisplay,
			because: "the display name is re-derived on every write, so a re-apply must not lose or change it");
	}

	[Test]
	[Description("Over the real MCP path: Guid.Empty as a Lookup 'value' is refused as referencing no record — the second refusal the tool description documents, and the newer of the two, so the bundled package regressing it must fail this suite — and the rejected edit is not persisted.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process rejects Guid.Empty as a lookup value")]
	public async Task ModifyBusinessProcess_Should_RejectGuidEmptyLookupValue_AsReferencingNoRecord() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpEmptyGuidE2e{Guid.NewGuid():N}";
		await CreateProcessAsync(context, processName, BuildPerformTaskDescriptor(processName));

		// Act — the placeholder an AI emits when it has no record id
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = """[ { "op": "addMapping", "mapping": { "elementName": "Task1", "elementParameter": "ActivityCategory", "value": "00000000-0000-0000-0000-000000000000" } } ]"""
		});

		// Assert — its own refusal (not the non-Guid macro message), and nothing saved.
		string text = SerializeToolText(callResult);
		text.Should().Contain("empty Guid",
			because: "the refusal must name the actual defect — a parseable Guid that references no record");
		text.Should().Contain("references no record",
			because: "the message must say WHY the placeholder is refused, or the caller retries the same value");
		DescribedElement task = await ReadTaskAsync(context, processName);
		task.Parameters.Should().NotContain(parameter => parameter.Name == "ActivityCategory",
			because: "a rejected mapping aborts the edit, so the parameter stays unbound and invisible in describe");
	}

	[Test]
	[Description("Over the real MCP path: a 'performer' of type role on a Perform task resolves the role BY NAME, and the typed describe reads the block back top-level (type, the stored role macro, the display name, the designer-parity showPage=false); switching the performer to 'user' in a second edit replaces the choice in place.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process assigns a role performer and reads it back")]
	public async Task ModifyBusinessProcess_Should_AssignRolePerformer_AndReadItBack() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpPerformerE2e{Guid.NewGuid():N}";
		await CreateProcessAsync(context, processName, BuildPerformTaskDescriptor(processName));

		// Act — assign to a role by NAME ("All employees" is base-seed data present on every stand)
		await ModifyExpectingSuccessAsync(context, processName,
			@"[ { ""op"": ""setElement"", ""elementName"": ""Task1"", ""elementUpdate"": { ""performer"": { ""type"": ""role"", ""role"": ""All employees"" } } } ]");
		DescribedElement roleTask = await ReadTaskAsync(context, processName);

		// Assert — the claim model read back through the typed top-level block
		roleTask.Performer.Should().NotBeNull(
			because: "a Perform task's performer assignment must read back top-level, or the block is write-only");
		roleTask.Performer!.Type.Should().Be("role",
			because: "the Role assignment reads back as its contract token");
		roleTask.Performer.Role.Should().Contain("[#Lookup.",
			because: "the stored role macro is the re-appliable value the same block accepts back");
		roleTask.Performer.RoleDisplay.Should().Be("All employees",
			because: "a name-resolved role carries the human-readable name as its display value");
		roleTask.Performer.ShowPage.Should().BeFalse(
			because: "an omitted showPage defaults to false for a role performer — designer parity, because a "
			+ "role activity has an EMPTY owner and an auto-opened page would target nobody");

		// Act — switch the performer to a specific user; the choice replaces in place
		await ModifyExpectingSuccessAsync(context, processName,
			@"[ { ""op"": ""setElement"", ""elementName"": ""Task1"", ""elementUpdate"": { ""performer"": { ""type"": ""user"", ""contact"": ""[#SysVariable.CurrentUserContact#]"" } } } ]");
		DescribedElement userTask = await ReadTaskAsync(context, processName);

		// Assert
		userTask.Performer!.Type.Should().Be("user",
			because: "re-applying the performer replaces the previous choice in place, setElement semantics");
		userTask.Performer.Contact.Should().Be("[#SysVariable.CurrentUserContact#]",
			because: "the contact formula is stored verbatim and read back re-appliably");
	}

	[Test]
	[Description("Over the real MCP path: a 'performer' of type MANAGER configured from a BARE Contact Guid — the two newly documented contract shapes in one write. The bare id is existence-checked and stored as the composed [#Lookup…#] macro (the designer's own encoding, so describe hands back a re-appliable value), and the manager kind reads back as its token with the designer-parity showPage=false. The managerless RUNTIME error ('process error when the contact's employee record has no manager') is an ACCEPTED coverage gap: it surfaces only when the process runs, and this suite verifies design-time contracts.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process assigns a manager performer from a bare contact Guid")]
	public async Task ModifyBusinessProcess_Should_AssignManagerPerformer_FromBareContactGuid() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpManagerE2e{Guid.NewGuid():N}";
		await CreateProcessAsync(context, processName, BuildPerformTaskDescriptor(processName));

		// Act — a bare base-seed contact id, not a formula
		await ModifyExpectingSuccessAsync(context, processName,
			$$"""[ { "op": "setElement", "elementName": "Task1", "elementUpdate": { "performer": { "type": "manager", "contact": "{{SupervisorContactId}}" } } } ]""");
		DescribedElement task = await ReadTaskAsync(context, processName);

		// Assert
		task.Performer.Should().NotBeNull(
			because: "the manager assignment must read back top-level exactly like the role one");
		task.Performer!.Type.Should().Be("manager",
			because: "the Manager kind reads back as its contract token");
		task.Performer.Contact.Should().StartWith("[#Lookup.",
			because: "a bare EXISTING contact id is stored as the composed lookup macro — the designer's own "
			+ "encoding — instead of reaching the platform as a formula its pre-save validator refuses");
		task.Performer.Contact.Should().Contain(SupervisorContactId,
			because: "the macro must still carry the record the caller named");
		task.Performer.ShowPage.Should().BeFalse(
			because: "an omitted showPage defaults to false for a manager performer — designer parity: the "
			+ "manager is resolved only at run time, so at design time there is nobody to open the page for");
	}

	[Test]
	[Description("Over the real MCP path, at CREATE time: an element-level 'performer' inline in the create descriptor — the create tool's own deserialization path, distinct from modify's — lands on the created process and reads back with the bare contact Guid composed into the stored macro.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process takes an inline performer with a bare contact Guid")]
	public async Task CreateBusinessProcess_Should_TakeInlinePerformer_WithBareContactGuid() {
		// Arrange & Act — the performer travels INSIDE the create descriptor
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpCreatePerformerE2e{Guid.NewGuid():N}";
		await CreateProcessAsync(context, processName,
			$$"""
			{
			  "name": "{{processName}}",
			  "caption": "Clio BP Create Performer E2E",
			  "packageName": "Custom",
			  "elements": [
			    { "name": "StartEvent1", "type": "startEvent" },
			    { "name": "Task1", "type": "performTask", "caption": "Perform task",
			      "performer": { "type": "user", "contact": "{{SupervisorContactId}}" } },
			    { "name": "EndEvent1", "type": "endEvent" }
			  ],
			  "flows": [
			    { "source": "StartEvent1", "target": "Task1" },
			    { "source": "Task1", "target": "EndEvent1" }
			  ]
			}
			""");
		DescribedElement task = await ReadTaskAsync(context, processName);

		// Assert
		task.Performer.Should().NotBeNull(
			because: "an inline performer in the CREATE descriptor must land like modify's — create has its own "
			+ "deserialization path, and a dropped member there would fail silently");
		task.Performer!.Type.Should().Be("user",
			because: "the User kind reads back as its contract token");
		task.Performer.Contact.Should().StartWith("[#Lookup.",
			because: "the bare contact id is composed into the stored macro on the create path too");
		task.Performer.Contact.Should().Contain(SupervisorContactId,
			because: "the macro must carry the record the descriptor named");
	}

	[Test]
	[Description("Over the real MCP path: a syntactically valid but NON-EXISTENT role Guid in performer.role is REFUSED — the id route is existence-checked exactly like the name route, because an arbitrary Guid would otherwise be written into the Activity's OwnerRole (a column that does not control integrity) and read back through describe as a normal team assignment nobody can see.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process rejects a non-existent role Guid in performer.role")]
	public async Task ModifyBusinessProcess_Should_RejectPerformerRole_WhenRoleGuidDoesNotExist() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpGhostRoleE2e{Guid.NewGuid():N}";
		await CreateProcessAsync(context, processName, BuildPerformTaskDescriptor(processName));
		string ghostRole = Guid.NewGuid().ToString();

		// Act — a well-formed Guid that belongs to no role on any environment
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = $$"""[ { "op": "setElement", "elementName": "Task1", "elementUpdate": { "performer": { "type": "role", "role": "{{ghostRole}}" } } } ]"""
		});

		// Assert — refused naming the value, and nothing was persisted
		string text = SerializeToolText(callResult);
		text.Should().Contain(ghostRole,
			because: "the refusal must name the value it rejected, or the caller cannot tell which of several "
			+ "ids in a batch was wrong");
		text.Should().Contain("role",
			because: "the refusal must say the id matched no ROLE, not merely that something was invalid");
		DescribedElement task = await ReadTaskAsync(context, processName);
		task.Performer.Should().BeNull(
			because: "a rejected performer aborts the whole edit, so the element keeps no assignment — the "
			+ "failure this guard exists to prevent is precisely a stored one that LOOKS valid in describe");
	}

	[Test]
	[Description("Over the real MCP path: a SysAdminUnit role id pasted into the Contact-typed OwnerId is REFUSED naming the reference object — the shape check alone cannot tell a Contact id from a role id, and before this guard the value persisted as a well-formed ConstValue that referenced nothing at run time.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process rejects a role id on OwnerId as the wrong entity")]
	public async Task ModifyBusinessProcess_Should_RejectRoleGuidOnOwnerId_AsWrongEntity() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpWrongEntityE2e{Guid.NewGuid():N}";
		await CreateProcessAsync(context, processName, BuildPerformTaskDescriptor(processName));

		// Act — the "assign to a team" mistake: the base-seed "All employees" ROLE id into OwnerId
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = $$"""[ { "op": "addMapping", "mapping": { "elementName": "Task1", "elementParameter": "OwnerId", "value": "{{AllEmployeesRoleId}}" } } ]"""
		});

		// Assert — refused naming the reference object, and nothing was saved.
		string text = SerializeToolText(callResult);
		text.Should().Contain("no Contact record has this id",
			because: "the refusal must name the reference object, or the caller retries other role ids forever");
		DescribedElement task = await ReadTaskAsync(context, processName);
		task.Parameters.Should().NotContain(parameter => parameter.Name == "OwnerId",
			because: "a rejected mapping aborts the edit, so the fake assignment is not persisted");
	}

	[Test]
	[Description("Over the real MCP path: a 'performer' on the retired CallUserTask is REFUSED by name with the Perform task route in the message — the Call element's runtime ignores the performer-assignment options, so accepting the block would assign nobody silently — and the aborted edit leaves no element behind.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process refuses a performer on the retired Call element")]
	public async Task ModifyBusinessProcess_Should_RefusePerformerOnRetiredCallTask() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpCallPerformerE2e{Guid.NewGuid():N}";
		await CreateProcessAsync(context, processName, BuildPerformTaskDescriptor(processName));

		// Act — add the retired Call element WITH a performer in one operation
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = @"[ { ""op"": ""addElement"", ""element"": { ""name"": ""Call1"", ""type"": ""userTask"", ""userTaskName"": ""CallUserTask"", ""performer"": { ""type"": ""user"" } } } ]"
		});

		// Assert — refused naming the retirement and the working route, and the whole edit aborted.
		string text = SerializeToolText(callResult);
		text.Should().Contain("retired",
			because: "the refusal must say WHY the element cannot take a performer — its runtime ignores the options");
		text.Should().Contain("ActivityUserTask",
			because: "the refusal must route the caller to the Perform task element instead of dead-ending");
		DescribedElement callFree = await ReadTaskAsync(context, processName);
		callFree.Name.Should().Be("Task1",
			because: "any failed operation aborts the whole edit, so the half-configured Call element is not saved "
			+ "(the perform task remains the only task element)");
	}

	[Test]
	[Description("Over the real MCP path: mapping a type-incompatible source (an Integer process parameter) onto the Perform task's Lookup->Contact performer parameter is rejected with the incompatible-types diagnosis and the edit is not persisted.")]
	[AllureTag(ToolName)]
	[AllureName("modify-business-process rejects a type-incompatible mapping onto the performer lookup")]
	public async Task ModifyBusinessProcess_Should_RejectTypeIncompatibleMapping_OntoPerformerLookup() {
		// Arrange — the descriptor carries an Integer parameter to misuse as the source.
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		string processName = $"UsrClioBpIncompatMapE2e{Guid.NewGuid():N}";
		await CreateProcessAsync(context, processName, BuildPerformTaskWithIntegerParameterDescriptor(processName));

		// Act
		CallToolResult callResult = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = """[ { "op": "addMapping", "mapping": { "elementName": "Task1", "elementParameter": "OwnerId", "processParameter": "Attempts" } } ]"""
		});

		// Assert
		SerializeToolText(callResult).Should().Contain("incompatible data value types",
			because: "the type-compatibility gate is what stops a value the runtime could never assign to the performer");
		DescribedElement task = await ReadTaskAsync(context, processName);
		task.Parameters.Should().NotContain(parameter => parameter.Name == "OwnerId",
			because: "the rejected mapping was discarded, so the performer parameter stays unbound");
	}

	/// <summary>
	/// "All employees" — the base-seed SysAdminUnit role with the same UId on every stand. Used both as a
	/// REAL role (the performer tests resolve it by name, and this id is what the stored macro carries) and
	/// as the canonical WRONG-ENTITY id for the Contact-typed <c>OwnerId</c> in the rejection test.
	/// </summary>
	private const string AllEmployeesRoleId = "a29a3ba5-4b0d-de11-9a51-005056c00008";

	/// <summary>
	/// The Supervisor CONTACT — base-seed, same UId on every stand. Measured on the target stand: the
	/// Supervisor <c>SysAdminUnit</c> is a DIFFERENT id (7f3b869f-…), so the pair also proves the
	/// reference-existence guard distinguishes the two tables rather than matching a shared Guid. The
	/// bare-Guid performer tests need an id that EXISTS, because the guard refuses an invented one by design.
	/// </summary>
	private const string SupervisorContactId = "410006e1-ca4e-4502-a9ec-e54d922d2c00";

	/// <summary>
	/// "To do" — a base-seed ActivityCategory row with the same UId on every stand: the platform runtime
	/// itself hardcodes this Guid as the category fallback in <c>ActivityUserTask</c>, which is what makes
	/// asserting the literal safe against any environment this suite runs on.
	/// </summary>
	private const string ToDoActivityCategoryId = "f51c4643-58e6-df11-971b-001d60e938c6";

	/// <summary>
	/// The en-US display name of <see cref="ToDoActivityCategoryId"/> — what the designer shows in place of the id
	/// for an English-culture caller. The row is base seed on every stand this suite runs on.
	/// <para>A PROFILE-CULTURE value, and debug it as one: the server reads the name through the platform's entity
	/// read (<c>Entity.FetchFromDB</c> → <c>EntitySchemaQuery</c>), which localizes to the CALLER's culture with a
	/// fallback to the stand's primary language. So this literal holds only while the MCP user's profile culture
	/// is en-US; under ru-RU the same call yields "Выполнить". The assertions therefore check the SHAPE of the
	/// value (present, and not a Guid) rather than this literal — see
	/// <see cref="AssertResolvedDisplayName"/>. The literal is kept for the writes that need the name.</para>
	/// </summary>
	private const string ToDoActivityCategoryName = "To do";

	/// <summary>
	/// The culture-independent half of the display-name contract: a resolved name is PRESENT and is NOT the record
	/// id. The exact word depends on the MCP user's profile culture on the stand (the server localizes it), so
	/// asserting <see cref="ToDoActivityCategoryName"/> literally would make the suite fail on any non-English
	/// profile while proving nothing more than this does: the defect was the Guid, and the Guid is what is ruled out.
	/// </summary>
	private static void AssertResolvedDisplayName(string valueDisplay, string rawValue) {
		valueDisplay.Should().NotBeNullOrWhiteSpace(
			because: "the server resolves the referenced record's name into the display value; an absent one means the "
				+ "stand could not name the record, which this suite's base-seed category must never be");
		Guid.TryParse(valueDisplay, out _).Should().BeFalse(
			because: "the display value must be a NAME - the raw record id in that slot is exactly the designer defect "
				+ "ENG-96325 removed");
		valueDisplay.Should().NotBeEquivalentTo(rawValue,
			because: "the display value must differ from the stored value it labels - equality would mean the id was "
				+ "echoed back in a shape the Guid check did not recognise");
		valueDisplay.Should().NotContainEquivalentOf("error",
			because: "a message leaking through the name read would be non-empty and non-Guid and still be wrong");
		valueDisplay.Should().NotContain("[#",
			because: "on an element parameter the display value is the record's plain name, never a macro - a macro "
				+ "here means the change-data rendering path was applied to the wrong slot");
	}

	/// <summary>
	/// The ActivityCategory OBJECT's schema UId — the first segment of a fixed-lookup macro naming a category
	/// record. Only the macro-round-trip test needs it: everywhere else the parameter's own reference object
	/// supplies the typing.
	/// </summary>
	private const string ActivityCategoryObjectUId = "961e2086-a12b-4d27-b095-40b1e64d6cc0";

	/// <summary>
	/// "High" — a base-seed ActivityPriority row, same-UId-everywhere for the same reason as the category
	/// above (the shipped <c>ActivityUserTask</c> metadata hardcodes its sibling "Medium"
	/// ab96fa02-7fe6-df11-971b-001d60e938c6 as the default). High is chosen BECAUSE it is not the default,
	/// so the read-back discriminates an applied write from the shipped default.
	/// </summary>
	private const string HighActivityPriorityId = "d625a9fc-7ee6-df11-971b-001d60e938c6";

	private static string ConfigurePerformTaskOperations() =>
		$$"""
		[
		  { "op": "addMapping", "mapping": { "elementName": "Task1", "elementParameter": "Recommendation", "value": "Call the client about the renewal" } },
		  { "op": "addMapping", "mapping": { "elementName": "Task1", "elementParameter": "OwnerId", "expression": "[#SysVariable.CurrentUserContact#]" } },
		  { "op": "addMapping", "mapping": { "elementName": "Task1", "elementParameter": "InformationOnStep", "value": "Check the last invoice before calling" } },
		  { "op": "addMapping", "mapping": { "elementName": "Task1", "elementParameter": "Duration", "value": "2" } },
		  { "op": "addMapping", "mapping": { "elementName": "Task1", "elementParameter": "DurationPeriod", "value": "2" } },
		  { "op": "addMapping", "mapping": { "elementName": "Task1", "elementParameter": "StartIn", "value": "1" } },
		  { "op": "addMapping", "mapping": { "elementName": "Task1", "elementParameter": "StartInPeriod", "value": "1" } },
		  { "op": "addMapping", "mapping": { "elementName": "Task1", "elementParameter": "RemindBefore", "value": "30" } },
		  { "op": "addMapping", "mapping": { "elementName": "Task1", "elementParameter": "RemindBeforePeriod", "value": "0" } },
		  { "op": "addMapping", "mapping": { "elementName": "Task1", "elementParameter": "ShowExecutionPage", "value": "true" } },
		  { "op": "addMapping", "mapping": { "elementName": "Task1", "elementParameter": "ShowInScheduler", "value": "true" } },
		  { "op": "addMapping", "mapping": { "elementName": "Task1", "elementParameter": "ActivityCategory", "value": "{{ToDoActivityCategoryId}}" } },
		  { "op": "addMapping", "mapping": { "elementName": "Task1", "elementParameter": "ActivityPriority", "value": "{{HighActivityPriorityId}}" } }
		]
		""";

	private static string MapOutputsDownstreamOperations() =>
		"""
		[
		  { "op": "addMapping", "mapping": { "targetProcessParameter": "FirstTaskActivityId", "sourceElement": "Task1", "sourceElementParameter": "CurrentActivityId" } },
		  { "op": "addMapping", "mapping": { "targetProcessParameter": "FirstTaskResultId", "sourceElement": "Task1", "sourceElementParameter": "ActivityResult" } }
		]
		""";

	private static string BuildTwoTaskDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Output Source E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "Task1", "type": "performTask", "caption": "First task" },
		    { "name": "Task2", "type": "performTask", "caption": "Second task" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "Task1" },
		    { "source": "Task1", "target": "Task2" },
		    { "source": "Task2", "target": "EndEvent1" }
		  ],
		  "parameters": [
		    { "name": "FirstTaskActivityId", "type": "Guid", "direction": "Variable" },
		    { "name": "FirstTaskResultId", "type": "Guid", "direction": "Variable" }
		  ]
		}
		""";

	private static string BuildPerformTaskWithIntegerParameterDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Incompatible Mapping E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "Task1", "type": "performTask", "caption": "Perform task" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "Task1" },
		    { "source": "Task1", "target": "EndEvent1" }
		  ],
		  "parameters": [
		    { "name": "Attempts", "type": "Integer", "direction": "Variable" }
		  ]
		}
		""";

	/// <summary>
	/// Creates the process and asserts the create itself succeeded. An unchecked create turns every later
	/// assertion into a statement about a process that does not exist.
	/// </summary>
	private static async Task CreateProcessAsync(ArrangeContext context, string processName, string descriptor) {
		CallToolResult result = await CallToolAsync(context, CreateToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["descriptor"] = descriptor
		});
		result.IsError.Should().NotBeTrue(
			because: $"the arrange must actually create '{processName}', or the test measures nothing");
	}

	/// <summary>Applies operations and asserts the edit succeeded, returning the result so a caller can read its notices.</summary>
	private static async Task<CallToolResult> ModifyExpectingSuccessAsync(ArrangeContext context,
			string processName, string operations) {
		CallToolResult result = await CallToolAsync(context, ToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName,
			["operations"] = operations
		});
		result.IsError.Should().NotBeTrue(because: "the platform must accept the shape this package writes");
		SerializeToolText(result).Should().Contain("edited (",
			because: "the success line is what distinguishes an applied edit from a refusal the test would otherwise read as success");
		return result;
	}

	/// <summary>Reads the perform task itself through the TYPED describe model.</summary>
	private static async Task<DescribedElement> ReadTaskAsync(ArrangeContext context, string processName) {
		CallToolResult callResult = await CallToolAsync(context, DescribeToolName, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = processName
		});
		DescribeProcessResult described = ParseDescribeResult(callResult);
		return described.Elements.Single(element => element.Name == "Task1");
	}

	/// <summary>Reads the perform task's connections through the TYPED describe model.</summary>
	private static async Task<IReadOnlyList<DescribedConnection>> ReadConnectionsAsync(ArrangeContext context,
			string processName) {
		return (await ReadTaskAsync(context, processName)).Connections ?? [];
	}

	private static async Task<DescribedConnection> ReadConnectionAsync(ArrangeContext context, string processName,
			string column) {
		IReadOnlyList<DescribedConnection> connections = await ReadConnectionsAsync(context, processName);
		return connections.Single(connection => connection.Column == column);
	}

	/// <summary>
	/// Concatenates the tool result's text blocks. Serializing the whole result escapes the payload, so a literal
	/// with quotes in it can never match — which is how three of these assertions were unfalsifiable at first.
	/// </summary>
	private static string SerializeToolText(CallToolResult callResult) =>
		string.Join("\n", callResult.Content.OfType<TextContentBlock>().Select(block => block.Text));

	/// <summary>
	/// A process whose single task is a PERFORM TASK — the one connection-capable user task with no
	/// CreateActivity gate, so a connections failure here can never be the effectiveness rule — plus a Guid
	/// process parameter to bind from.
	/// </summary>
	private static string BuildPerformTaskDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Connections E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "Task1", "type": "performTask", "caption": "Perform task" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "Task1" },
		    { "source": "Task1", "target": "EndEvent1" }
		  ],
		  "parameters": [
		    { "name": "AccountRef", "type": "Guid", "direction": "Variable" }
		  ]
		}
		""";

	private static string SetAccountConnectionFromProcessParameterOperations() =>
		"""
		[ { "op": "setConnections", "elementName": "Task1", "connections": [ { "column": "Account", "processParameter": "AccountRef" } ] } ]
		""";

	private static string SetConnectionFromRecordIdOperations(string column, Guid recordId) =>
		$$"""
		[ { "op": "setConnections", "elementName": "Task1", "connections": [ { "column": "{{column}}", "recordId": "{{recordId}}" } ] } ]
		""";

	private static string ClearConnectionOperations(string column) =>
		$$"""
		[ { "op": "clearConnections", "elementName": "Task1", "connections": [ { "column": "{{column}}" } ] } ]
		""";

	private static string SetAccountConnectionFromDateConstantOperations() =>
		"""
		[ { "op": "setConnections", "elementName": "Task1", "connections": [ { "column": "Account", "expression": "[#DateValue.2026-01-01#]" } ] } ]
		""";

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
