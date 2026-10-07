using System.Text.Json.Nodes;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Drives <c>new-ui-project</c> through the real MCP server into a temporary clio workspace and checks
/// which template a requested Creatio version selects (GitHub issue #1761).
/// </summary>
[TestFixture, Category("McpE2E.NoEnvironment"), AllureNUnit]
[AllureFeature(CreateUiProjectTool.CreateUiProjectToolName)]
[NonParallelizable]
public sealed class CreateUiProjectToolE2ETests : McpContractFixtureBase {

	private const string PackageName = "UsrI1761E2e";
	private const string VendorPrefix = "usr";

	private string _workspace = string.Empty;

	[SetUp]
	public async Task CreateWorkspaceAsync() {
		_workspace = Path.Combine(Path.GetTempPath(), $"clio-ui-project-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(Path.Combine(_workspace, ".clio"));
		await File.WriteAllTextAsync(Path.Combine(_workspace, ".clio", "workspaceSettings.json"),
			"{\"Packages\":[],\"ApplicationVersion\":\"10.0.0\"}");
	}

	[TearDown]
	public void DeleteWorkspace() {
		if (Directory.Exists(_workspace)) {
			Directory.Delete(_workspace, true);
		}
	}

	[Test]
	[Description("Scaffolds the current template for Creatio 10.0.0 with the empty flag, reports the template and SDK range it used, and points the build output at the hosting package.")]
	[AllureName("Creatio 10.0.0 selects the current UI project template")]
	public async Task Tool_Should_Use_Current_Template_When_Creatio10_Is_Requested() {
		// Arrange
		const string projectName = "i1761_current";
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(3));

		// Act
		CommandExecutionEnvelope execution = await CreateProjectAsync(projectName, "10.0.0", cancellation.Token);

		// Assert
		execution.ExitCode.Should().Be(0, because: "Creatio 10.0.0 maps to a shipped template");
		string projectPath = Path.Combine(_workspace, "projects", projectName);
		JsonNode packageJson = JsonNode.Parse(await File.ReadAllTextAsync(
			Path.Combine(projectPath, "package.json"), cancellation.Token))!;
		string devkitRange = packageJson["dependencies"]?["@creatio-devkit/common"]?.GetValue<string>() ?? string.Empty;
		devkitRange.Should().NotStartWith("^0.80",
			because: "Creatio 10.0.0 must not fall back to a legacy 8.0.x snapshot and its SDK line");
		execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Info
				&& message.Value != null
				&& message.Value.Contains("UI project template: ui-project-Empty (current template", StringComparison.Ordinal)
				&& message.Value.Contains("requested Creatio version: 10.0.0", StringComparison.Ordinal)
				&& message.Value.Contains($"@creatio-devkit/common: {devkitRange}.", StringComparison.Ordinal),
			because: "the caller must see which template and SDK range the requested version mapped to");
		packageJson["scripts"]?["clean"]?.GetValue<string>().Should().Contain(
			$"packages/{PackageName}/Files/src/js/{projectName}",
			because: "the bundle belongs in the hosting package's Files/src/js folder");
	}

	[Test]
	[Description("Scaffolds the closest legacy empty template for Creatio 8.2.0, reports it, and points its Angular build output at the hosting package instead of dist.")]
	[AllureName("Creatio 8.2.0 selects the legacy 8.0.10 empty template with package output")]
	public async Task Tool_Should_Use_Legacy_Template_With_Package_Output_When_Older_Creatio_Is_Requested() {
		// Arrange
		const string projectName = "i1761_legacy";
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(3));

		// Act
		CommandExecutionEnvelope execution = await CreateProjectAsync(projectName, "8.2.0", cancellation.Token);

		// Assert
		execution.ExitCode.Should().Be(0, because: "Creatio 8.2.0 maps to the 8.0.10 legacy template");
		execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Info
				&& message.Value != null
				&& message.Value.Contains("UI project template: ui/8.0.10/ui-project-Empty", StringComparison.Ordinal),
			because: "a legacy template must be named in the output instead of being selected silently");
		JsonNode angularJson = JsonNode.Parse(await File.ReadAllTextAsync(
			Path.Combine(_workspace, "projects", projectName, "angular.json"), cancellation.Token))!;
		string? outputPath = angularJson["projects"]?[projectName]?["architect"]?["build"]?["options"]?["outputPath"]
			?.GetValue<string>();
		outputPath.Should().Be($"../../packages/{PackageName}/Files/src/js/{projectName}",
			because: "the legacy empty template must emit into the hosting package like the full template");
	}

	[Test]
	[Description("Rejects an unparsable Creatio version with a message naming it, before any package or project is written.")]
	[AllureName("An invalid Creatio version is rejected without side effects")]
	public async Task Tool_Should_Reject_Invalid_Creatio_Version_Without_Writing_Anything() {
		// Arrange
		const string projectName = "i1761_invalid";
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(3));

		// Act
		CommandExecutionEnvelope execution = await CreateProjectAsync(projectName, "ten", cancellation.Token);

		// Assert
		execution.ExitCode.Should().NotBe(0, because: "'ten' is not a Creatio version");
		execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Error
				&& message.Value != null
				&& message.Value.Contains("Creatio version 'ten'", StringComparison.Ordinal),
			because: "the error must name the rejected value so the caller can correct it");
		Directory.Exists(Path.Combine(_workspace, "packages", PackageName)).Should().BeFalse(
			because: "an invalid version must fail before the hosting package is created");
		Directory.Exists(Path.Combine(_workspace, "projects", projectName)).Should().BeFalse(
			because: "an invalid version must fail before the project is scaffolded");
	}

	private async Task<CommandExecutionEnvelope> CreateProjectAsync(string projectName, string creatioVersion,
		CancellationToken cancellationToken) {
		CallToolResult callResult = await Session.CallToolAsync(
			CreateUiProjectTool.CreateUiProjectToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["workspaceDirectory"] = _workspace,
					["projectName"] = projectName,
					["packageName"] = PackageName,
					["vendorPrefix"] = VendorPrefix,
					["empty"] = true,
					["creatioVersion"] = creatioVersion
				}
			}, cancellationToken);
		return McpCommandExecutionParser.Extract(callResult);
	}
}
