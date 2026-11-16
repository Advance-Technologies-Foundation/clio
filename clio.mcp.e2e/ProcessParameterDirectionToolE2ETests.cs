using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
/// element output mapped onto it, an expression mapping that reads an element output (a raw meta path: the mapping
/// expression route does not expand element names), or a Formula result. A stated direction
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
	[Description("create-business-process with three Text parameters: one declared with no direction that a Formula element fills, one with a stated Variable that a Formula fills, and one with no direction that only a constant fills. The first is saved Internal and named as switched once; the stated Variable keeps Variable and is advised once; the constant keeps Variable, silently.")]
	[AllureTag(CreateToolName)]
	[AllureName("a helper parameter declared with no direction is built Internal")]
	public async Task CreateBusinessProcess_Should_SaveOnlyTheDefaultedHelperInternal() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("Process parameter direction", MinimumPackageVersion);
		string processName = $"UsrClioBpDirection{Guid.NewGuid():N}";

		// Act
		string built = await BuildAsync(context, processName, BuildDescriptor(processName));
		// Before the try: when this fires clio refused the call, nothing was built, and the cleanup would only log.
		ProcessDesignerE2ESupport.IgnoreWhenProcessBuilderIsBehind(built, MinimumPackageVersion);

		try {
			// Assert
			// Checked before the read-back, so a refused build fails with its own refusal text.
			built.Should().Contain("created (UId:", because: "the build must succeed: {0}", built);
			Dictionary<string, string?> directions = await DirectionsAsync(context, processName);
			directions["PHelper"].Should().Be("Internal",
				because: "no direction was stated and only a Formula element of the process fills it");
			directions["PStated"].Should().Be("Variable", because: "a stated direction is the author's choice");
			directions["PConstant"].Should().Be("Variable",
				because: "a constant is a starting value, not a value an element of the process produces");
			built.Should().Contain($"Process parameter 'PHelper' {SwitchNotice}", Exactly.Once(),
				because: "the switch is reported once, with the way back: {0}", built);
			built.Should().NotContain($"Process parameter 'PHelper' {AdviceWarning}",
				because: "a switched helper is no longer Variable, so there is nothing to advise: {0}", built);
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
		string built = await BuildAsync(context, processName, FileCollectionDescriptor(processName));
		// Before the try: when this fires clio refused the call, nothing was built, and the cleanup would only log.
		ProcessDesignerE2ESupport.IgnoreWhenProcessBuilderIsBehind(built, MinimumPackageVersion);

		try {
			// Assert
			// Checked before the read-back, so a refused build fails with its own refusal text.
			built.Should().Contain("created (UId:", because: "the build must succeed: {0}", built);
			Dictionary<string, string?> directions = await DirectionsAsync(context, processName);
			directions["PDocs"].Should().Be("Variable",
				because: "a FileCollection keeps its Variable default even when an element fills it (owner decision D3)");
			await StoredValueAsync(context, processName, "PDocs");
			built.Should().NotContain($"Process parameter 'PDocs' {SwitchNotice}",
				because: "the builder does not switch a collection: {0}", built);
			built.Should().NotContain($"Process parameter 'PDocs' {AdviceWarning}",
				because: "a FileCollection is never advised: its Variable is the builder's own choice: {0}", built);
		} finally {
			await ProcessDesignerE2ESupport.DeleteProcessAsync(context.EnvironmentName, processName);
		}
	}

	[Test]
	[Description("modify-business-process on a process whose PExisting parameter was built with a stated Variable: one request maps an element output onto PExisting, adds PNew with no direction plus a mapping onto it, and adds PCopy with no direction plus an expression mapping that reads the contact name the Read data element found. PNew and PCopy are saved Internal with the switch notice once each; PExisting, which existed before the request, keeps Variable and the response advises it once, worded as a report that asks for its callers first.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify switches new defaulted helpers and advises on an existing Variable one")]
	public async Task ModifyBusinessProcess_Should_SwitchTheNewHelpersAndAdviseOnTheExistingOne() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("Process parameter direction", MinimumPackageVersion);
		string processName = $"UsrClioBpDirectionMod{Guid.NewGuid():N}";
		string built = await BuildAsync(context, processName, ModifyBaseDescriptor(processName));
		// Before the try: when this fires clio refused the call, nothing was built, and the cleanup would only log.
		ProcessDesignerE2ESupport.IgnoreWhenProcessBuilderIsBehind(built, MinimumPackageVersion);

		try {
			built.Should().Contain("created (UId:", because: "the arrange step must have built the process: {0}", built);
			string readNameMetaPath = await StoredValueAsync(context, processName, "PSeed");

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
						},
						new Dictionary<string, object?> {
							["op"] = "addParameter",
							["parameter"] = new Dictionary<string, object?> { ["name"] = "PCopy", ["type"] = "Text" }
						},
						new Dictionary<string, object?> {
							["op"] = "addMapping",
							["mapping"] = new Dictionary<string, object?> {
								["targetProcessParameter"] = "PCopy", ["expression"] = $"{readNameMetaPath} + \" (copy)\""
							}
						}
					})
				}));

			// Assert
			// Checked before the read-back, so a refused edit fails with its own refusal text.
			modified.Should().Contain("edited (", because: "the edit must succeed: {0}", modified);
			Dictionary<string, string?> directions = await DirectionsAsync(context, processName);
			directions["PNew"].Should().Be("Internal",
				because: "the request created it with no direction and an element output fills it");
			directions["PCopy"].Should().Be("Internal",
				because: "the request created it with no direction and an expression that reads an element output fills it");
			directions["PExisting"].Should().Be("Variable",
				because: "a parameter that existed before the request is never switched");
			modified.Should().Contain($"Process parameter 'PNew' {SwitchNotice}", Exactly.Once(),
				because: "the switch is reported once: {0}", modified);
			modified.Should().Contain($"Process parameter 'PCopy' {SwitchNotice}", Exactly.Once(),
				because: "the expression route is reported like a mapping: {0}", modified);
			modified.Should().NotContain($"Process parameter 'PNew' {AdviceWarning}",
				because: "a switched helper is no longer Variable, so there is nothing to advise: {0}", modified);
			modified.Should().Contain($"Process parameter 'PExisting' {AdviceWarning}", Exactly.Once(),
				because: "an existing Variable parameter the request made an element fill gets advice once: {0}", modified);
			NoticeAbout(modified, "PExisting").Should().Contain(CallersFirst,
				because: "other processes may call an existing parameter, so its advice asks for the callers first: {0}",
				modified);
			modified.Should().Contain(CallersFirst, Exactly.Once(),
				because: "only the parameter that existed before the request is reported that way: {0}", modified);
			modified.Should().NotContain($"Process parameter 'PCopy' {AdviceWarning}",
				because: "a switched helper is no longer Variable, so there is nothing to advise: {0}", modified);
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

	/// <summary>
	/// Runs the build. When the call itself throws, the server may still have saved the process, so it is deleted before
	/// the exception goes on; a call that returns hands its text to the skip check, which runs before any cleanup is
	/// scheduled.
	/// </summary>
	private static async Task<string> BuildAsync(ProcessDesignerArrangeContext context, string processName,
		string descriptor) {
		try {
			return ToolText(await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
				new Dictionary<string, object?> {
					["environment-name"] = context.EnvironmentName,
					["descriptor"] = descriptor
				}));
		} catch {
			await ProcessDesignerE2ESupport.DeleteProcessAsync(context.EnvironmentName, processName);
			throw;
		}
	}

	/// <summary>The process's parameters as describe reports them, by name.</summary>
	private static async Task<Dictionary<string, JsonObject>> ParametersAsync(ProcessDesignerArrangeContext context,
		string processName) {
		JsonObject graph = DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName));
		return graph["parameters"]!.AsArray().OfType<JsonObject>()
			.ToDictionary(parameter => parameter["name"]!.GetValue<string>());
	}

	private static async Task<Dictionary<string, string?>> DirectionsAsync(ProcessDesignerArrangeContext context,
		string processName) =>
		(await ParametersAsync(context, processName))
			.ToDictionary(parameter => parameter.Key, parameter => parameter.Value["direction"]?.GetValue<string>());

	/// <summary>
	/// The stored value of a process parameter, checked to read an element output: for a parameter mapped from one it is
	/// that output's raw meta path. The mapping <c>expression</c> route stores what it is given and does not expand
	/// element names, so an expression that reads an element must carry such a path.
	/// </summary>
	private static async Task<string> StoredValueAsync(ProcessDesignerArrangeContext context, string processName,
		string parameterName) {
		JsonObject parameter = (await ParametersAsync(context, processName))[parameterName];
		string? value = parameter["value"]?.GetValue<string>();
		value.Should().Contain("[Element:", because: "'{0}' was built mapped from an element output: {1}", parameterName,
			parameter.ToJsonString());
		return value!;
	}

	/// <summary>
	/// The advice about <paramref name="name"/> in a tool response: from its opening up to the end of that message. The
	/// advice for a parameter that existed before the request carries no double quote, so the message ends at the next one.
	/// </summary>
	private static string NoticeAbout(string toolText, string name) =>
		Regex.Match(toolText, $"Process parameter '{Regex.Escape(name)}' is 'Variable'[^\"]*").Value;

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
				new Dictionary<string, object> { ["name"] = "PExisting", ["type"] = "Text", ["direction"] = "Variable" },
				// Stated Internal, so it draws no notice: it only gives the modify a stored meta path to read.
				new Dictionary<string, object> { ["name"] = "PSeed", ["type"] = "Text", ["direction"] = "Internal" }
			},
			["elements"] = new object[] {
				new Dictionary<string, object> { ["name"] = "Start1", ["type"] = "startEvent" },
				ReadOneContact(),
				new Dictionary<string, object> { ["name"] = "End1", ["type"] = "endEvent" }
			},
			["flows"] = new[] { Flow("Start1", "ReadOne"), Flow("ReadOne", "End1") },
			["mappings"] = new object[] { NameOfTheReadContact("PSeed") }
		});

	private static Dictionary<string, object> Flow(string source, string target) =>
		new() { ["source"] = source, ["target"] = target };

	#endregion
}
