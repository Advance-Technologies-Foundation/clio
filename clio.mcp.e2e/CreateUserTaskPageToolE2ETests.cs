using System.Text.Json;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>Exercises the Classic page scaffold through the real offline MCP server.</summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(CreateUserTaskPageTool.ToolName)]
public sealed class CreateUserTaskPageToolE2ETests : McpContractFixtureBase {
	/// <summary>Validates page/source association, directions, optional SVG slots and conflicting retries.</summary>
	[Test]
	[Description("Scaffolds a Classic page over MCP with native task association, input-only editors and all icon slots, refusing a conflicting retry.")]
	public async Task CreatePage_Should_WriteNativeArtifacts_WithoutEnvironment() {
		// Arrange
		string workspace = CreateFixtureDirectory("user-task-page");
		string package = Path.Combine(workspace, "packages", "UsrExample");
		Guid taskUid = Guid.NewGuid();
		Guid packageUid = Guid.NewGuid();
		await WriteAsync(Path.Combine(workspace, ".clio", "workspaceSettings.json"), new { Packages = new[] { "UsrExample" } });
		await WriteAsync(Path.Combine(package, "descriptor.json"), new { Descriptor = new { Name = "UsrExample", UId = packageUid } });
		await WriteAsync(Path.Combine(package, "Schemas", "UsrTask", "descriptor.json"), new {
			Descriptor = new { Name = "UsrTask", UId = taskUid, ManagerName = "ProcessUserTaskSchemaManager" }
		});
		string metadataPath = Path.Combine(package, "Schemas", "UsrTask", "metadata.json");
		await WriteAsync(metadataPath, new { MetaData = new { Schema = new {
			A2 = "UsrTask", UId = taskUid, B6 = packageUid, ManagerName = "ProcessUserTaskSchemaManager",
			FJ1 = new[] { new { A2 = "Input", UId = Guid.NewGuid(), L12 = 0 }, new { A2 = "Result", UId = Guid.NewGuid(), L12 = 1 } }
		} } });
		string icon = Path.Combine(workspace, "icon.svg");
		await File.WriteAllTextAsync(icon, "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 16 16\"><path d=\"M0 0L16 16\"/></svg>");
		Dictionary<string, object?> args = new() {
			["workspace-path"] = workspace, ["package-name"] = "UsrExample", ["user-task-uid"] = taskUid.ToString(),
			["page-name"] = "UsrTaskPage", ["caption"] = "Task parameters", ["culture"] = "en-US",
			["small-icon-path"] = icon, ["large-icon-path"] = icon, ["title-icon-path"] = icon
		};
		using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
		// Act & Assert
		await AllureApi.Step("Create the Classic page without an environment", async () => {
			var result = await Session.CallToolAsync(CreateUserTaskPageTool.ToolName, new Dictionary<string, object?> { ["args"] = args }, timeout.Token);
			result.IsError.Should().NotBeTrue(because: "valid local scaffolding must succeed over MCP");
			CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(result);
			execution.ExitCode.Should().Be(0, because: "the offline tool must generate the requested artifacts: {0}", string.Join(Environment.NewLine, execution.Output.Select(e => e.Value)));
			execution.Output.Should().Contain(e => e.MessageType == LogDecoratorType.Info, because: "the caller needs the created page path");
		});
		await AllureApi.Step("Verify native association, input mapping and icon resources", async () => {
			string body = await File.ReadAllTextAsync(Path.Combine(package, "Schemas", "UsrTaskPage", "UsrTaskPage.js"), timeout.Token);
			body.Should().Contain("InputMapping", because: "an input mapping editor must be generated");
			body.Should().NotContain("ResultMapping", because: "output-only parameters must not have input editors");
			using JsonDocument task = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath, timeout.Token));
			using JsonDocument page = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(package, "Schemas", "UsrTaskPage", "descriptor.json"), timeout.Token));
			task.RootElement.GetProperty("MetaData").GetProperty("Schema").GetProperty("FK11").GetGuid()
				.Should().Be(page.RootElement.GetProperty("Descriptor").GetProperty("UId").GetGuid(), because: "the task must link to the generated page identity");
			string resources = await File.ReadAllTextAsync(Path.Combine(package, "Resources", "UsrTask.ProcessUserTask", "resource.en-US.xml"), timeout.Token);
			foreach (string slot in new[] { "SmallSvgImage", "LargeSvgImage", "TitleSvgImage" }) {
				resources.Should().Contain(slot, because: "each requested icon belongs to the task's native resources");
			}
		});
		await AllureApi.Step("Refuse replacement while preserving the user's editable page", async () => {
			var before = Directory.GetFiles(package, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllText);
			var result = await Session.CallToolAsync(CreateUserTaskPageTool.ToolName, new Dictionary<string, object?> { ["args"] = args }, timeout.Token);
			CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(result);
			execution.ExitCode.Should().Be(1, because: "create must refuse an existing page association");
			execution.Output.Should().Contain(e => e.MessageType == LogDecoratorType.Error, because: "the conflict needs an actionable diagnostic");
			before.Keys.ToDictionary(p => p, File.ReadAllText).Should().BeEquivalentTo(before, because: "a refused retry must preserve every existing artifact");
		});
	}
	private static async Task WriteAsync(string path, object content) {
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		await File.WriteAllTextAsync(path, JsonSerializer.Serialize(content));
	}
}
