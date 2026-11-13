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
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for ENG-105558 over the real MCP path: a process parameter that only carries a value between
/// elements of the process is saved <c>Internal</c>, not <c>Variable</c>. Runs on the process-designer lane, which
/// installs the bundled CrtProcessBuilder; locally, against a stand carrying 1.6.6.172 or newer. Each test deletes
/// the process it built.
/// <para>The builder decides only for a parameter the SAME request creates with no direction and an element fills - an
/// element output mapped onto it, an expression mapping that reads an element, or a Formula result. A stated direction
/// is never switched, but a <c>Variable</c> stated for a parameter the request creates and an element fills is advised
/// once; a parameter only a constant fills keeps <c>Variable</c> silently; a <c>FileCollection</c> keeps its
/// <c>Variable</c> default with no advice; and a parameter that existed before the request is never switched - it gets
/// advice instead, worded as a report that asks for its callers first.</para>
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class ProcessParameterDirectionToolE2ETests {

	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;
	private const string ModifyToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;

	/// <summary>The cut that sets Internal on a defaulted helper; named in the skip message.</summary>
	private const string MinimumPackageVersion = "1.6.6.172";

	private const string SwitchNotice = "was added with no direction, and an element of this process fills it";
	private const string AdviceWarning = "is 'Variable' (bidirectional), and an element of this process fills it";
	private const string CallersFirst = "It existed before this request, so other processes may already call it";

	#region Methods: Tests

	[Test]
	[Description("create-business-process with four Text parameters: one declared with no direction that a Formula element fills, one with no direction that an expression mapping reading an element fills, one with a stated Variable that a Formula fills, and one with no direction that only a constant fills. The first two are saved Internal and named as switched once; the stated Variable keeps Variable and is advised once; the constant keeps Variable, silently.")]
	[AllureTag(CreateToolName)]
	[AllureName("a helper parameter declared with no direction is built Internal")]
	public async Task CreateBusinessProcess_Should_SaveOnlyTheDefaultedHelpersInternal() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("Process parameter direction", MinimumPackageVersion);
		string processName = $"UsrClioBpDirection{Guid.NewGuid():N}";

		// Act
		string built = ToolText(await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = BuildDescriptor(processName)
			}));
		// Before the try: when this fires clio refused the call, nothing was built, and the cleanup would only log.
		ProcessDesignerE2ESupport.IgnoreWhenProcessBuilderIsBehind(built, MinimumPackageVersion);

		try {
			// Assert
			// Checked before the read-back, so a refused build fails with its own refusal text.
			built.Should().Contain("created (UId:", because: "the build must succeed: {0}", built);
			Dictionary<string, string?> directions = await DirectionsAsync(context, processName);
			directions["PHelper"].Should().Be("Internal",
				because: "no direction was stated and only a Formula element of the process fills it");
			directions["PExpression"].Should().Be("Internal",
				because: "no direction was stated and an expression that reads an element output fills it");
			directions["PStated"].Should().Be("Variable", because: "a stated direction is the author's choice");
			directions["PConstant"].Should().Be("Variable",
				because: "a constant is a starting value, not a value an element of the process produces");
			foreach (string helper in new[] { "PHelper", "PExpression" }) {
				built.Should().Contain($"Process parameter '{helper}' {SwitchNotice}", Exactly.Once(),
					because: "the switch is reported once, with the way back: {0}", built);
				built.Should().NotContain($"Process parameter '{helper}' {AdviceWarning}",
					because: "a switched helper is no longer Variable, so there is nothing to advise: {0}", built);
			}
			built.Should().Contain($"Process parameter 'PStated' {AdviceWarning}", Exactly.Once(),
				because: "a Variable stated for a helper the request creates is kept but advised once: {0}", built);
			built.Should().NotContain($"Process parameter 'PStated' {SwitchNotice}",
				because: "a stated direction is never switched, so it is never reported as one: {0}", built);
			built.Should().NotContain(CallersFirst,
				because: "nothing can call a parameter the request has just created: {0}", built);
			built.Should().NotContain("'PConstant'", because: "nothing in the process fills it: {0}", built);
		} finally {
			await ProcessDesignerE2ESupport.DeleteProcessAsync(context.EnvironmentName, processName);
		}
	}

	[Test]
	[Description("create-business-process with a FileCollection declared with no direction that a Process file element's ObjectFiles output fills: the FileCollection keeps its Variable default (owner decision D3) and the response neither switches nor advises it.")]
	[AllureTag(CreateToolName)]
	[AllureName("a FileCollection an element fills keeps Variable, with no advice")]
	public async Task CreateBusinessProcess_Should_KeepAFilledFileCollectionVariable_WithNoAdvice() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("Process parameter direction", MinimumPackageVersion);
		string processName = $"UsrClioBpDirectionFiles{Guid.NewGuid():N}";

		// Act
		string built = ToolText(await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = FileCollectionDescriptor(processName)
			}));
		// Before the try: when this fires clio refused the call, nothing was built, and the cleanup would only log.
		ProcessDesignerE2ESupport.IgnoreWhenProcessBuilderIsBehind(built, MinimumPackageVersion);

		try {
			// Assert
			// Checked before the read-back, so a refused build fails with its own refusal text.
			built.Should().Contain("created (UId:", because: "the build must succeed: {0}", built);
			Dictionary<string, string?> directions = await DirectionsAsync(context, processName);
			directions["PDocs"].Should().Be("Variable",
				because: "a FileCollection keeps its Variable default even when an element fills it (owner decision D3)");
			built.Should().NotContain($"Process parameter 'PDocs' {SwitchNotice}",
				because: "the builder does not switch a collection: {0}", built);
			built.Should().NotContain($"Process parameter 'PDocs' {AdviceWarning}",
				because: "a FileCollection is never advised: its Variable is the builder's own choice: {0}", built);
		} finally {
			await ProcessDesignerE2ESupport.DeleteProcessAsync(context.EnvironmentName, processName);
		}
	}

	[Test]
	[Description("modify-business-process on a process whose PExisting parameter was built with a stated Variable: one request maps an element output onto PExisting and adds PNew with no direction plus a mapping onto it. PNew is saved Internal with the switch notice once; PExisting, which existed before the request, keeps Variable and the response advises it once, worded as a report that asks for its callers first.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify switches a new defaulted helper and advises on an existing Variable one")]
	public async Task ModifyBusinessProcess_Should_SwitchTheNewHelperAndAdviseOnTheExistingOne() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("Process parameter direction", MinimumPackageVersion);
		string processName = $"UsrClioBpDirectionMod{Guid.NewGuid():N}";
		string built = ToolText(await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = ModifyBaseDescriptor(processName)
			}));
		// Before the try: when this fires clio refused the call, nothing was built, and the cleanup would only log.
		ProcessDesignerE2ESupport.IgnoreWhenProcessBuilderIsBehind(built, MinimumPackageVersion);

		try {
			built.Should().Contain("created (UId:", because: "the arrange step must have built the process: {0}", built);

			// Act
			string modified = ToolText(await ProcessDesignerE2EArrange.CallToolAsync(context, ModifyToolName,
				new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["process-name"] = processName,
					["operations"] = JsonSerializer.Serialize(new object[] {
						new Dictionary<string, object?> {
							["op"] = "addMapping", ["mapping"] = NameOfTheReadContact("PExisting")
						},
						new Dictionary<string, object?> {
							["op"] = "addParameter",
							["parameter"] = new Dictionary<string, object?> { ["name"] = "PNew", ["type"] = "Text" }
						},
						new Dictionary<string, object?> {
							["op"] = "addMapping", ["mapping"] = NameOfTheReadContact("PNew")
						}
					})
				}));

			// Assert
			// Checked before the read-back, so a refused edit fails with its own refusal text.
			modified.Should().Contain("edited (", because: "the edit must succeed: {0}", modified);
			Dictionary<string, string?> directions = await DirectionsAsync(context, processName);
			directions["PNew"].Should().Be("Internal",
				because: "the request created it with no direction and an element output fills it");
			directions["PExisting"].Should().Be("Variable",
				because: "a parameter that existed before the request is never switched");
			modified.Should().Contain($"Process parameter 'PNew' {SwitchNotice}", Exactly.Once(),
				because: "the switch is reported once: {0}", modified);
			modified.Should().NotContain($"Process parameter 'PNew' {AdviceWarning}",
				because: "a switched helper is no longer Variable, so there is nothing to advise: {0}", modified);
			modified.Should().Contain($"Process parameter 'PExisting' {AdviceWarning}", Exactly.Once(),
				because: "an existing Variable parameter the request made an element fill gets advice once: {0}", modified);
			modified.Should().Contain(CallersFirst, Exactly.Once(),
				because: "other processes may call an existing parameter, so its advice asks for the callers first: {0}",
				modified);
			modified.Should().NotContain($"Process parameter 'PExisting' {SwitchNotice}",
				because: "a parameter that existed before the request is never switched, so never reported as one: {0}",
				modified);
		} finally {
			await ProcessDesignerE2ESupport.DeleteProcessAsync(context.EnvironmentName, processName);
		}
	}

	#endregion

	#region Methods: Private

	private static string ToolText(CallToolResult callResult) =>
		string.Join(" ", callResult.Content.OfType<TextContentBlock>().Select(block => block.Text));

	private static async Task<Dictionary<string, string?>> DirectionsAsync(ProcessDesignerArrangeContext context,
		string processName) {
		JsonObject graph = DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.CallToolAsync(context,
			DescribeProcessTool.ToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["mode"] = "full",
				["process-name"] = processName
			}));
		return graph["parameters"]!.AsArray().OfType<JsonObject>()
			.ToDictionary(parameter => parameter["name"]!.GetValue<string>(),
				parameter => parameter["direction"]?.GetValue<string>());
	}

	private static Dictionary<string, object> NameOfTheReadContact(string targetParameter) =>
		new() {
			["targetProcessParameter"] = targetParameter, ["sourceElement"] = "ReadOne",
			["sourceElementParameter"] = "ResultEntity", ["sourceColumn"] = "Name"
		};

	private static Dictionary<string, object> ReadOneContact() =>
		new() {
			["name"] = "ReadOne", ["type"] = "readData", ["caption"] = "Read a contact",
			["readData"] = new Dictionary<string, object> {
				["source"] = "Contact", ["mode"] = "first", ["columns"] = new[] { "Id", "Name" }
			}
		};

	private static Dictionary<string, object> Formula(string name, string resultParameter) =>
		new() {
			["name"] = name, ["type"] = "formulaTask", ["caption"] = $"Set {resultParameter}",
			["formula"] = new Dictionary<string, object> {
				["body"] = "[#ReadOne.ResultEntity.Name#]", ["resultProcessParameter"] = resultParameter
			}
		};

	private static string BuildDescriptor(string processName) =>
		JsonSerializer.Serialize(new Dictionary<string, object> {
			["name"] = processName, ["caption"] = "Clio BP Parameter Direction E2E", ["packageName"] = "Custom",
			["parameters"] = new object[] {
				new Dictionary<string, object> { ["name"] = "PHelper", ["type"] = "Text" },
				new Dictionary<string, object> { ["name"] = "PExpression", ["type"] = "Text" },
				new Dictionary<string, object> { ["name"] = "PStated", ["type"] = "Text", ["direction"] = "Variable" },
				new Dictionary<string, object> { ["name"] = "PConstant", ["type"] = "Text", ["value"] = "fixed" }
			},
			["elements"] = new object[] {
				new Dictionary<string, object> { ["name"] = "Start1", ["type"] = "startEvent" },
				ReadOneContact(),
				Formula("FHelper", "PHelper"),
				Formula("FStated", "PStated"),
				new Dictionary<string, object> { ["name"] = "End1", ["type"] = "endEvent" }
			},
			["flows"] = new[] {
				Flow("Start1", "ReadOne"), Flow("ReadOne", "FHelper"), Flow("FHelper", "FStated"), Flow("FStated", "End1")
			},
			["mappings"] = new object[] {
				new Dictionary<string, object> {
					["targetProcessParameter"] = "PExpression",
					["expression"] = "[#ReadOne.ResultEntity.Name#] + \" (copy)\""
				}
			}
		});

	private static string FileCollectionDescriptor(string processName) =>
		JsonSerializer.Serialize(new Dictionary<string, object> {
			["name"] = processName, ["caption"] = "Clio BP Parameter Direction Files E2E", ["packageName"] = "Custom",
			["parameters"] = new object[] {
				new Dictionary<string, object> {
					["name"] = "ContactRef", ["type"] = "Lookup", ["referenceSchema"] = "Contact", ["direction"] = "In"
				},
				new Dictionary<string, object> { ["name"] = "PDocs", ["type"] = "FileCollection" }
			},
			["elements"] = new object[] {
				new Dictionary<string, object> { ["name"] = "Start1", ["type"] = "startEvent" },
				new Dictionary<string, object> {
					["name"] = "OF1", ["type"] = "fileProcessing", ["caption"] = "Process contact files",
					["fileProcessing"] = new Dictionary<string, object> {
						["attachments"] = new Dictionary<string, object> {
							["object"] = "Contact",
							["recordId"] = new Dictionary<string, object> { ["processParameter"] = "ContactRef" }
						}
					}
				},
				new Dictionary<string, object> { ["name"] = "End1", ["type"] = "endEvent" }
			},
			["flows"] = new[] { Flow("Start1", "OF1"), Flow("OF1", "End1") },
			["mappings"] = new object[] {
				new Dictionary<string, object> {
					["targetProcessParameter"] = "PDocs", ["sourceElement"] = "OF1",
					["sourceElementParameter"] = "ObjectFiles"
				}
			}
		});

	private static string ModifyBaseDescriptor(string processName) =>
		JsonSerializer.Serialize(new Dictionary<string, object> {
			["name"] = processName, ["caption"] = "Clio BP Parameter Direction Modify E2E", ["packageName"] = "Custom",
			["parameters"] = new object[] {
				new Dictionary<string, object> { ["name"] = "PExisting", ["type"] = "Text", ["direction"] = "Variable" }
			},
			["elements"] = new object[] {
				new Dictionary<string, object> { ["name"] = "Start1", ["type"] = "startEvent" },
				ReadOneContact(),
				new Dictionary<string, object> { ["name"] = "End1", ["type"] = "endEvent" }
			},
			["flows"] = new[] { Flow("Start1", "ReadOne"), Flow("ReadOne", "End1") }
		});

	private static Dictionary<string, object> Flow(string source, string target) =>
		new() { ["source"] = source, ["target"] = target };

	#endregion
}
