using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the new-theme MCP tool.
/// </summary>
[TestFixture]
// [AllureNUnit] intentionally omitted — see CreateWorkspaceToolE2ETests for the deadlock rationale.
[AllureFeature(NewThemeTool.NewThemeToolName)]
[NonParallelizable]
public sealed class NewThemeToolE2ETests {
	private const string ToolName = NewThemeTool.NewThemeToolName;
	private const string CssClassName = "freedom-theme";
	private const string PackageName = "UsrThemes";

	[Test]
	[AllureTag(ToolName)]
	[AllureDescription("Starts the real clio MCP server and invokes new-theme inside a workspace, verifying theme.json and theme.css are scaffolded under the package from the shipped baseline template.")]
	[AllureName("New Theme Tool scaffolds theme.json and theme.css into the package")]
	public async Task NewTheme_Should_Scaffold_Theme_Files_In_Workspace() {
		// Arrange
		await using NewThemeArrangeContext context = await ArrangeAsync(createWorkspaceMarker: true);

		// Act
		NewThemeActResult actResult = await ActAsync(context);

		// Assert
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: $"a valid new-theme invocation should return a normal MCP result. Content: {DescribeCallResult(actResult.CallResult)}");
		actResult.Execution.ExitCode.Should().Be(0,
			because: "the underlying new-theme command should complete successfully in a workspace");

		string themeDirectory = Path.Combine(context.WorkspacePath, "packages", PackageName, "Files", "themes", CssClassName);
		File.Exists(Path.Combine(themeDirectory, "theme.json")).Should().BeTrue(
			because: "new-theme should scaffold theme.json under Files/themes/<cssClassName>/");
		File.Exists(Path.Combine(themeDirectory, "theme.css")).Should().BeTrue(
			because: "new-theme should scaffold theme.css under Files/themes/<cssClassName>/");
		File.ReadAllText(Path.Combine(themeDirectory, "theme.css")).Should()
			.Contain($".{CssClassName} {{", because: "the scaffolded css must be scoped under the theme class")
			.And.NotContain("<%", because: "all template tokens must be substituted in the scaffolded css");
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureDescription("Invokes new-theme against a directory that is not a clio workspace and verifies the tool reports failure without scaffolding any files.")]
	[AllureName("New Theme Tool rejects a non-workspace directory")]
	public async Task NewTheme_Should_Report_Failure_When_Directory_Is_Not_A_Workspace() {
		// Arrange
		await using NewThemeArrangeContext context = await ArrangeAsync(createWorkspaceMarker: false);

		// Act
		NewThemeActResult actResult = await ActAsync(context);

		// Assert
		bool failed = actResult.CallResult.IsError == true || actResult.Execution.ExitCode != 0;
		failed.Should().BeTrue(
			because: "new-theme should fail when the target directory is not a clio workspace");
		Directory.Exists(Path.Combine(context.WorkspacePath, "packages", PackageName)).Should().BeFalse(
			because: "no package should be scaffolded when the workspace check fails");
	}

	private static async Task<NewThemeArrangeContext> ArrangeAsync(bool createWorkspaceMarker) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		settings.ProcessEnvironmentVariables["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		string workspacePath = Path.Combine(Path.GetTempPath(), $"clio-new-theme-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(workspacePath);
		if (createWorkspaceMarker) {
			Directory.CreateDirectory(Path.Combine(workspacePath, ".clio"));
			await File.WriteAllTextAsync(Path.Combine(workspacePath, ".clio", "workspaceSettings.json"), "{}");
		}

		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new NewThemeArrangeContext(workspacePath, session, cancellationTokenSource);
	}

	private static async Task<NewThemeActResult> ActAsync(NewThemeArrangeContext context) {
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the new-theme MCP tool must be advertised by the server before the end-to-end call can be executed");

		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["workspaceDirectory"] = context.WorkspacePath,
					["cssClassName"] = CssClassName,
					["packageName"] = PackageName
				}
			},
			context.CancellationTokenSource.Token);

		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		return new NewThemeActResult(callResult, execution);
	}

	private static string DescribeCallResult(CallToolResult callResult) {
		if (callResult.Content is null || callResult.Content.Count == 0) {
			return "<no content>";
		}
		return string.Join(" | ", callResult.Content.Select(content => content?.ToString() ?? "<null>"));
	}

	private sealed record NewThemeArrangeContext(
		string WorkspacePath,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
			if (Directory.Exists(WorkspacePath)) {
				Directory.Delete(WorkspacePath, recursive: true);
			}
		}
	}

	private sealed record NewThemeActResult(
		CallToolResult CallResult,
		CommandExecutionEnvelope Execution);
}
