using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for ENG-103990 over the real MCP path: several signal starts on one object share ONE chain
/// through a process parameter with <c>mergeFrom</c>, which takes the record Id of whichever start fired. NOT in CI -
/// run manually against a stand carrying CrtProcessBuilder 1.6.6.154 or newer (mergeFrom arrived in 1.6.6.153;
/// 1.6.6.154 is the create and modify floor).
/// <para>Two facts a caller can see: the run started by either signal reads the triggering record through the merged
/// parameter, and a describe -> modify round trip through clio keeps the merge byte for byte (D7) - the field is
/// declared on clio's describe model, so it is not dropped on the way to the caller.</para>
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class ProcessParameterMergeToolE2ETests {

	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;
	private const string ModifyToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;

	/// <summary>The cut that reads mergeFrom; named in the skip message.</summary>
	private const string MinimumPackageVersion = "1.6.6.154";

	private const string MergedParameter = "PContactId";

	#region Methods: Tests

	[Test]
	[Description("Two signal starts on Contact flow straight into one shared Read data filtered by a mergeFrom parameter. The build raises no warning at all and the run started by the 'added' signal reads the triggering record - the measured fix for the naive join, which completes and changes nothing. The 'added' start is SigB, the SECOND source in the stored formula (schema order), so a merge that only ever reads the first start's Id fails here.")]
	[AllureTag(CreateToolName)]
	[AllureName("a mergeFrom parameter feeds one shared chain from two signal starts")]
	public async Task CreateBusinessProcess_Should_ReadTheTriggeringRecord_ThroughAMergedParameter() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("Process parameter merge", MinimumPackageVersion);
		string token = $"ClioMerge{Guid.NewGuid():N}"[..20];
		string processName = $"UsrClioBpMerge{Guid.NewGuid():N}";

		// Act
		CallToolResult buildCall = await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = MergedDescriptor(processName, token)
			});
		string built = ToolText(buildCall);
		JsonElement run = await TriggerAsync(context, processName, token);

		// Assert
		built.Should().Contain("created (UId:", because: "the merge build must succeed: {0}", built);
		(McpCommandExecutionParser.Extract(buildCall).Output ?? [])
			.Where(message => message.MessageType == LogDecoratorType.Warning)
			.Select(message => message.Value)
			.Should().BeEmpty(because: "a correct merge earns no warning at all - neither the server's (a shared element "
				+ "reading one start's RecordId, a start reaching a reader of the merged parameter without being in "
				+ "mergeFrom) nor clio's binding warnings, and nothing else in this descriptor warns: {0}", built);
		CreatedTitle(run).Should().Be($"{token}:{token} Alice",
			because: "the 'added' run reads its record through the merged Id: {0}", run.ToString());
	}

	[Test]
	[Description("describe-business-process reports the merged parameter as mergeFrom, and feeding that list back through setParameter changes nothing: the second describe reports the same parameter. Pins D7 - the list survives clio's describe model and short forms.")]
	[AllureTag(ModifyToolName)]
	[AllureName("describe -> modify keeps a mergeFrom parameter unchanged")]
	public async Task DescribeThenModify_Should_KeepTheMergeUnchanged() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("Process parameter merge", MinimumPackageVersion);
		string token = $"ClioMerge{Guid.NewGuid():N}"[..20];
		string processName = $"UsrClioBpMergeRt{Guid.NewGuid():N}";
		string built = ToolText(await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = MergedDescriptor(processName, token)
			}));
		built.Should().Contain("created (UId:", because: "the arrange step must have built the process: {0}", built);
		JsonObject before = await DescribedParameterAsync(context, processName);

		// Act
		string modified = ToolText(await ProcessDesignerE2EArrange.CallToolAsync(context, ModifyToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = JsonSerializer.Serialize(new[] {
					new Dictionary<string, object?> {
						["op"] = "setParameter", ["parameterName"] = MergedParameter,
						["parameterUpdate"] = new Dictionary<string, object?> { ["mergeFrom"] = before["mergeFrom"] }
					}
				})
			}));
		JsonObject after = await DescribedParameterAsync(context, processName);

		// Assert
		before["mergeFrom"]!.AsArray().Select(entry => entry!.GetValue<string>())
			.Should().Equal(["SigA.RecordId", "SigB.RecordId"],
				because: "describe reports the merge as the list, in the starts' order: {0}", before.ToJsonString());
		before["value"].Should().BeNull(because: "a merge is reported as mergeFrom, never beside its raw formula");
		modified.Should().Contain("edited (", because: "re-sending the described list is a valid edit: {0}", modified);
		after.ToJsonString().Should().Be(before.ToJsonString(),
			because: "a describe -> modify round trip through clio must change nothing (D7)");
	}

	#endregion

	#region Methods: Private

	private static string ToolText(CallToolResult callResult) =>
		string.Join(" ", callResult.Content.OfType<TextContentBlock>().Select(block => block.Text));

	private static async Task<JsonObject> DescribedParameterAsync(ProcessDesignerArrangeContext context,
		string processName) {
		JsonObject graph = DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.CallToolAsync(context,
			DescribeProcessTool.ToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["mode"] = "full",
				["process-name"] = processName
			}));
		JsonObject? parameter = graph["parameters"]!.AsArray().OfType<JsonObject>()
			.FirstOrDefault(candidate => candidate["name"]?.GetValue<string>() == MergedParameter);
		parameter.Should().NotBeNull(because: "the merged parameter must be described: {0}", graph.ToJsonString());
		return parameter!;
	}

	private static async Task<JsonElement> TriggerAsync(ProcessDesignerArrangeContext context, string processName,
		string token) {
		string text = ToolText(await ProcessDesignerE2EArrange.CallToolAsync(context, TriggerSmokeTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["entity"] = "Contact",
				["values"] = new Dictionary<string, object?> { ["Name"] = $"{token} Alice" },
				["watch"] = new[] { "AccountType" },
				["process"] = processName,
				["timeout"] = 30
			}));
		JsonElement response = JsonDocument.Parse(text).RootElement.Clone();
		response.GetProperty("success").GetBoolean().Should().BeTrue(
			because: "the signal must start one run of the process under test and it must complete: {0}", text);
		return response;
	}

	private static string? CreatedTitle(JsonElement run) =>
		run.GetProperty("created").EnumerateArray()
			.Where(item => item.GetProperty("entity").GetString() == "AccountType")
			.Select(item => item.GetProperty("title").GetString())
			.SingleOrDefault();

	private static string MergedDescriptor(string processName, string token) {
		Dictionary<string, object> byToken = new() {
			["object"] = "Contact", ["logicalOperation"] = "and",
			["conditions"] = new[] {
				new Dictionary<string, object> { ["column"] = "Name", ["comparison"] = "contains", ["value"] = token }
			}
		};
		return JsonSerializer.Serialize(new Dictionary<string, object> {
			["name"] = processName, ["caption"] = "Clio BP Parameter Merge E2E", ["packageName"] = "Custom",
			["parameters"] = new object[] {
				new Dictionary<string, object> {
					["name"] = MergedParameter, ["type"] = "Guid",
					["mergeFrom"] = new[] { "SigB.RecordId", "SigA.RecordId" }
				},
				new Dictionary<string, object> { ["name"] = "PName", ["type"] = "Text", ["direction"] = "Variable" },
				new Dictionary<string, object> { ["name"] = "PTitle", ["type"] = "Text", ["direction"] = "Variable" }
			},
			["elements"] = new object[] {
				new Dictionary<string, object> {
					["name"] = "SigA", ["type"] = "signalStart",
					["signal"] = new Dictionary<string, object> { ["entity"] = "Contact", ["on"] = "modified" },
					["filter"] = byToken
				},
				new Dictionary<string, object> {
					["name"] = "SigB", ["type"] = "signalStart",
					["signal"] = new Dictionary<string, object> { ["entity"] = "Contact", ["on"] = "added" },
					["filter"] = byToken
				},
				new Dictionary<string, object> {
					["name"] = "ReadShared", ["type"] = "readData", ["caption"] = "Read the triggering contact",
					["readData"] = new Dictionary<string, object> {
						["source"] = "Contact", ["mode"] = "first", ["columns"] = new[] { "Id", "Name" }
					},
					["filter"] = new Dictionary<string, object> {
						["object"] = "Contact", ["logicalOperation"] = "and",
						["conditions"] = new[] {
							new Dictionary<string, object> {
								["column"] = "Id", ["comparison"] = "equal", ["processParameter"] = MergedParameter
							}
						}
					}
				},
				new Dictionary<string, object> {
					["name"] = "FTitle", ["type"] = "formulaTask", ["caption"] = "Compose title",
					["formula"] = new Dictionary<string, object> {
						["body"] = $"\"{token}:\" + [#PName#]", ["resultProcessParameter"] = "PTitle"
					}
				},
				new Dictionary<string, object> {
					["name"] = "AddRec", ["type"] = "addData", ["caption"] = "Record the title",
					["addData"] = new Dictionary<string, object> {
						["source"] = "AccountType",
						["values"] = new[] {
							new Dictionary<string, object> { ["column"] = "Name", ["processParameter"] = "PTitle" }
						}
					}
				},
				new Dictionary<string, object> { ["name"] = "End1", ["type"] = "endEvent" }
			},
			["flows"] = new[] {
				Flow("SigA", "ReadShared"), Flow("SigB", "ReadShared"), Flow("ReadShared", "FTitle"),
				Flow("FTitle", "AddRec"), Flow("AddRec", "End1")
			},
			["mappings"] = new[] {
				new Dictionary<string, object> {
					["targetProcessParameter"] = "PName", ["sourceElement"] = "ReadShared",
					["sourceElementParameter"] = "ResultEntity", ["sourceColumn"] = "Name"
				}
			}
		});
	}

	private static Dictionary<string, object> Flow(string source, string target) =>
		new() { ["source"] = source, ["target"] = target };

	#endregion
}
