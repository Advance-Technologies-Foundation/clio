using System.Text.Json;
using System.Text.Json.Nodes;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>Exercises runtime selection through a real MCP parent and its worker.</summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[NonParallelizable]
public sealed class McpWorkerRuntimeE2ETests {
	[Test]
	[Description("A real worker reaches the Creatio stub when both parent and worker require an explicit runtime roll-forward setting to start.")]
	[AllureFeature(PageListTool.ToolName)]
	[AllureTag(PageListTool.ToolName)]
	[AllureName("Worker inherits the runtime roll-forward policy")]
	public async Task Worker_ShouldReachCreatio_WhenParentRequiresRuntimeRollForward() {
		// Arrange
		string scratch = Path.Combine(Path.GetTempPath(), $"clio-runtime-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(scratch);
		try {
			string source = Path.GetDirectoryName(TestConfiguration.ResolveFreshClioProcessPath())!;
			string install = Path.Combine(scratch, "clio");
			CopyBuild(source, install);
			string configPath = Path.Combine(install, "clio.runtimeconfig.json");
			JsonNode config = JsonNode.Parse(await File.ReadAllTextAsync(configPath))!;
			JsonNode runtime = config["runtimeOptions"]!;
			// An unavailable minor in the previous major requires a major roll-forward on every
			// machine. Disable in the file makes losing the environment override fail deterministically.
			string requested = $"{Environment.Version.Major - 1}.999.0";
			foreach (JsonNode? framework in runtime["frameworks"]!.AsArray()) {
				framework!["version"] = requested;
			}
			runtime["rollForward"] = "Disable";
			await File.WriteAllTextAsync(configPath, config.ToJsonString());
			string home = Path.Combine(scratch, "home");
			Directory.CreateDirectory(home);
			await using CreatioWedgeStubServer stub = CreatioWedgeStubServer.Start();
			await File.WriteAllTextAsync(Path.Combine(home, "appsettings.json"), JsonSerializer.Serialize(new {
				ActiveEnvironmentKey = "runtime-probe",
				Environments = new Dictionary<string, object> {
					["runtime-probe"] = new { Uri = stub.BaseUrl, Login = "fixture", Password = "fixture", IsNetCore = false }
				}
			}));
			McpE2ESettings settings = TestConfiguration.Load();
			settings.ClioProcessPath = Path.Combine(install, "clio.dll");
			settings.ProcessEnvironmentVariables["CLIO_HOME"] = home;
			settings.ProcessEnvironmentVariables["DOTNET_ROLL_FORWARD"] = "LatestMajor";
			using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(2));
			await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellation.Token);

			// Act
			CallToolResult result = await session.CallToolAsync(PageListTool.ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> { ["environment-name"] = "runtime-probe" }
				}, cancellation.Token);

			// Assert
			await AllureApi.Step("The worker starts and executes the read against the stub", () => {
				result.IsError.Should().NotBeTrue(because: "losing the runtime policy must not cause a worker relay failure");
				string text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
				using JsonDocument payload = JsonDocument.Parse(text);
				payload.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(
					because: "a transport response alone does not establish successful tool execution");
				stub.SelectCount.Should().BeGreaterThan(0,
					because: "the worker must actually read from Creatio after its runtime starts");
				return Task.CompletedTask;
			});
		} finally {
			Directory.Delete(scratch, recursive: true);
		}
	}

	private static void CopyBuild(string source, string destination) {
		Directory.CreateDirectory(destination);
		foreach (string file in Directory.EnumerateFiles(source)) {
			File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
		}
		foreach (string directory in Directory.EnumerateDirectories(source)) {
			CopyBuild(directory, Path.Combine(destination, Path.GetFileName(directory)));
		}
	}
}
