using System.Text.Json;
using System.Text.Json.Nodes;
using Allure.Net.Commons;
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

/// <summary>Exercises unlimited-text task parameters against an explicitly selected linked FSM workspace.</summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[Category("McpE2E.Manual")]
[Category("LocalOnly")]
[Explicit("Compiles and restarts the selected Creatio lab; run only against an exclusively owned instance.")]
[NonParallelizable]
[AllureNUnit]
[AllureFeature(CreateUserTaskTool.CreateUserTaskToolName)]
public sealed class UserTaskUnlimitedTextToolE2ETests : McpContractFixtureBase {
	/// <inheritdoc />
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		TeamCityRunGuard.IgnoreIfRunningUnderTeamCityOrGitHubActions(
			"This fixture compiles and restarts Creatio. Run it locally against an exclusively owned linked FSM lab.");
	}

	/// <summary>Verifies both authoring tools against native persisted metadata.</summary>
	[Test]
	[Description("Creates and modifies unlimited-text outputs through real MCP and verifies native exported metadata.")]
	[AllureTag(CreateUserTaskTool.CreateUserTaskToolName, ModifyUserTaskParametersTool.ModifyUserTaskParametersToolName)]
	public async Task UnlimitedText_Should_RoundTripThroughBothUserTaskTools() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		string? workspace = Environment.GetEnvironmentVariable("CLIO_E2E_USER_TASK_WORKSPACE");
		string? package = Environment.GetEnvironmentVariable("CLIO_E2E_USER_TASK_PACKAGE");
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Opt in with McpE2E__AllowDestructiveMcpTests for this local authoring/compile/restart test.");
		}
		settings.Sandbox.EnvironmentName.Should().NotBeNullOrWhiteSpace(because: "an opted-in destructive test requires a sandbox environment");
		workspace.Should().NotBeNullOrWhiteSpace(because: "CLIO_E2E_USER_TASK_WORKSPACE must name the linked FSM workspace");
		package.Should().NotBeNullOrWhiteSpace(because: "CLIO_E2E_USER_TASK_PACKAGE must name its existing editable package");
		string schemaName = "UsrUnlimitedText" + Guid.NewGuid().ToString("N");
		string processName = schemaName + "Process";
		bool processCreated = false;
		bool compilationMayBeRunning = false;
		string metadataPath = Path.Combine(workspace!, "packages", package!, "Schemas", schemaName, "metadata.json");
		Dictionary<string, object?> common = new() {
			["environment-name"] = settings.Sandbox.EnvironmentName,
			["workspace-path"] = workspace
		};
		using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(10));
		Exception? testFailure = null;
		try {
			// Act
			Dictionary<string, object?> create = new(common) {
				["code"] = schemaName, ["package-name"] = package, ["title"] = "Unlimited text test",
				["parameters"] = new[] { Parameter("ErrorMessage", "Unlimited text"), Parameter("PlainText", "Text") }
			};
			AssertSuccess(await CallStepAsync(CreateUserTaskTool.CreateUserTaskToolName,
				new Dictionary<string, object?> { ["args"] = create }, timeout.Token));
			using JsonDocument before = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath, timeout.Token));
			JsonElement original = FindParameter(before, "ErrorMessage");
			string? originalUid = original.GetProperty("UId").GetString();
			string unchangedMetadata = await File.ReadAllTextAsync(metadataPath, timeout.Token);
			Dictionary<string, object?> invalid = new(common) {
				["user-task-name"] = schemaName,
				["add-parameters"] = new[] { Parameter("Invalid", "NotADataType") }
			};
			CallToolResult rejected = await CallStepAsync(ModifyUserTaskParametersTool.ModifyUserTaskParametersToolName,
				new Dictionary<string, object?> { ["args"] = invalid }, timeout.Token);
			CommandExecutionEnvelope rejection = McpCommandExecutionParser.Extract(rejected);
			rejection.ExitCode.Should().NotBe(0,
				because: "unsupported parameter types must fail before saving any changes");
			rejection.Output.Should().Contain(entry => entry.MessageType == LogDecoratorType.Error,
				because: "a rejected type must explain its validation error to the caller");
			(await File.ReadAllTextAsync(metadataPath, timeout.Token)).Should().Be(unchangedMetadata,
				because: "invalid input must not alter the previously created parameters");
			Dictionary<string, object?> modify = new(common) {
				["user-task-name"] = schemaName,
				["add-parameters"] = new[] { Parameter("OtherError", "MaxSizeText") }
			};
			AssertSuccess(await CallStepAsync(ModifyUserTaskParametersTool.ModifyUserTaskParametersToolName,
				new Dictionary<string, object?> { ["args"] = modify }, timeout.Token));

			// Assert
			using JsonDocument after = JsonDocument.Parse(await File.ReadAllTextAsync(metadataPath, timeout.Token));
			foreach (string name in new[] { "ErrorMessage", "OtherError" }) {
				JsonElement parameter = FindParameter(after, name);
				parameter.GetProperty("L1").GetGuid().Should().Be(Guid.Parse("c0f04627-4620-4bc0-84e5-9419dc8516b1"),
					because: "the real designer must persist native MaxSizeText rather than ordinary Text");
				parameter.GetProperty("L12").GetInt32().Should().Be(1,
					because: "both creation and modification must preserve explicit Out direction");
			}
			FindParameter(after, "ErrorMessage").GetProperty("UId").GetString().Should().Be(originalUid,
				because: "adding another output must preserve existing parameter identities");
			FindParameter(after, "PlainText").GetProperty("L1").GetGuid().Should().Be(Guid.Parse("8b3f29bb-ea14-4ce5-a5c5-293a929b6ba2"),
				because: "adding unlimited text must not change the existing Text mapping");

			// Compile against the platform-generated string property and exercise its value through the process engine.
			JsonNode taskMetadata = JsonNode.Parse(await File.ReadAllTextAsync(metadataPath, timeout.Token))!;
			// Use the same partial-task source pattern as the reference: FK13 = IsPartial, FK16 = HasUserDefinedCode.
			taskMetadata["MetaData"]!["Schema"]!["FK13"] = true;
			taskMetadata["MetaData"]!["Schema"]!["FK16"] = true;
			await File.WriteAllTextAsync(metadataPath, taskMetadata.ToJsonString(), timeout.Token);
			string bodyPath = Path.Combine(Path.GetDirectoryName(metadataPath)!, schemaName + ".cs");
			await File.WriteAllTextAsync(bodyPath, $$"""
				namespace Terrasoft.Core.Process.Configuration {
				    public partial class {{schemaName}} {
				        protected override bool InternalExecute(ProcessExecutingContext context) {
				            ErrorMessage = new string('x', 12000);
				            return true;
				        }
				    }
				}
				""", timeout.Token);
			string descriptorPath = Path.Combine(Path.GetDirectoryName(metadataPath)!, "descriptor.json");
			JsonNode taskDescriptor = JsonNode.Parse(await File.ReadAllTextAsync(descriptorPath, timeout.Token))!;
			taskDescriptor["Descriptor"]!["ModifiedOnUtc"] = $"/Date({DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()})/";
			await File.WriteAllTextAsync(descriptorPath, taskDescriptor.ToJsonString(), timeout.Token);
			AssertSuccess(await CallStepAsync(LoadPackagesTool.LoadPackagesToDbToolName,
				new Dictionary<string, object?> { ["environmentName"] = settings.Sandbox.EnvironmentName }, timeout.Token));
			// The runtime schema manager can retain the original task even while designer GetSchema sees the imported metadata.
			AssertSuccess(await CallStepAsync(RestartTool.RestartByEnvironmentNameToolName,
				new Dictionary<string, object?> { ["environmentName"] = settings.Sandbox.EnvironmentName,
					["waitReady"] = true, ["waitTimeoutSeconds"] = 120 }, timeout.Token));
			// Explicit generation is needed after FSM edits; GenerateModified can leave an existing task's source unchanged.
			string generationResponse = Path.Combine(CreateFixtureDirectory("task-source"), "response.json");
			string generationRequest = JsonSerializer.Serialize(new[] {
				taskMetadata["MetaData"]!["Schema"]!["UId"]!.GetValue<string>()
			});
			ClioCliCommandResult generated = await ClioCliCommandRunner.RunAsync(settings, [
				"call-service", "-e", settings.Sandbox.EnvironmentName!, "--service-path",
				"ServiceModel/WorkspaceExplorerService.svc/GenerateSchemasSources", "--body", generationRequest,
				"--destination", generationResponse
			], cancellationToken: timeout.Token);
			generated.ExitCode.Should().Be(0, because: "native source generation must succeed: {0} {1}",
				generated.StandardOutput, generated.StandardError);
			using JsonDocument generation = JsonDocument.Parse(await File.ReadAllTextAsync(generationResponse, timeout.Token));
			generation.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(
				because: "the platform must regenerate the task from its updated metadata before compilation");
			compilationMayBeRunning = true;
			AssertSuccess(await CallStepAsync(CompileCreatioTool.CompileCreatioToolName,
				new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?> {
					["environment-name"] = settings.Sandbox.EnvironmentName
				} }, timeout.Token));
			// An accepted compile can outlive the MCP response deadline; wait before reading sources or restarting.
			while (true) {
				using JsonDocument status = ReadPayload(await CallStepAsync(CompileStatusTool.CompileStatusToolName,
					new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?> {
						["environment-name"] = settings.Sandbox.EnvironmentName
					} }, timeout.Token));
				string? state = status.RootElement.GetProperty("status").GetString();
				if (state != "running") {
					compilationMayBeRunning = state is not ("succeeded" or "failed");
					state.Should().Be("succeeded", because: "native compilation must finish successfully before execution: {0}",
						status.RootElement.GetRawText());
					break;
				}
				await Task.Delay(TimeSpan.FromSeconds(5), timeout.Token);
			}
			string generatedPath = Path.Combine(workspace!, "packages", package!, "Autogenerated", "Src",
				schemaName + "Schema." + package + ".cs");
			string generatedSource = await File.ReadAllTextAsync(generatedPath, timeout.Token);
			generatedSource.Should().Contain("public virtual string ErrorMessage",
				because: "native MaxSizeText generation must expose a CLR string property");
			generatedSource.Should().Contain("public partial class " + schemaName,
				because: "generation must consume the newly imported partial-task metadata rather than its original cached form");
			generatedSource.Should().Contain("public virtual string OtherError",
				because: "generation must include the parameter added through modify-user-task-parameters");
			// Package compilation writes the assembly; the running application may still have its previous version loaded.
			AssertSuccess(await CallStepAsync(RestartTool.RestartByEnvironmentNameToolName,
				new Dictionary<string, object?> { ["environmentName"] = settings.Sandbox.EnvironmentName,
					["waitReady"] = true, ["waitTimeoutSeconds"] = 120 }, timeout.Token));
			string descriptor = JsonSerializer.Serialize(new {
				name = processName, caption = "Unlimited text round trip", packageName = package,
				elements = new object[] {
					new { name = "Start", type = "startEvent" },
					new { name = "Task", type = "userTask", userTaskName = schemaName },
					new { name = "End", type = "endEvent" }
				},
				flows = new[] { new { source = "Start", target = "Task" }, new { source = "Task", target = "End" } },
				parameters = new[] { new { name = "Message", caption = "Message", direction = "Out", typeFromElement = "Task", typeFromElementParameter = "ErrorMessage" } },
				mappings = new[] { new { targetProcessParameter = "Message", sourceElement = "Task", sourceElementParameter = "ErrorMessage" } }
			});
			CallToolResult built = await CallStepAsync(CreateBusinessProcessTool.CreateBusinessProcessToolName,
				new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?> {
					["environment-name"] = settings.Sandbox.EnvironmentName, ["descriptor"] = descriptor
				} }, timeout.Token);
			AssertSuccess(built);
			processCreated = true;
			CallToolResult ran = await CallStepAsync(RunProcessTool.ToolName,
				new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?> {
					["environment-name"] = settings.Sandbox.EnvironmentName, ["process-name"] = processName,
					["result-parameters"] = new[] { "Message" }
				} }, timeout.Token);
			using JsonDocument runPayload = ReadPayload(ran);
			runPayload.RootElement.GetProperty("status").GetString().Should().Be("completed",
				because: "the newly compiled user task must execute successfully in the real process engine: {0}",
				runPayload.RootElement.GetRawText());
			string? message = runPayload.RootElement.GetProperty("resultParameterValues").GetProperty("Message").GetString();
			message.Should().HaveLength(12000, because: "unlimited-text output must survive process mapping without truncation");
			message.Should().MatchRegex("^x+$", because: "every character must survive the round trip unchanged");
		} catch (Exception exception) {
			testFailure = exception;
			throw;
		} finally {
			if (compilationMayBeRunning) {
				await TestContext.Error.WriteLineAsync($"Compilation completion is unknown; retained test schemas {schemaName} and {processName} in {package}. Confirm compilation has stopped before deleting their source files.");
			}
			List<Exception> cleanupFailures = [];
			string processMetadataPath = Path.Combine(workspace!, "packages", package!, "Schemas", processName, "metadata.json");
			if (!compilationMayBeRunning && (processCreated || File.Exists(processMetadataPath))) {
				await TryCleanupSchemaAsync(processName);
			}
			// Clean up only the uniquely named schema owned by this test, including partially completed creation.
			if (!compilationMayBeRunning && File.Exists(metadataPath)) {
				await TryCleanupSchemaAsync(schemaName);
			}
			if (cleanupFailures.Count > 0) {
				if (testFailure is null) {
					throw new AggregateException("Test schema cleanup failed.", cleanupFailures);
				}
				await TestContext.Error.WriteLineAsync(string.Join(Environment.NewLine, cleanupFailures));
			}

			async Task TryCleanupSchemaAsync(string name) {
				try {
					using CancellationTokenSource cleanup = new(TimeSpan.FromMinutes(3));
					Dictionary<string, object?> delete = new(common) { ["schema-name"] = name };
					AssertSuccess(await CallStepAsync(DeleteSchemaTool.DeleteSchemaToolName,
						new Dictionary<string, object?> { ["args"] = delete }, cleanup.Token));
					// Native Delete leaves FSM files behind; remove only this test's successfully deleted schema.
					DeleteOwnedSource(workspace!, package!, name);
				} catch (Exception exception) {
					cleanupFailures.Add(exception);
				}
			}
		}
	}

	private Task<CallToolResult> CallStepAsync(string name, Dictionary<string, object?> arguments,
		CancellationToken cancellationToken) =>
		AllureApi.Step("Invoke " + name, async () =>
			await Session.CallToolAsync(name, arguments, cancellationToken));
	private static JsonDocument ReadPayload(CallToolResult result) =>
		JsonDocument.Parse(result.Content.OfType<TextContentBlock>().First().Text);

	private static void DeleteOwnedSource(string workspace, string package, string schemaName) {
		foreach (string area in new[] { "Schemas", "Resources" }) {
			string ownedDirectory = Path.GetFullPath(Path.Combine(workspace, "packages", package, area, schemaName));
			string parentDirectory = Path.GetFullPath(Path.Combine(workspace, "packages", package, area)) + Path.DirectorySeparatorChar;
			if (!ownedDirectory.StartsWith(parentDirectory, StringComparison.Ordinal)) {
				throw new InvalidOperationException("Test cleanup path escaped its package.");
			}
			if (Directory.Exists(ownedDirectory)) {
				Directory.Delete(ownedDirectory, recursive: true);
			}
		}
		string generatedSource = Path.Combine(workspace, "packages", package, "Autogenerated", "Src",
			schemaName + "Schema." + package + ".cs");
		if (File.Exists(generatedSource)) {
			File.Delete(generatedSource);
		}
	}

	private static Dictionary<string, object?> Parameter(string name, string type) => new() {
		["code"] = name, ["title"] = name, ["type"] = type,
		["direction"] = "Out", ["resulting"] = true, ["serializable"] = true
	};

	private static JsonElement FindParameter(JsonDocument document, string name) =>
		document.RootElement.GetProperty("MetaData").GetProperty("Schema").GetProperty("FJ1")
			.EnumerateArray().Single(parameter => parameter.GetProperty("A2").GetString() == name);

	private static void AssertSuccess(CallToolResult result) {
		result.IsError.Should().NotBeTrue(because: "the real MCP invocation must succeed");
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(result);
		execution.ExitCode.Should().Be(0, because: "the native operation must complete successfully: {0}",
			string.Join(Environment.NewLine, execution.Output?.Select(message => message.Value) ?? []));
		execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Info,
			because: "successful authoring must report its completed operation");
	}
}
