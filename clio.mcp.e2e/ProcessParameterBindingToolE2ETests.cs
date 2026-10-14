using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
/// End-to-end coverage for ENG-103081 over the real MCP path: a <c>targetProcessParameter</c> mapping is a BINDING
/// the parameter is read through, not an assignment made when a branch runs. NOT in CI - run manually against a
/// stand carrying CrtProcessBuilder.
/// <para>The fixture reproduces the reported scenario - two signal starts on one object, each reading the
/// triggering record and mapping it into ONE shared parameter - and proves three things a caller can see: the
/// build now WARNS about it, the run still reads the shared parameter EMPTY (the platform behaviour the warning is
/// about), and a Formula element per branch with <c>resultProcessParameter</c> assigns the value on every run. A
/// single-start case pins the other half: a lone binding read after its element ran is correct and is NOT
/// warned about.</para>
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class ProcessParameterBindingToolE2ETests {

	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;
	private const string ModifyToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;

	/// <summary>The cut that builds a Formula element and resolves sourceColumn; named in the skip message.</summary>
	private const string MinimumPackageVersion = "1.6.6.40";

	#region Methods: Tests

	[Test]
	[Description("The ENG-103081 scenario: two signal starts each map their own read into one shared process parameter. The build warns that the parameter is bound twice and that the surviving binding's element is not reached from the other start; the run started by the first signal then reads the shared parameter EMPTY, which is the platform behaviour the warnings describe.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process warns on a shared parameter bound per branch, and the run reads it empty")]
	public async Task CreateBusinessProcess_Should_WarnAndReadEmpty_WhenEachStartBindsTheSharedParameter() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("Process parameter binding", MinimumPackageVersion);
		string token = $"ClioBind{Guid.NewGuid():N}"[..20];
		string processName = $"UsrClioBpBindTwice{Guid.NewGuid():N}";

		// Act
		string built = ToolText(await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = TwoStartDescriptor(processName, token, perBranchFormula: false)
			}));
		JsonElement run = await TriggerAsync(context, processName, token);

		// Assert
		built.Should().Contain("created (UId:", because: "the binding warnings are caveats on a build that succeeded: {0}", built);
		built.Should().Contain("binds process parameter 'PName' 2 times",
			because: "the second mapping silently replaced the first before ENG-103081: {0}", built);
		built.Should().Contain("started by 'SigA' does not reach",
			because: "the surviving binding points at ReadB, which a SigA run never executes: {0}", built);
		CreatedTitle(run).Should().Be($"{token}:",
			because: "the SigA run reads PName through a binding to the branch that did not run, so it is empty: {0}",
			run.ToString());
	}

	[Test]
	[Description("The fix for the same scenario: a Formula element on each branch assigns the shared parameter with formula.resultProcessParameter. The build raises no binding warning, and the run started by the first signal reads the triggering record's name.")]
	[AllureTag(CreateToolName)]
	[AllureName("a formulaTask per branch assigns the shared parameter on every run")]
	public async Task CreateBusinessProcess_FormulaPerBranch_Should_AssignTheSharedParameterPerRun() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("Process parameter binding", MinimumPackageVersion);
		string token = $"ClioBind{Guid.NewGuid():N}"[..20];
		string processName = $"UsrClioBpBindFormula{Guid.NewGuid():N}";

		// Act
		string built = ToolText(await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = TwoStartDescriptor(processName, token, perBranchFormula: true)
			}));
		JsonElement run = await TriggerAsync(context, processName, token);

		// Assert
		built.Should().Contain("created (UId:", because: "the formula variant must build: {0}", built);
		built.Should().NotContain("process parameter 'PName'",
			because: "a Formula assignment is not a binding, so neither binding warning applies: {0}", built);
		CreatedTitle(run).Should().Be($"{token}:{token} Alice",
			because: "the SigA branch's Formula assigned the triggering record's name before the title was built: {0}",
			run.ToString());
	}

	[Test]
	[Description("The supported half, pinned so the warning cannot grow into it: in a single-start process a lone binding to a Read data column reads EMPTY in a Formula placed before the read and the column value in one placed after it - a live binding, not a default evaluated at start - and the build raises no binding warning.")]
	[AllureTag(CreateToolName)]
	[AllureName("a lone binding in a single-start process is read live and is not warned about")]
	public async Task CreateBusinessProcess_Should_NotWarn_AndReadTheBindingLive_InASingleStartProcess() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("Process parameter binding", MinimumPackageVersion);
		string processName = $"UsrClioBpBindLive{Guid.NewGuid():N}";

		// Act
		string built = ToolText(await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = SingleStartDescriptor(processName)
			}));
		string ran = ToolText(await ProcessDesignerE2EArrange.CallToolAsync(context, RunProcessTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["result-parameters"] = new[] { "PName", "PEarly", "PLate" }
			}));

		// Assert
		built.Should().Contain("created (UId:", because: "the arrange step must have built the process: {0}", built);
		built.Should().NotContain("process parameter 'PName'",
			because: "a lone binding read after its element ran is the supported pattern; a warning would be false: {0}",
			built);
		JsonElement values = JsonDocument.Parse(ran).RootElement.GetProperty("resultParameterValues");
		values.GetProperty("PEarly").GetString().Should().Be("early:",
			because: "read before ReadFirst ran, the binding has no value yet: {0}", ran);
		string name = values.GetProperty("PName").GetString()!;
		name.Should().NotBeNullOrEmpty(because: "read at the end of the run, the binding returns the column: {0}", ran);
		values.GetProperty("PLate").GetString().Should().Be($"late:{name}",
			because: "read after ReadFirst ran, the same binding returns the column - it is evaluated on read: {0}", ran);
	}

	[Test]
	[Description("On modify, an addMapping onto a process parameter the process already binds to ANOTHER element is applied and warned about, naming the binding it replaced - modify has no removeMapping, so the overwrite used to be silent.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process warns when addMapping replaces a stored binding")]
	public async Task ModifyBusinessProcess_Should_Warn_WhenAddMappingReplacesAStoredBinding() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("Process parameter binding", MinimumPackageVersion);
		string token = $"ClioBind{Guid.NewGuid():N}"[..20];
		string processName = $"UsrClioBpBindModify{Guid.NewGuid():N}";
		string built = ToolText(await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = TwoStartDescriptor(processName, token, perBranchFormula: false, bindBranchB: false)
			}));
		built.Should().Contain("created (UId:", because: "the arrange step must have built the process: {0}", built);

		// Act
		string modified = ToolText(await ProcessDesignerE2EArrange.CallToolAsync(context, ModifyToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = """
					[{"op":"addMapping","mapping":{"targetProcessParameter":"PName","sourceElement":"ReadB",
						"sourceElementParameter":"ResultEntity","sourceColumn":"Name"}}]
					"""
			}));

		// Assert
		modified.Should().Contain("edited (", because: "the re-map is applied, the warning is a caveat: {0}", modified);
		modified.Should().Contain("replaced the existing binding of process parameter 'PName'",
			because: "the edit dropped the ReadA binding and must say so: {0}", modified);
		modified.Should().Contain("was ReadA.ResultEntity.Name, now ReadB.ResultEntity.Name",
			because: "the caller needs both sides of the overwrite to decide whether it was meant: {0}", modified);
	}

	#endregion

	#region Methods: Private

	/// <summary>The tool's text content, unescaped - the serialized result escapes the quotes messages use.</summary>
	private static string ToolText(CallToolResult callResult) =>
		string.Join(" ", callResult.Content.OfType<TextContentBlock>().Select(block => block.Text));

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

	// The AccountType the run added, whose Name the process composed from the shared parameter.
	private static string? CreatedTitle(JsonElement run) =>
		run.GetProperty("created").EnumerateArray()
			.Where(item => item.GetProperty("entity").GetString() == "AccountType")
			.Select(item => item.GetProperty("title").GetString())
			.SingleOrDefault();

	private static string TwoStartDescriptor(string processName, string token, bool perBranchFormula,
		bool bindBranchB = true) {
		static Dictionary<string, object> Filter(string column, string comparison, string rightKey, object right) =>
			new() {
				["object"] = "Contact", ["logicalOperation"] = "and",
				["conditions"] = new[] {
					new Dictionary<string, object> { ["column"] = column, ["comparison"] = comparison, [rightKey] = right }
				}
			};
		Dictionary<string, object> byToken = Filter("Name", "contains", "value", token);
		Dictionary<string, object> ById(string start) => Filter("Id", "equal", "elementParameter",
			new Dictionary<string, object> { ["elementName"] = start, ["parameter"] = "RecordId" });
		List<object> elements = [
			new Dictionary<string, object> {
				["name"] = "SigA", ["type"] = "signalStart",
				["signal"] = new Dictionary<string, object> { ["entity"] = "Contact", ["on"] = "added" }, ["filter"] = byToken
			},
			new Dictionary<string, object> {
				["name"] = "SigB", ["type"] = "signalStart",
				["signal"] = new Dictionary<string, object> { ["entity"] = "Contact", ["on"] = "modified" }, ["filter"] = byToken
			},
			ReadElement("ReadA", ById("SigA")),
			ReadElement("ReadB", ById("SigB")),
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
					["values"] = new[] { new Dictionary<string, object> { ["column"] = "Name", ["processParameter"] = "PTitle" } }
				}
			},
			new Dictionary<string, object> { ["name"] = "End1", ["type"] = "endEvent" }
		];
		List<object> flows = [Flow("SigA", "ReadA"), Flow("SigB", "ReadB")];
		List<object> mappings = [];
		if (perBranchFormula) {
			elements.Add(AssignElement("FA", "ReadA"));
			elements.Add(AssignElement("FB", "ReadB"));
			flows.AddRange([Flow("ReadA", "FA"), Flow("ReadB", "FB"), Flow("FA", "FTitle"), Flow("FB", "FTitle")]);
		} else {
			flows.AddRange([Flow("ReadA", "FTitle"), Flow("ReadB", "FTitle")]);
			mappings.Add(Binding("ReadA"));
			if (bindBranchB) {
				mappings.Add(Binding("ReadB"));
			}
		}
		flows.AddRange([Flow("FTitle", "AddRec"), Flow("AddRec", "End1")]);
		return JsonSerializer.Serialize(new Dictionary<string, object> {
			["name"] = processName, ["caption"] = "Clio BP Parameter Binding E2E", ["packageName"] = "Custom",
			["parameters"] = new[] { TextParameter("PName", "Variable"), TextParameter("PTitle", "Variable") },
			["elements"] = elements, ["flows"] = flows, ["mappings"] = mappings
		});
	}

	private static string SingleStartDescriptor(string processName) =>
		JsonSerializer.Serialize(new Dictionary<string, object> {
			["name"] = processName, ["caption"] = "Clio BP Parameter Binding Live E2E", ["packageName"] = "Custom",
			["parameters"] = new[] {
				TextParameter("PName", "Out"), TextParameter("PEarly", "Out"), TextParameter("PLate", "Out")
			},
			["elements"] = new object[] {
				new Dictionary<string, object> { ["name"] = "Start1", ["type"] = "startEvent" },
				CopyElement("FEarly", "early:", "PEarly"),
				new Dictionary<string, object> {
					["name"] = "ReadFirst", ["type"] = "readData", ["caption"] = "Read a contact",
					["readData"] = new Dictionary<string, object> {
						["source"] = "Contact", ["mode"] = "first", ["columns"] = new[] { "Id", "Name" },
						["sort"] = new Dictionary<string, object> { ["column"] = "Name" }
					},
					["filter"] = new Dictionary<string, object> {
						["object"] = "Contact", ["logicalOperation"] = "and",
						["conditions"] = new[] { new Dictionary<string, object> { ["column"] = "Name", ["comparison"] = "isNotNull" } }
					}
				},
				CopyElement("FLate", "late:", "PLate"),
				new Dictionary<string, object> { ["name"] = "End1", ["type"] = "endEvent" }
			},
			["flows"] = new[] {
				Flow("Start1", "FEarly"), Flow("FEarly", "ReadFirst"), Flow("ReadFirst", "FLate"), Flow("FLate", "End1")
			},
			["mappings"] = new[] {
				new Dictionary<string, object> {
					["targetProcessParameter"] = "PName", ["sourceElement"] = "ReadFirst",
					["sourceElementParameter"] = "ResultEntity", ["sourceColumn"] = "Name"
				}
			}
		});

	private static Dictionary<string, object> ReadElement(string name, Dictionary<string, object> filter) =>
		new() {
			["name"] = name, ["type"] = "readData", ["caption"] = $"Read {name}",
			["readData"] = new Dictionary<string, object> {
				["source"] = "Contact", ["mode"] = "first", ["columns"] = new[] { "Id", "Name" }
			},
			["filter"] = filter
		};

	private static Dictionary<string, object> AssignElement(string name, string read) =>
		new() {
			["name"] = name, ["type"] = "formulaTask", ["caption"] = $"Assign from {read}",
			["formula"] = new Dictionary<string, object> {
				["body"] = $"[#{read}.ResultEntity.Name#]", ["resultProcessParameter"] = "PName"
			}
		};

	private static Dictionary<string, object> CopyElement(string name, string prefix, string target) =>
		new() {
			["name"] = name, ["type"] = "formulaTask", ["caption"] = name,
			["formula"] = new Dictionary<string, object> {
				["body"] = $"\"{prefix}\" + [#PName#]", ["resultProcessParameter"] = target
			}
		};

	private static Dictionary<string, object> Binding(string read) =>
		new() {
			["targetProcessParameter"] = "PName", ["sourceElement"] = read,
			["sourceElementParameter"] = "ResultEntity", ["sourceColumn"] = "Name"
		};

	private static Dictionary<string, object> TextParameter(string name, string direction) =>
		new() { ["name"] = name, ["type"] = "Text", ["direction"] = direction };

	private static Dictionary<string, object> Flow(string source, string target) =>
		new() { ["source"] = source, ["target"] = target };

	#endregion
}
