using System;
using System.Collections.Generic;
using System.Text.Json;
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
/// End-to-end coverage for ENG-101880 over the real MCP path: run-process runs EXACTLY the version whose code it
/// is given. NOT in CI - run manually against a reachable environment carrying CrtProcessBuilder 1.6.3.16 or later.
/// <para>Each version computes a different marker from the same input, so the value that comes back proves which
/// version ran - before the fix, the code of a new version silently ran the active one and answered its value.
/// Formula elements need no compile, so the stand is not reloaded. Every run creates two permanent schemas: the
/// platform has no operation that deletes a process version.</para>
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(RunProcessTool.ToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class RunProcessVersionE2ETests {

	/// <summary>The cut that builds the formula element; named in the skip message.</summary>
	private const string MinimumPackageVersion = "1.6.3.16";

	private const int Seed = 7;

	[Test]
	[Description("run-process launched with the code of a version that is not active runs THAT version - its marker comes back, not the active one's - names it, reports it as not active with the active version's code, and warns; the active version's own code runs the active version with no warning.")]
	[AllureTag(RunProcessTool.ToolName)]
	[AllureName("run-process runs the non-active version whose code it is given")]
	public async Task RunProcess_Should_RunTheNamedVersion_WhenItIsNotTheActiveOne() {
		// Arrange
		await using ProcessDesignerArrangeContext context =
			await ProcessDesignerE2EArrange.StartAsync("RunProcessVersion", MinimumPackageVersion);
		string processName = $"UsrClioRunVersionE2e{Guid.NewGuid():N}";
		string created = JsonSerializer.Serialize(await ProcessDesignerE2EArrange.CallToolAsync(context,
			CreateBusinessProcessTool.CreateBusinessProcessToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = BuildDescriptor(processName)
			}));
		created.Should().Contain("created (UId:", because: "the arrange step must have built the process: {0}", created);
		CallToolResult versioned = await ProcessDesignerE2EArrange.CallToolAsync(context,
			"modify-business-process-as-new-version",
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = processName,
				["operations"] = """
					[{ "op": "setElement", "elementName": "CalcMarker",
					   "elementUpdate": { "formula": { "body": "[#Seed#] + 200", "resultProcessParameter": "Marker" } } }]
					"""
			});
		string versionName = ProcessDesignerE2EArrange.CreatedVersionName(versioned);

		// Act
		RunProcessEnvelope newVersion = await RunAsync(context, versionName);
		RunProcessEnvelope root = await RunAsync(context, processName);

		// Assert
		newVersion.Error.Should().BeNull(because: $"the new version must launch cleanly. Error: {newVersion.Error}");
		Convert.ToInt32(newVersion.ResultParameterValues!["Marker"].ToString()).Should().Be(200 + Seed,
			because: "the new version's formula adds 200; the active root's 100 would mean the platform substituted "
				+ "the active version, which is the ENG-101880 defect");
		newVersion.VersionName.Should().Be(versionName, because: "the answer names the version that ran");
		newVersion.IsActiveVersion.Should().BeFalse(because: "a version created by a new-version save is inactive");
		newVersion.ActiveVersionName.Should().Be(processName,
			because: "the root is still the version the environment's own triggers start");
		newVersion.Warnings.Should().Contain(w => w.Contains("NOT the active version"),
			because: "a caller must hear that what ran is not what the environment runs");

		root.Error.Should().BeNull(because: $"the active root must launch cleanly. Error: {root.Error}");
		Convert.ToInt32(root.ResultParameterValues!["Marker"].ToString()).Should().Be(100 + Seed,
			because: "the active version's own code runs the active version, exactly as before");
		root.VersionName.Should().Be(processName, because: "the answer names the version that ran");
		root.IsActiveVersion.Should().BeTrue(because: "the root is the active member of this family");
		root.ActiveVersionName.Should().Be(processName, because: "the root is the version the environment runs");
		root.Warnings.Should().BeEmpty(because: "running the active version needs no caveat");
	}

	private static async Task<RunProcessEnvelope> RunAsync(ProcessDesignerArrangeContext context, string code) {
		CallToolResult result = await ProcessDesignerE2EArrange.CallToolAsync(context, RunProcessTool.ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = code,
				["parameters"] = new Dictionary<string, object?> { ["Seed"] = Seed },
				["result-parameters"] = new[] { "Marker" }
			});
		return EntitySchemaStructuredResultParser.Extract<RunProcessEnvelope>(result);
	}

	private static string BuildDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio run-process version E2E",
		  "packageName": "Custom",
		  "parameters": [
		    { "name": "Seed", "type": "Integer", "direction": "In" },
		    { "name": "Marker", "type": "Integer", "direction": "Out" }
		  ],
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "CalcMarker", "type": "formulaTask", "caption": "Calc marker",
		      "formula": { "body": "[#Seed#] + 100", "resultProcessParameter": "Marker" } },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "CalcMarker" },
		    { "source": "CalcMarker", "target": "EndEvent1" }
		  ]
		}
		""";
}
