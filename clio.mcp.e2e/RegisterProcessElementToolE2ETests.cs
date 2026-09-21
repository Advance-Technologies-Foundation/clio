using System.Text.Json;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>Exercises offline registration through the real MCP server.</summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(RegisterProcessElementTool.ToolName)]
public sealed class RegisterProcessElementToolE2ETests : McpContractFixtureBase {
	/// <summary>Checks ownership refusal, both database artifacts and stable retries.</summary>
	[Test]
	[Description("Real MCP registration creates both SQL dialects offline, rejects a foreign UId, and preserves identities on retry.")]
	public async Task RegisterProcessElement_Should_GenerateNativeArtifacts_WithoutEnvironment() {
		// Arrange
		string workspace = CreateFixtureDirectory("registration");
		string package = Path.Combine(workspace, "packages", "UsrExample");
		Guid taskUid = Guid.NewGuid();
		Guid packageUid = Guid.NewGuid();
		await WriteAsync(Path.Combine(workspace, ".clio", "workspaceSettings.json"), new { Packages = new[] { "UsrExample" } });
		await WriteAsync(Path.Combine(package, "descriptor.json"), new { Descriptor = new { Name = "UsrExample", UId = packageUid } });
		await WriteAsync(Path.Combine(package, "Schemas", "UsrTask", "descriptor.json"), new {
			Descriptor = new { Name = "UsrTask", UId = taskUid, ManagerName = "ProcessUserTaskSchemaManager" }
		});
		await WriteAsync(Path.Combine(package, "Schemas", "UsrTask", "metadata.json"), new {
			MetaData = new { Schema = new { A2 = "UsrTask", UId = taskUid, B6 = packageUid, ManagerName = "ProcessUserTaskSchemaManager" } }
		});
		using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
		Dictionary<string, object?> args = new() {
			["workspace-path"] = workspace, ["package-name"] = "UsrExample",
			["user-task-uid"] = Guid.NewGuid().ToString(), ["caption"] = "Format text"
		};
		// Act / Assert
		await AllureApi.Step("Reject an unowned task without writing SQL", async () => {
			CallToolResult result = await Session.CallToolAsync(RegisterProcessElementTool.ToolName,
				new Dictionary<string, object?> { ["args"] = args }, timeout.Token);
			CommandExecutionEnvelope rejected = McpCommandExecutionParser.Extract(result);
			rejected.ExitCode.Should().Be(1, because: "a foreign schema UId cannot be registered by this package");
			rejected.Output.Should().Contain(entry => entry.MessageType == LogDecoratorType.Error,
				because: "the caller needs an explicit ownership diagnostic");
			Directory.Exists(Path.Combine(package, "SqlScripts")).Should().BeFalse(
				because: "invalid input must not leave deployable artifacts");
		});
		args["user-task-uid"] = taskUid.ToString();
		await AllureApi.Step("Generate PostgreSQL and SQL Server after-package scripts", async () => {
			CallToolResult result = await Session.CallToolAsync(RegisterProcessElementTool.ToolName,
				new Dictionary<string, object?> { ["args"] = args }, timeout.Token);
			AssertSuccess(result);
			string[] scripts = Directory.GetFiles(Path.Combine(package, "SqlScripts"), "*.sql", SearchOption.AllDirectories);
			scripts.Should().HaveCount(2, because: "one registration artifact is required per supported database engine");
			foreach (string script in scripts) {
				string sql = await File.ReadAllTextAsync(script, timeout.Token);
				sql.Should().Contain(taskUid.ToString(), because: "the script must register the selected task");
				sql.Should().Contain("NOT EXISTS", because: "reinstallation must preserve an existing registration");
				using JsonDocument descriptor = JsonDocument.Parse(await File.ReadAllTextAsync(
					Path.Combine(Path.GetDirectoryName(script)!, "descriptor.json"), timeout.Token));
				descriptor.RootElement.GetProperty("SqlScript").GetProperty("InstallType").GetInt32().Should().Be(1,
					because: "registration needs schema installation to finish first");
			}
		});
		await AllureApi.Step("Repeat without changing script identities or content", async () => {
			string[] files = Directory.GetFiles(Path.Combine(package, "SqlScripts"), "*", SearchOption.AllDirectories);
			var before = files.ToDictionary(path => path, File.ReadAllText);
			AssertSuccess(await Session.CallToolAsync(RegisterProcessElementTool.ToolName,
				new Dictionary<string, object?> { ["args"] = args }, timeout.Token));
			files.ToDictionary(path => path, File.ReadAllText).Should().BeEquivalentTo(before,
				because: "MCP retries must preserve installed script identities, timestamps and captions");
		});
	}

	private static async Task WriteAsync(string path, object content) {
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		await File.WriteAllTextAsync(path, JsonSerializer.Serialize(content));
	}

	private static void AssertSuccess(CallToolResult result) {
		result.IsError.Should().NotBeTrue(because: "valid offline generation must succeed over MCP");
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(result);
		execution.ExitCode.Should().Be(0, because: "valid registration artifacts must be created: {0}",
			string.Join(Environment.NewLine, execution.Output.Select(entry => entry.Value)));
		execution.Output.Should().Contain(entry => entry.MessageType == LogDecoratorType.Info,
			because: "the caller needs the generated artifact paths");
	}
}
