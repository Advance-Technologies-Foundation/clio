using System.Xml.Linq;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture, Category("McpE2E.NoEnvironment"), AllureNUnit, NonParallelizable]
[AllureFeature(CreateTestProjectTool.ToolName)]
public sealed class CreateTestProjectToolE2ETests {
	[TestCase(false)]
	[TestCase(true)]
	[TestCase(null)]
	[Description("Creates or preserves unit-test projects through real MCP and verifies both solutions across multiple packages and reruns.")]
	[AllureName("Unit-test scaffolding registers every project and preserves reruns")]
	public async Task Tool_ShouldRegisterProjects_WhenScaffoldingOrRepairing(bool? existingProject) {
		// Arrange
		string workspace = Path.Combine(Path.GetTempPath(), $"clio unit e2e {Guid.NewGuid():N}");
		Directory.CreateDirectory(Path.Combine(workspace, ".clio"));
		Directory.CreateDirectory(Path.Combine(workspace, "tests", "Acme"));
		await File.WriteAllTextAsync(Path.Combine(workspace, ".clio", "workspaceSettings.json"),
			"{\"Packages\":[\"Acme\",\"Other\"],\"ApplicationVersion\":\"8.1.0\"}");
		if (existingProject.HasValue) {
			await File.WriteAllTextAsync(Path.Combine(workspace, "tests", "UnitTests.slnx"), "<Solution />");
			await File.WriteAllTextAsync(Path.Combine(workspace, "MainSolution.slnx"), "<Solution />");
		}
		foreach (string package in new[] { "Acme", "Other" }) {
			string directory = Path.Combine(workspace, "packages", package, "Files");
			Directory.CreateDirectory(directory);
			await File.WriteAllTextAsync(Path.Combine(directory, package + ".csproj"),
				"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
		}
		string projectPath = Path.Combine(workspace, "tests", "Acme", "Acme.Tests.csproj");
		string fixturePath = Path.Combine(workspace, "tests", "Acme", "BaseComposableAppTestFixture.cs");
		const string customizedProject = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><Description>Keep customizations</Description></PropertyGroup></Project>";
		if (existingProject == true) {
			await File.WriteAllTextAsync(projectPath, customizedProject);
			await File.WriteAllTextAsync(fixturePath, "// existing custom fixture");
			await File.WriteAllTextAsync(Path.Combine(workspace, "tests", "UnitTests.slnx"),
				"<Solution><Folder Name=\"/Tests/\"><Project Path=\"Acme/Acme.Tests.csproj\" /></Folder></Solution>");
		}
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string clioHome = Path.Combine(workspace, "clio-home");
		Directory.CreateDirectory(clioHome);
		await File.WriteAllTextAsync(Path.Combine(clioHome, "appsettings.json"),
			"{\"ActiveEnvironmentKey\":\"scaffold\",\"Autoupdate\":false,\"Environments\":{\"scaffold\":{\"Uri\":\"http://127.0.0.1:1\",\"IsNetCore\":true}}}");
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = clioHome;
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(3));
		try {
			await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellation.Token);
			IReadOnlyCollection<string> names = await session.ListReachableToolNamesAsync(cancellation.Token);
			names.Should().Contain(CreateTestProjectTool.ToolName, because: "agents must discover the existing scaffold through MCP");
			Dictionary<string, object?> args = new() {
				["args"] = new Dictionary<string, object?> {
					["package-name"] = "Acme,Other", ["workspace-path"] = workspace, ["environment-name"] = "scaffold"
				}
			};

			// Act
			for (int attempt = 0; attempt < 2; attempt++) {
				CallToolResult result = await session.CallToolAsync(CreateTestProjectTool.ToolName, args, cancellation.Token);
				CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(result);

				// Assert
				AllureApi.Step("Scaffolding succeeds with an Info message", () => {
					result.IsError.Should().NotBe(true, because: "valid scaffolding must not produce an MCP error");
					execution.ExitCode.Should().Be(0, because: "both packages must be registered before success");
					execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Info,
						because: "success must be visible to the calling agent");
				});
			}
			AllureApi.Step("Both slnx files contain each generated project exactly once", () => {
				foreach (string package in new[] { "Acme", "Other" }) {
					AssertProject(Path.Combine(workspace, "tests", "UnitTests.slnx"), Path.Combine(workspace, "tests", package, package + ".Tests.csproj"));
					AssertProject(Path.Combine(workspace, "tests", "UnitTests.slnx"), Path.Combine(workspace, "packages", package, "Files", package + ".csproj"));
					AssertProject(Path.Combine(workspace, "MainSolution.slnx"), Path.Combine(workspace, "tests", package, package + ".Tests.csproj"));
				}
			});
			if (existingProject == true) {
				File.ReadAllText(projectPath).Should().Be(customizedProject, because: "solution repair must preserve the user's project");
				File.ReadAllText(fixturePath).Should().Be("// existing custom fixture", because: "reruns must preserve custom fixture code");
			}

			// Arrange / Act: a malformed existing solution must not produce another successful scaffold.
			await File.WriteAllTextAsync(Path.Combine(workspace, "tests", "UnitTests.slnx"), "<WrongRoot />", cancellation.Token);
			CallToolResult failure = await session.CallToolAsync(CreateTestProjectTool.ToolName, args, cancellation.Token);
			CommandExecutionEnvelope failedExecution = McpCommandExecutionParser.Extract(failure);

			// Assert
			AllureApi.Step("Invalid solution reports failure with actionable Error output", () => {
				failure.IsError.Should().NotBe(true, because: "handled command failures use the execution envelope");
				failedExecution.ExitCode.Should().Be(1, because: "missing solution membership cannot be reported as success");
				failedExecution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Error && message.Value!.Contains("Repair the solution"),
					because: "the agent needs an actionable solution diagnostic");
				File.ReadAllText(Path.Combine(workspace, "tests", "UnitTests.slnx")).Should().Be("<WrongRoot />",
					because: "the failed update must preserve the malformed file for repair");
			});
		}
		finally {
			Directory.Delete(workspace, true);
		}
	}

	private static void AssertProject(string solution, string expectedProject) {
		XDocument document = XDocument.Load(solution);
		string[] projects = document.Descendants("Project").Select(element => Path.GetFullPath(
			Path.Combine(Path.GetDirectoryName(solution)!, element.Attribute("Path")!.Value.Replace('\\', Path.DirectorySeparatorChar)))).ToArray();
		projects.Should().ContainSingle(path => path == expectedProject,
			because: "each project must resolve from its solution and appear exactly once");
		File.Exists(expectedProject).Should().BeTrue(because: "solution entries must point to actual generated files");
	}
}
