using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture, Category("McpE2E.NoEnvironment"), AllureNUnit]
[AllureFeature("new-integration-test-project")]
[NonParallelizable]
public sealed class CreateIntegrationTestProjectToolE2ETests {
	[Test]
	[Description("Starts the real MCP server, creates a portable integration-test project in a temporary clio workspace, and verifies its files and solution registrations.")]
	[AllureName("Integration-test scaffold is generated end to end")]
	public async Task Tool_Should_Generate_Project_Without_Environment() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(3));
		string workspace = Path.Combine(Path.GetTempPath(), $"clio-integration-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(Path.Combine(workspace, ".clio"));
		await File.WriteAllTextAsync(Path.Combine(workspace, ".clio", "workspaceSettings.json"),
			"{\"Packages\":[\"Acme\"],\"ApplicationVersion\":\"8.1.0\"}", cancellation.Token);
		try {
			await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellation.Token);

			// Act
			CallToolResult callResult = await session.CallToolAsync(
				"new-integration-test-project",
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["package-name"] = "Acme",
						["workspace-path"] = workspace,
						["target-framework"] = "net10.0"
					}
				}, cancellation.Token);
			CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

			// Assert
			execution.ExitCode.Should().Be(0, "because the real MCP tool should generate a valid local scaffold");
			string projectDirectory = Path.Combine(workspace, "tests", "Acme.IntegrationTests");
			File.Exists(Path.Combine(projectDirectory, "Acme.IntegrationTests.csproj")).Should().BeTrue(
				because: "the tool should materialize the project template");
			File.Exists(Path.Combine(projectDirectory, "Infrastructure", "CreatioTestSettings.cs")).Should().BeTrue(
				because: "the scaffold should include CI-friendly runtime settings");
			string integrationSolution = await File.ReadAllTextAsync(Path.Combine(workspace, "tests", "IntegrationTests.slnx"), cancellation.Token);
			integrationSolution.Should().Contain("Acme.IntegrationTests.csproj",
				because: "the integration solution should register the generated project");
			string mainSolution = await File.ReadAllTextAsync(Path.Combine(workspace, "MainSolution.slnx"), cancellation.Token);
			mainSolution.Should().Contain("Acme.IntegrationTests.csproj",
				because: "the workspace main solution should register the generated project");
		}
		finally {
			if (Directory.Exists(workspace)) {
				Directory.Delete(workspace, true);
			}
		}
	}
}
