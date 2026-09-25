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
/// End-to-end coverage for the Script task element and the process-level usings (ENG-92711) over the real MCP
/// path. NOT in CI - run manually, gated on the <c>process-designer</c> feature and a reachable environment
/// carrying CrtProcessBuilder 1.6.6.30 or later.
/// <para>What these tests pin is what a caller can SEE: the element's build token and body, the usings as
/// describe reports them, and - the part the element changes for every caller - the compile signal. A process
/// with a script task needs a compile, so its result must carry the server's compile-required warning and
/// must NOT carry the compile-not-required note; an edit that leaves the C# alone gets the note back. The
/// compile and the run themselves are a stand leg (docs/script-task-element-capture.md in the package repo):
/// nothing here compiles the configuration, because that reloads the environment for every user. The
/// process-name compile is covered on the one path that compiles nothing - a process without C# - which proves
/// the route, the package gate and the answer.</para>
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreateBusinessProcessTool.CreateBusinessProcessToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class ScriptTaskElementToolE2ETests {

	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;
	private const string ModifyToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;
	private const string VersionToolName = ModifyProcessAsNewVersionTool.ModifyProcessAsNewVersionToolName;

	// The transport reports isError:null for a tool that ran and failed, so the exit code in the serialized
	// result is what says the call succeeded (the pattern ModifyProcessAsNewVersionToolE2ETests uses).
	private const string ExitCodeZero = "\\u0022exit-code\\u0022:0";

	/// <summary>The cut that builds the element; named in the skip message so a developer knows what to install.</summary>
	private const string MinimumPackageVersion = "1.6.6.30";

	/// <summary>The cut that ships the CompileProcess operation compile-creatio's process-name mode calls.</summary>
	private const string MinimumCompilePackageVersion = "1.6.6.32";

	private const string CompileToolName = CompileCreatioTool.CompileCreatioToolName;

	#region Methods: Tests

	[Test]
	[Description("Over the real MCP path, create-business-process builds a script task with process-level usings, and describe reads both back: the element resolves to the scripttask build token with its body verbatim and forInterpretedProcess true, and the usings - one of them aliased - come back in build shape.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process builds a script task and describe reads the body and usings back")]
	public async Task CreateBusinessProcess_Should_BuildScriptTaskWithUsings_AndReadThemBack() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("ScriptTask", MinimumPackageVersion);
		string processName = $"UsrClioBpScriptTaskE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = BuildDescriptor(processName)
			});

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "the build must not fail on the transport");
		JsonSerializer.Serialize(callResult).Should().Contain("created (UId:",
			because: "a script task with a body, a valid name and valid usings must build");
		JsonObject graph = DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName));
		JsonObject script = ElementNamed(graph, "CalcTotal");
		script["buildType"]!.GetValue<string>().Should().Be("scripttask",
			because: "the element round-trips to its own build token, not to the formula task that derives from it");
		script["scriptTask"]!["body"]!.GetValue<string>().Should().Contain("Get<int>(\"Amount\")",
			because: "the body is stored and reported verbatim");
		script["scriptTask"]!["forInterpretedProcess"]!.GetValue<bool>().Should().BeTrue(
			because: "clio builds the interpreted variant, the only one in which Get/Set compile");
		string usings = graph["usings"]!.ToJsonString();
		usings.Should().Contain("System.Linq", because: "a plain using is read back by namespace");
		usings.Should().Contain("\"alias\":\"SysSettings\"",
			because: "the alias is what makes the aliased type name compile, so it must survive the round trip");
	}

	[Test]
	[Description("A build that adds a script task carries the server's compile-required warning and NOT the compile-not-required note - the two cannot both be acted on, and the note used to be appended unconditionally.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process with a script task drops the compile-not-required note")]
	public async Task CreateBusinessProcess_WithAScriptTask_Should_CarryTheCompileWarningAndNotTheNote() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("ScriptTask", MinimumPackageVersion);
		string processName = $"UsrClioBpScriptTaskNoteE2e{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await ProcessDesignerE2EArrange.CallToolAsync(context, CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = BuildDescriptor(processName)
			});

		// Assert
		string resultJson = JsonSerializer.Serialize(callResult);
		resultJson.Should().Contain(CommandExecutionResult.CompileRequiredWarningMarker,
			because: "the server says the process cannot run until the configuration is compiled");
		resultJson.Should().NotContain(CommandExecutionResult.CompileNotRequiredNote,
			because: "a note saying the opposite of the server's warning is what sends an agent past the compile");
	}

	[Test]
	[Description("An edit that does not touch the C# gets the compile-not-required note back, while one that replaces the script body carries the compile-required warning instead.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process raises the compile demand only for an edit that changes C#")]
	public async Task ModifyBusinessProcess_Should_DemandACompile_OnlyWhenTheEditChangesCSharp() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("ScriptTask", MinimumPackageVersion);
		string processName = $"UsrClioBpScriptTaskModifyE2e{Guid.NewGuid():N}";
		string created = JsonSerializer.Serialize(await ProcessDesignerE2EArrange.CallToolAsync(context,
			CreateToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = BuildDescriptor(processName)
			}));
		created.Should().Contain("created (UId:", because: "the arrange step must have built the process");

		// Act
		string unrelated = JsonSerializer.Serialize(await ProcessDesignerE2EArrange.CallToolAsync(context,
			ModifyToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = """[{"op":"setElement","elementName":"CalcTotal","elementUpdate":{"useBackgroundMode":false}}]"""
			}));
		string bodyEdit = JsonSerializer.Serialize(await ProcessDesignerE2EArrange.CallToolAsync(context,
			ModifyToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = """[{"op":"setElement","elementName":"CalcTotal","elementUpdate":{"scriptTask":{"body":"Set(\"Total\", 3);\nreturn true;"}}}]"""
			}));

		// Assert
		unrelated.Should().Contain(CommandExecutionResult.CompileNotRequiredNote,
			because: "the note speaks for THIS edit, which changed no C#; the compile the create made owed is "
				+ "still owed, and the create's own warning said so");
		bodyEdit.Should().Contain(CommandExecutionResult.CompileRequiredWarningMarker,
			because: "a replaced body is new C#, and until a compile the environment runs the previous one");
		bodyEdit.Should().NotContain(CommandExecutionResult.CompileNotRequiredNote,
			because: "the note must never contradict the server's compile-required warning");
	}

	[Test]
	[Description("addUsing refuses an alias on a namespace the generated code already imports: the platform's generator drops such an entry together with its alias, so the alias would not exist at compile time.")]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process refuses an alias on a default namespace")]
	public async Task ModifyBusinessProcess_Should_RefuseAnAliasOnADefaultNamespace() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("ScriptTask", MinimumPackageVersion);
		string processName = $"UsrClioBpScriptTaskAliasE2e{Guid.NewGuid():N}";
		string created = JsonSerializer.Serialize(await ProcessDesignerE2EArrange.CallToolAsync(context,
			CreateToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = BuildDescriptor(processName)
			}));
		created.Should().Contain("created (UId:", because: "the arrange step must have built the process");

		// Act
		string resultJson = JsonSerializer.Serialize(await ProcessDesignerE2EArrange.CallToolAsync(context,
			ModifyToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = """[{"op":"addUsing","using":{"namespace":"Terrasoft.Core","alias":"Core"}}]"""
			}));

		// Assert
		resultJson.Should().NotContain(ExitCodeZero, because: "the refused edit must not report success");
		resultJson.Should().Contain("already imported by the generated process code",
			because: "the refusal says why the alias could not work, which a compile error would not");
		resultJson.Should().NotContain(CommandExecutionResult.CompileNotRequiredNote,
			because: "a refused edit saves nothing and must not carry a success-only note");
	}

	[Test]
	[Description("A new VERSION of a script-task process carries the compile-required warning and not the note, even when its batch changes no C#: a version is a new process name, so its generated code does not exist until the configuration is compiled.")]
	[AllureTag(VersionToolName)]
	[AllureName("modify-business-process-as-new-version of a script-task process demands a compile")]
	public async Task ModifyProcessAsNewVersion_OfAScriptTaskProcess_Should_DemandACompile() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("ScriptTask", MinimumPackageVersion);
		string processName = $"UsrClioBpScriptTaskVersionE2e{Guid.NewGuid():N}";
		string created = JsonSerializer.Serialize(await ProcessDesignerE2EArrange.CallToolAsync(context,
			CreateToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = BuildDescriptor(processName)
			}));
		created.Should().Contain("created (UId:", because: "the arrange step must have built the process");

		// Act
		string versioned = JsonSerializer.Serialize(await ProcessDesignerE2EArrange.CallToolAsync(context,
			VersionToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["package-name"] = "Custom",
				["operations"] = "[]"
			}));

		// Assert
		versioned.Should().Contain(ExitCodeZero, because: "a snapshot version of a valid process saves");
		versioned.Should().Contain(CommandExecutionResult.CompileRequiredWarningMarker,
			because: "the version's generated code does not exist until the configuration is compiled");
		versioned.Should().NotContain(CommandExecutionResult.CompileNotRequiredNote,
			because: "activating such a version without a compile refuses to start it");
	}

	[Test]
	[Description("Process methods travel with the build beside an interpreted script task and read back verbatim, and a setMethods edit carries the compile-required warning - the methods are new C# for the same generated class.")]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process builds process methods and setMethods demands a compile")]
	public async Task CreateAndModify_Should_CarryProcessMethods_AndDemandACompileForSetMethods() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("ScriptTask", MinimumPackageVersion);
		string processName = $"UsrClioBpScriptTaskMethodsE2e{Guid.NewGuid():N}";
		string created = JsonSerializer.Serialize(await ProcessDesignerE2EArrange.CallToolAsync(context,
			CreateToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = BuildDescriptor(processName, withMethods: true)
			}));
		created.Should().Contain("created (UId:", because: "the arrange step must have built the process");

		// Act
		JsonObject graph = DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName));
		string edited = JsonSerializer.Serialize(await ProcessDesignerE2EArrange.CallToolAsync(context,
			ModifyToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = """[{"op":"setMethods","methods":"private int Doubled(int value) => value * 3;"}]"""
			}));

		JsonObject regraph = DescribedProcessGraph.Read(await ProcessDesignerE2EArrange.DescribeAsync(context, processName));

		// Assert
		graph["methods"]?.GetValue<string>().Should().Be("private int Doubled(int value) => value * 2;",
			because: "the methods are stored and read back verbatim");
		edited.Should().Contain(ExitCodeZero, because: "replacing the methods is a valid edit");
		edited.Should().Contain(CommandExecutionResult.CompileRequiredWarningMarker,
			because: "new methods are new C# for the class the script tasks compile into");
		regraph["methods"]?.GetValue<string>().Should().Be("private int Doubled(int value) => value * 3;",
			because: "setMethods REPLACES the text, and an older server would drop it while answering success");
	}

	[Test]
	[Description("compile-creatio's process-name mode reaches the package's CompileProcess over the real MCP path, and for a process without C# it compiles NOTHING and says so - the leg that proves the route, the requirement gate and the answer without reloading the environment for every user.")]
	[AllureTag(CompileToolName)]
	[AllureName("compile-creatio process-name compiles nothing for a process without C#")]
	public async Task CompileCreatio_WithProcessName_ForAProcessWithoutCSharp_Should_CompileNothing() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("ScriptTask", MinimumCompilePackageVersion);
		string processName = $"UsrClioBpNoCodeCompileE2e{Guid.NewGuid():N}";
		string created = JsonSerializer.Serialize(await ProcessDesignerE2EArrange.CallToolAsync(context,
			CreateToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = BuildNoCodeDescriptor(processName)
			}));
		created.Should().Contain("created (UId:", because: "the arrange step must have built the process");

		// Act
		string compiled = JsonSerializer.Serialize(await ProcessDesignerE2EArrange.CallToolAsync(context,
			CompileToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName
			}));

		// Assert
		compiled.Should().Contain(ExitCodeZero, because: "a process without C# needs no compile, which is a success");
		compiled.Should().Contain("nothing was compiled",
			because: "the server's own predicate owes no compile, so none ran and the runtime was not reloaded");
	}

	[Test]
	[Description("compile-creatio refuses process-name together with package-name before anything reaches the environment: process-name already names the package it compiles.")]
	[AllureTag(CompileToolName)]
	[AllureName("compile-creatio refuses process-name with package-name")]
	public async Task CompileCreatio_WithProcessAndPackageName_Should_Refuse() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("ScriptTask", MinimumCompilePackageVersion);

		// Act
		string refused = JsonSerializer.Serialize(await ProcessDesignerE2EArrange.CallToolAsync(context,
			CompileToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["package-name"] = "Custom",
				["process-name"] = "UsrAnyProcess"
			}));

		// Assert
		refused.Should().NotContain(ExitCodeZero, because: "the two modes are exclusive");
		refused.Should().Contain("process-name", because: "the refusal names the argument to keep");
	}

	#endregion

	#region Methods: Private

	private static string BuildNoCodeDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP No Code Compile E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "Start1", "type": "startEvent" },
		    { "name": "End1", "type": "endEvent" }
		  ],
		  "flows": [ { "source": "Start1", "target": "End1" } ]
		}
		""";

	private static string BuildDescriptor(string processName, bool withMethods = false) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Script Task E2E",
		  "packageName": "Custom",
		  {{(withMethods ? "\"methods\": \"private int Doubled(int value) => value * 2;\"," : string.Empty)}}
		  "usings": [
		    { "namespace": "System.Linq" },
		    { "namespace": "Terrasoft.Configuration" },
		    { "namespace": "Terrasoft.Core.Configuration.SysSettings", "alias": "SysSettings" }
		  ],
		  "parameters": [
		    { "name": "Amount", "type": "Integer", "direction": "In" },
		    { "name": "Total", "type": "Integer", "direction": "Out" }
		  ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "CalcTotal", "type": "scriptTask", "caption": "Calculate total",
		      "scriptTask": { "body": "int amount = Get<int>(\"Amount\");\nSet(\"Total\", new[] { amount, amount }.Sum());\nreturn true;" } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "CalcTotal" },
		    { "source": "CalcTotal", "target": "EndEvent1" }
		  ]
		}
		""";

	/// <summary>The described element with that name, as the graph reports it.</summary>
	private static JsonObject ElementNamed(JsonObject graph, string name) =>
		graph["elements"]!.AsArray()
			.Select(element => element!.AsObject())
			.Single(element => element["name"]!.GetValue<string>() == name);

	#endregion

}
