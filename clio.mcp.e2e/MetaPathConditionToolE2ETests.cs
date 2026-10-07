using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for ENG-102114 over the real MCP path: a condition written on an EXISTING process takes the
/// <c>[#Element.Parameter.Column#]</c> name a build-path condition takes, and a meta path written by hand is
/// accepted only in the spelling the platform writes, its prefix optional, naming a column the record delivers - in a
/// condition and in a mapping expression - and stored with its GUIDs lower-cased. NOT in CI — run manually against an
/// environment carrying CrtProcessBuilder 1.6.6.77 or later (1.6.6.83 for the GUID-case test).
/// <para>The motivating defect (clio#1529): a hand-assembled token missing the dot before
/// <c>[EntityColumn:…]</c>. On a Script value the platform refuses it at save with "Value for argument
/// "parameterUId" must be specified", which names neither the flow nor the token; these tests pin that the package
/// refuses it first, naming the flow and handing back the canonical token, and that the modify path no longer
/// needs the hand-assembled token at all.</para>
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(ModifyBusinessProcessTool.ModifyBusinessProcessToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class MetaPathConditionToolE2ETests {

	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;
	private const string ModifyToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;

	/// <summary>The first cut that expands names on the modify path and checks a hand-written meta path.</summary>
	private const string MinimumPackageVersion = "1.6.6.77";

	/// <summary>The first cut that stores an accepted meta path with its GUIDs lower-cased.</summary>
	private const string GuidCaseMinimumPackageVersion = "1.6.6.83";

	/// <summary>The prefix the platform's GetMetaPath writes before every reference.</summary>
	private const string Prefix = "[IsOwnerSchema:false].[IsSchema:false].";

	#region Methods: Tests

	[Test]
	[Description("setFlowCondition on an existing process takes [#ReadContact.ResultEntity.DoNotUseCall#] and describe reads the condition back as the platform's three-segment meta path - the caller no longer assembles the UId token by hand.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process setFlowCondition expands a record-column name")]
	public async Task ModifyBusinessProcess_Should_ExpandANamedCondition() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("Meta-path condition", MinimumPackageVersion);
		string processName = $"UsrClioBpMetaPathE2e{Guid.NewGuid():N}";
		await CreateAsync(context, processName, condition: null);

		// Act
		CallToolResult modified = await ProcessDesignerE2EArrange.CallToolAsync(context, ModifyToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = SetCondition("[#ReadContact.ResultEntity.DoNotUseCall#] == false")
			});

		// Assert
		McpCommandExecutionParser.Extract(modified).ExitCode.Should().Be(0,
			because: "a name the process can answer for is expanded, so the platform's gate accepts the condition");
		string condition = ConditionOnCall(
			DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName)));
		condition.Should().Contain("].[EntityColumn:{",
			because: "the name is stored as the platform's three-segment meta path");
		condition.Should().NotContain("ReadContact.ResultEntity",
			because: "an unexpanded name would never be evaluated at run time");
	}

	[Test]
	[Description("A hand-written meta path missing the dot before [EntityColumn:] - clio#1529's typo - is refused by the package naming the flow and handing back the canonical token, instead of the platform's 'parameterUId must be specified'; the stored condition is left as it was.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process refuses a misspelled meta path in a condition")]
	public async Task ModifyBusinessProcess_Should_RefuseAMisspelledMetaPath() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("Meta-path condition", MinimumPackageVersion);
		string processName = $"UsrClioBpMetaPathBadE2e{Guid.NewGuid():N}";
		await CreateAsync(context, processName, "[#ReadContact.ResultEntity.DoNotUseCall#] == false");
		string canonical = ConditionOnCall(
			DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName)));
		string missingDot = canonical.Replace("].[EntityColumn:", "][EntityColumn:");
		missingDot.Should().NotBe(canonical, because: "the arrange must actually have removed the dot");

		// Act
		CallToolResult refused = await ProcessDesignerE2EArrange.CallToolAsync(context, ModifyToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = SetCondition(missingDot)
			});

		// Assert
		// The decoded log messages, not the serialized result: serialization escapes every apostrophe, so a
		// phrase that quotes a name would never be found in it.
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(refused);
		string refusal = string.Join(" ", (execution.Output ?? []).Select(message => message.Value));
		execution.ExitCode.Should().NotBe(0, because: "the misspelled condition must abort the edit");
		refusal.Should().Contain("is not spelled exactly",
			because: "the package refuses the spelling before the platform's gate reports it cryptically");
		refusal.Should().Contain("from 'ReadContact' to 'Call'",
			because: "the refusal names the flow, which the platform's message does not");
		refusal.Should().Contain($"Send '{TokenOf(canonical)}'",
			because: "the refusal hands back the token describe reports, spelled as the platform writes it");
		refusal.Should().NotContain("parameterUId",
			because: "the platform's unattributed message must not be what the caller reads");
		ConditionOnCall(DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName)))
			.Should().Be(canonical, because: "a refused edit writes nothing");
	}

	[Test]
	[Description("The prefix-less meta path - the spelling the published guidance taught, which processes built through clio store - is accepted and stored exactly as written, so a describe-then-modify round trip of such a process is never refused.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process accepts the prefix-less meta path in a condition")]
	public async Task ModifyBusinessProcess_Should_AcceptThePrefixLessMetaPath() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("Meta-path condition", MinimumPackageVersion);
		string processName = $"UsrClioBpMetaPathShortE2e{Guid.NewGuid():N}";
		await CreateAsync(context, processName, "[#ReadContact.ResultEntity.DoNotUseCall#] == false");
		string canonical = ConditionOnCall(
			DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName)));
		string prefixLess = canonical.Replace(Prefix, string.Empty);
		prefixLess.Should().NotBe(canonical, because: "the arrange must actually have dropped the prefix");

		// Act
		CallToolResult modified = await ProcessDesignerE2EArrange.CallToolAsync(context, ModifyToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = SetCondition(prefixLess)
			});

		// Assert
		McpCommandExecutionParser.Extract(modified).ExitCode.Should().Be(0,
			because: "the prefix-less spelling is one of the two the check accepts");
		ConditionOnCall(DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName)))
			.Should().Be(prefixLess, because: "an accepted spelling is never rewritten to the other one");
	}

	[Test]
	[Description("QA, 2026-10-07: a meta path whose GUIDs are upper case passed the check and saved green, but the run time and the designer match only lower case, so the branch read an empty value and the designer showed the raw token. The package now stores it with its GUIDs lower-cased - exactly the token the platform writes.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process stores an upper-case-GUID meta path lower-cased")]
	public async Task ModifyBusinessProcess_Should_StoreAnUpperCaseGuidMetaPathLowerCased() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("Meta-path condition", GuidCaseMinimumPackageVersion);
		string processName = $"UsrClioBpMetaPathCaseE2e{Guid.NewGuid():N}";
		await CreateAsync(context, processName, "[#ReadContact.ResultEntity.DoNotUseCall#] == false");
		string canonical = ConditionOnCall(
			DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName)));
		await ModifyExpectingSuccessAsync(context, processName, SetCondition("true"));
		string upper = Regex.Replace(canonical, @"\{[0-9a-f-]{36}\}", match => match.Value.ToUpperInvariant(),
			RegexOptions.None, TimeSpan.FromSeconds(1));
		upper.Should().NotBe(canonical, because: "the arrange must actually have upper-cased the GUIDs");

		// Act
		CallToolResult modified = await ProcessDesignerE2EArrange.CallToolAsync(context, ModifyToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = SetCondition(upper)
			});

		// Assert
		McpCommandExecutionParser.Extract(modified).ExitCode.Should().Be(0,
			because: "a GUID's case carries no meaning, so the reference is accepted rather than refused");
		ConditionOnCall(DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName)))
			.Should().Be(canonical, because: "the run time and the designer match lower-case GUIDs only");
	}

	[Test]
	[Description("A correctly spelled reference to a column the Read data element does not load is refused, naming the column - the platform would save it green and the branch would read an empty value at run time.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process refuses a condition on a column the read does not load")]
	public async Task ModifyBusinessProcess_Should_RefuseAColumnTheReadDoesNotLoad() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("Meta-path condition", MinimumPackageVersion);
		string processName = $"UsrClioBpMetaPathUnreadE2e{Guid.NewGuid():N}";
		await CreateAsync(context, processName, "[#ReadContact.ResultEntity.DoNotUseCall#] == false");
		string canonical = ConditionOnCall(
			DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName)));
		await ModifyExpectingSuccessAsync(context, processName, SetCondition("true"));
		await ModifyExpectingSuccessAsync(context, processName, JsonSerializer.Serialize(new[] {
			new Dictionary<string, object> {
				["op"] = "setElement", ["elementName"] = "ReadContact",
				["elementUpdate"] = new Dictionary<string, object> {
					["readData"] = new Dictionary<string, object> { ["columns"] = new[] { "Id" } }
				}
			}
		}));

		// Act
		CallToolResult refused = await ProcessDesignerE2EArrange.CallToolAsync(context, ModifyToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = SetCondition(canonical)
			});

		// Assert
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(refused);
		execution.ExitCode.Should().NotBe(0, because: "the column is no longer read, so it would arrive empty");
		string.Join(" ", (execution.Output ?? []).Select(message => message.Value)).Should()
			.Contain("column 'DoNotUseCall' is not among the columns",
				because: "the refusal names the column and why the reference cannot work");
	}

	[Test]
	[Description("A mapping 'expression' is checked like a condition: a hand-written meta path missing the dot before [EntityColumn:] is refused naming the mapping and handing back the canonical token - and, because a mapping takes no names, no name is offered.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process refuses a misspelled meta path in a mapping expression")]
	public async Task ModifyBusinessProcess_Should_RefuseAMisspelledMetaPathInAMapping() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("Meta-path condition", MinimumPackageVersion);
		string processName = $"UsrClioBpMetaPathMapE2e{Guid.NewGuid():N}";
		await CreateAsync(context, processName, "[#ReadContact.ResultEntity.DoNotUseCall#] == false");
		string token = TokenOf(ConditionOnCall(
			DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName))));
		string missingDot = token.Replace("].[EntityColumn:", "][EntityColumn:");
		missingDot.Should().NotBe(token, because: "the arrange must actually have removed the dot");

		// Act
		CallToolResult refused = await ProcessDesignerE2EArrange.CallToolAsync(context, ModifyToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = JsonSerializer.Serialize(new[] {
					new Dictionary<string, object> {
						["op"] = "addMapping",
						["mapping"] = new Dictionary<string, string> {
							["targetProcessParameter"] = "MapOut", ["expression"] = missingDot
						}
					}
				})
			});

		// Assert
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(refused);
		string refusal = string.Join(" ", (execution.Output ?? []).Select(message => message.Value));
		execution.ExitCode.Should().NotBe(0, because: "the misspelled expression must abort the edit");
		refusal.Should().Contain("The 'expression' mapping for target 'MapOut'",
			because: "the refusal names the mapping, which the platform's message does not");
		refusal.Should().Contain($"Send '{token}'", because: "the refusal hands back the canonical token");
		refusal.Should().NotContain("name it", because: "a mapping expression takes no names, so none is offered");
	}

	#endregion

	#region Methods: Private

	private static async Task CreateAsync(ProcessDesignerArrangeContext context, string processName,
			string? condition) {
		CallToolResult created = await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = BuildDescriptor(processName, condition)
			});
		JsonSerializer.Serialize(created).Should().Contain("created (UId:",
			because: "the process the modify call edits must exist, or the assertions fail for the wrong reason");
	}

	/// <summary>Read contact, then Call; the flow between them is conditional when a condition is given.</summary>
	private static string BuildDescriptor(string processName, string? condition) {
		// Without a condition the read leads straight to the call and End2 is not built at all, so the process has
		// no disconnected element; with one, End2 is the default branch beside the conditional one.
		string readToCall = condition == null
			? """{ "source": "ReadContact", "target": "Call" }"""
			: $$"""{ "source": "ReadContact", "target": "Call", "kind": "conditional", "condition": {{JsonSerializer.Serialize(condition)}} }, { "source": "ReadContact", "target": "End2", "kind": "default" }""";
		string end2 = condition == null ? string.Empty : """, { "name": "End2", "type": "endEvent" }""";
		return $$"""
			{
			  "name": "{{processName}}",
			  "caption": "Clio BP Meta-path Condition E2E",
			  "packageName": "Custom",
			  "parameters": [ { "name": "MapOut", "type": "Boolean" } ],
			  "elements": [
			    { "name": "Start1", "type": "startEvent" },
			    { "name": "ReadContact", "type": "readData", "caption": "Read contact",
			      "readData": { "source": "Contact", "mode": "first" } },
			    { "name": "Call", "type": "performTask", "caption": "Call the contact" },
			    { "name": "End1", "type": "endEvent" }{{end2}}
			  ],
			  "flows": [
			    { "source": "Start1", "target": "ReadContact" },
			    {{readToCall}},
			    { "source": "Call", "target": "End1" }
			  ]
			}
			""";
	}

	private static async Task ModifyExpectingSuccessAsync(ProcessDesignerArrangeContext context, string processName,
			string operations) {
		CallToolResult result = await ProcessDesignerE2EArrange.CallToolAsync(context, ModifyToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = operations
			});
		McpCommandExecutionParser.Extract(result).ExitCode.Should().Be(0,
			because: "the arrangement edit must apply, or the assertions fail for the wrong reason");
	}

	private static string SetCondition(string condition) =>
		JsonSerializer.Serialize(new[] {
			new Dictionary<string, string> {
				["op"] = "setFlowCondition", ["source"] = "ReadContact", ["target"] = "Call", ["condition"] = condition
			}
		});

	/// <summary>The first [#…#] token of a described condition - the reference without the comparison.</summary>
	private static string TokenOf(string condition) =>
		condition.Substring(0, condition.IndexOf("#]", StringComparison.Ordinal) + 2);

	private static string ConditionOnCall(JsonObject graph) =>
		graph["flows"]!.AsArray().Select(flow => flow!.AsObject())
			.Single(flow => flow["target"]?.GetValue<string>() == "Call")["condition"]?.GetValue<string>()
		?? string.Empty;

	#endregion

}
