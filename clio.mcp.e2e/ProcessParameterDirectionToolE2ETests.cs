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
/// installs the bundled CrtProcessBuilder; locally, against a stand carrying 1.6.6.170 or newer. Each test deletes
/// the process it built.
/// <para>The builder decides only for a parameter the SAME request creates with no direction and an element fills. A
/// stated direction is never switched, but a <c>Variable</c> stated for a parameter the request creates and an element
/// fills is advised once; a parameter only a constant fills keeps <c>Variable</c> silently; and a parameter that existed
/// before the request is never switched - it gets advice instead.</para>
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
	private const string MinimumPackageVersion = "1.6.6.170";

	private const string SwitchNotice = "was added with no direction, and an element of this process fills it";
	private const string AdviceWarning = "is 'Variable' (bidirectional), and an element of this process fills it";

	#region Methods: Tests

	[Test]
	[Description("create-business-process with three Text parameters: one declared with no direction that a Formula element fills, one with a stated Variable that a Formula fills, and one with no direction that only a constant fills. Only the first is saved Internal and named as switched; the stated Variable keeps Variable and is advised once; the constant keeps Variable, silently.")]
	[AllureTag(CreateToolName)]
	[AllureName("a helper parameter declared with no direction is built Internal")]
	public async Task CreateBusinessProcess_Should_SaveOnlyTheDefaultedHelperInternal() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("Process parameter direction", MinimumPackageVersion);
		string processName = $"UsrClioBpDirection{Guid.NewGuid():N}";

		try {
			// Act
			CallToolResult buildCall = await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
				new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["descriptor"] = BuildDescriptor(processName)
				});
			string built = ToolText(buildCall);
			ProcessDesignerE2ESupport.IgnoreWhenProcessBuilderIsBehind(built, MinimumPackageVersion);
			Dictionary<string, string?> directions = await DirectionsAsync(context, processName);

			// Assert
			built.Should().Contain("created (UId:", because: "the build must succeed: {0}", built);
			directions["PHelper"].Should().Be("Internal",
				because: "no direction was stated and only a Formula element of the process fills it");
			directions["PStated"].Should().Be("Variable", because: "a stated direction is the author's choice");
			directions["PConstant"].Should().Be("Variable",
				because: "a constant is a starting value, not a value an element of the process produces");
			built.Should().Contain($"Process parameter 'PHelper' {SwitchNotice}",
				because: "the switch is reported, with the way back: {0}", built);
			built.Should().Contain($"Process parameter 'PStated' {AdviceWarning}",
				because: "a Variable stated for a helper the request creates is kept but advised once: {0}", built);
			built.Should().NotContain("'PConstant'", because: "nothing in the process fills it: {0}", built);
		} finally {
			await ProcessDesignerE2ESupport.DeleteProcessAsync(context.EnvironmentName, processName);
		}
	}

	[Test]
	[Description("modify-business-process on a process whose PExisting parameter was built with a stated Variable: one request maps an element output onto PExisting and adds PNew with no direction plus a mapping onto it. PNew is saved Internal with the switch notice; PExisting, which existed before the request, keeps Variable and the response advises Internal / Out / Variable for it.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify switches a new defaulted helper and advises on an existing Variable one")]
	public async Task ModifyBusinessProcess_Should_SwitchTheNewHelperAndAdviseOnTheExistingOne() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("Process parameter direction", MinimumPackageVersion);
		string processName = $"UsrClioBpDirectionMod{Guid.NewGuid():N}";
		try {
			string built = ToolText(await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
				new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["descriptor"] = ModifyBaseDescriptor(processName)
				}));
			ProcessDesignerE2ESupport.IgnoreWhenProcessBuilderIsBehind(built, MinimumPackageVersion);
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
			Dictionary<string, string?> directions = await DirectionsAsync(context, processName);

			// Assert
			modified.Should().Contain("edited (", because: "the edit must succeed: {0}", modified);
			directions["PNew"].Should().Be("Internal",
				because: "the request created it with no direction and an element output fills it");
			directions["PExisting"].Should().Be("Variable",
				because: "a parameter that existed before the request is never switched");
			modified.Should().Contain($"Process parameter 'PNew' {SwitchNotice}",
				because: "the switch is reported: {0}", modified);
			modified.Should().Contain($"Process parameter 'PExisting' {AdviceWarning}",
				because: "an existing Variable parameter the request made an element fill gets advice: {0}", modified);
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
