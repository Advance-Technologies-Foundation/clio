using System.Text.Json;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Git;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Recreates the three-workspace Git lab and drives the real CLI and stdio MCP processes.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[Category("McpE2E.Manual")]
[Explicit("Developer-local merge lab: excluded from GitHub Actions and TeamCity; run explicitly to create temporary Git histories.")]
[AllureFeature(CreatioArtifactMergeTool.ToolName)]
[NonParallelizable]
// [AllureNUnit] is intentionally omitted. Each test performs many sequential process awaits while
// creating three Git workspaces, and fixture-level Allure bookkeeping can deadlock that pattern.
public sealed class CreatioArtifactMergeGitLabE2ETests : McpContractFixtureBase {
	private const string NumberTypeUId = "6b6b74e2-820d-490e-a017-2b73d4ccf2b0";
	private const string DateTimeTypeUId = "d21e9ef4-c064-4012-b286-fa1a8171da44";
	private const string ConflictQuestion =
		"Which type should UsrDeveloperAText keep: Number or Date/Time?";
	private string _isolatedClioHome = null!;

	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		TeamCityRunGuard.IgnoreIfRunningUnderTeamCityOrGitHubActions(
			"The Creatio merge Git lab is developer-local and must not run in GitHub Actions or TeamCity.");
		_isolatedClioHome = CreateIsolatedClioHome("{}", "creatio-merge-home");
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = _isolatedClioHome;
	}

	[Test]
	[Description("Recreates a real Git conflict in three workspaces, proves CLI and MCP expose the same alternatives, and proves neither preview mutates Git before the user decides.")]
	[AllureTag(CreatioArtifactMergeTool.ToolName)]
	[AllureName("Agent sees the true conflict and must ask before mutation")]
	[AllureDescription("Creates a temporary origin and three clones, invokes real CLI and stdio MCP processes against the conflicted Git stages, and proves the exact Number versus Date/Time question is available without repository mutation.")]
	public async Task MergeWorkflow_ShouldExposeQuestionWithoutMutation_WhenDevelopersChooseDifferentTypes() {
		// Arrange
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		CreatioMergeGitLab lab = await CreateLabAsync(cancellationTokenSource.Token);
		GitStateSnapshot before = await lab.CaptureStateAsync(cancellationTokenSource.Token);
		McpE2ESettings settings = ResolveSettings();

		// Act
		CreatioArtifactMergeResult cliResult = await RunCliMergeAsync(
			settings,
			lab,
			lab.MetadataStages,
			CreatioMergeGitLab.MetadataRelativePath,
			lab.DescriptorPath,
			expectedExitCode: 1,
			cancellationTokenSource.Token);
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		CallToolResult mcpCall = await context.Session.CallToolAsync(
			CreatioArtifactMergeTool.ToolName,
			CreateMcpRequest(lab),
			context.CancellationTokenSource.Token);
		CreatioArtifactMergeResult mcpResult = EntitySchemaStructuredResultParser
			.Extract<CreatioArtifactMergeResult>(mcpCall);
		GitStateSnapshot after = await lab.CaptureStateAsync(cancellationTokenSource.Token);

		// Assert
		mcpCall.IsError.Should().NotBeTrue(
			because: "a true semantic conflict is a normal domain result, not an MCP transport error");
		cliResult.Status.Should().Be("conflicts-remain",
			because: "the CLI must fail closed when both developers choose different types for one column");
		mcpResult.Status.Should().Be("conflicts-remain",
			because: "the agent-facing MCP call must expose the same decision boundary");
		mcpResult.Content.Should().Be(cliResult.Content,
			because: "CLI-first and MCP adapters must expose byte-identical conflict content");
		mcpResult.Report.Should().BeEquivalentTo(cliResult.Report,
			because: "both transports use the same semantic resolver and conflict report");
		mcpResult.Content.Should().Contain(NumberTypeUId,
			because: "the agent must see Developer A's Number alternative");
		mcpResult.Content.Should().Contain(DateTimeTypeUId,
			because: "the agent must see Developer B's Date/Time alternative");
		mcpResult.Content.Should().Contain("UsrDeveloperAText",
			because: "the conflicting column must be named in the proposed content");
		mcpResult.Content.Should().Contain("UsrDeveloperBNumber",
			because: "the unrelated developer column must remain preserved");
		mcpResult.Report.TrueConflicts.Should().Contain(path => path.EndsWith(".Body.S2", StringComparison.Ordinal),
			because: "the report must pinpoint the exact column type property that needs a user decision");
		mcpResult.Diagnostics.Should().Contain(ConflictQuestion,
			because: "the MCP result must give the agent the precise question to ask without decoding Creatio type UIds");
		cliResult.Diagnostics.Should().Contain(ConflictQuestion,
			because: "the CLI-first contract must expose the same human-readable decision");
		after.Should().BeEquivalentTo(before,
			because: "CLI and MCP are preview-only and must not write, stage, commit, or change conflict blobs");
	}

	[Test]
	[Description("Applies the user's Developer A answer to the resolver's conflict block and proves the merge commit retains Number rather than Date/Time.")]
	[AllureTag(CreatioArtifactMergeTool.ToolName)]
	[AllureName("Agent applies only the selected EntitySchema type")]
	[AllureDescription("Runs the true-conflict workflow, applies the user's Developer A choice, and proves Number is retained while Date/Time is absent from a clean two-parent merge.")]
	public async Task MergeWorkflow_ShouldKeepNumber_WhenUserChoosesDeveloperA() {
		// Arrange
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		CreatioMergeGitLab lab = await CreateLabAsync(cancellationTokenSource.Token);
		McpE2ESettings settings = ResolveSettings();
		CreatioArtifactMergeResult metadataResult = await RunCliMergeAsync(
			settings,
			lab,
			lab.MetadataStages,
			CreatioMergeGitLab.MetadataRelativePath,
			lab.DescriptorPath,
			expectedExitCode: 1,
			cancellationTokenSource.Token);
		string resolvedMetadata = SelectConflictAlternative(metadataResult.Content!, "Local");
		string metadataPath = ResolveWorkspacePath(lab.MainWorkspace, CreatioMergeGitLab.MetadataRelativePath);

		// Act
		await File.WriteAllTextAsync(metadataPath, resolvedMetadata, cancellationTokenSource.Token);
		await lab.RunGitAsync(
			["add", "--", CreatioMergeGitLab.MetadataRelativePath],
			cancellationTokenSource.Token);
		await lab.RunGitAsync(
			["commit", "-m", "merge: keep Developer A Number type"],
			cancellationTokenSource.Token);
		string committedMetadata = await lab.ReadGitAsync(
			["show", $"HEAD:{CreatioMergeGitLab.MetadataRelativePath}"],
			cancellationTokenSource.Token);
		string parents = await lab.ReadGitAsync(["show", "-s", "--format=%P", "HEAD"], cancellationTokenSource.Token);
		string status = await lab.ReadGitAsync(["status", "--porcelain=v1"], cancellationTokenSource.Token);
		string unmergedEntries = await lab.ReadGitAsync(["ls-files", "-u"], cancellationTokenSource.Token);

		// Assert
		CountOccurrences(committedMetadata, "UsrDeveloperAText").Should().Be(1,
			because: "the selected merge must retain the shared developer column exactly once");
		CountOccurrences(committedMetadata, "UsrDeveloperBNumber").Should().Be(1,
			because: "the unrelated developer column must remain exactly once");
		ReadColumnTypeUId(committedMetadata, "UsrDeveloperAText").Should().Be(NumberTypeUId,
			because: "the user's Developer A choice must retain Number on the conflicting column");
		committedMetadata.Should().NotContain(DateTimeTypeUId,
			because: "the rejected alternative must not leak into the committed artifact");
		committedMetadata.Should().NotContain("<<<<<<<", because: "the committed artifact must be marker-free");
		committedMetadata.Should().NotContain("=======", because: "the committed artifact must be marker-free");
		committedMetadata.Should().NotContain(">>>>>>>", because: "the committed artifact must be marker-free");
		parents.Split(' ', StringSplitOptions.RemoveEmptyEntries).Should().Equal(
			[lab.DeveloperACommit, lab.DeveloperBCommit],
			because: "the final commit must merge exactly the two simulated developer histories");
		unmergedEntries.Should().BeEmpty(because: "the selected result must clear every Git conflict stage");
		status.Should().BeEmpty(because: "the automated lab must finish with a clean integration workspace");
	}

	[Test]
	[Description("Preserves both independent column additions while applying Developer A's selected rename for a shared existing column.")]
	[AllureTag(CreatioArtifactMergeTool.ToolName)]
	[AllureName("Agent keeps both additions and Developer A rename")]
	[AllureDescription("Creates three Git workspaces from Creatio-authored fixtures, proves CLI and MCP expose one shared-column rename conflict without mutation, then applies Developer A's answer and commits both independent additions.")]
	public async Task MergeWorkflow_ShouldKeepBothAdditionsAndDeveloperARename_WhenUserChoosesDeveloperA() {
		// Arrange
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
		string fixtureRoot = Path.Combine(
			repositoryRoot,
			"lab",
			"creatio-three-way-merge",
			"fixtures",
			"entity-schema-rename-conflict");
		CreatioMergeGitLabScenario scenario = new(
			"developer-a-metadata.json",
			"developer-b-metadata.json",
			"developer A: add a column and rename the shared column",
			"developer B: add a column and rename the shared column");
		CreatioMergeGitLab lab = await CreatioMergeGitLab.CreateAsync(
			CreateFixtureDirectory("creatio-rename-merge-git-lab"),
			fixtureRoot,
			cancellationTokenSource.Token,
			scenario);
		GitStateSnapshot before = await lab.CaptureStateAsync(cancellationTokenSource.Token);
		McpE2ESettings settings = ResolveSettings();

		// Act
		CreatioArtifactMergeResult cliResult = await RunCliMergeAsync(
			settings,
			lab,
			lab.MetadataStages,
			CreatioMergeGitLab.MetadataRelativePath,
			lab.DescriptorPath,
			expectedExitCode: 1,
			cancellationTokenSource.Token);
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		CallToolResult mcpCall = await context.Session.CallToolAsync(
			CreatioArtifactMergeTool.ToolName,
			CreateMcpRequest(lab),
			context.CancellationTokenSource.Token);
		CreatioArtifactMergeResult mcpResult = EntitySchemaStructuredResultParser
			.Extract<CreatioArtifactMergeResult>(mcpCall);
		GitStateSnapshot afterPreview = await lab.CaptureStateAsync(cancellationTokenSource.Token);
		string resolvedMetadata = SelectConflictAlternative(mcpResult.Content!, "Local");
		string metadataPath = ResolveWorkspacePath(lab.MainWorkspace, CreatioMergeGitLab.MetadataRelativePath);
		await File.WriteAllTextAsync(metadataPath, resolvedMetadata, cancellationTokenSource.Token);
		await lab.RunGitAsync(["add", "--", CreatioMergeGitLab.MetadataRelativePath], cancellationTokenSource.Token);
		await lab.RunGitAsync(
			["commit", "-m", "merge: keep both additions and Developer A rename"],
			cancellationTokenSource.Token);
		string committedMetadata = await lab.ReadGitAsync(
			["show", $"HEAD:{CreatioMergeGitLab.MetadataRelativePath}"],
			cancellationTokenSource.Token);
		string parents = await lab.ReadGitAsync(["show", "-s", "--format=%P", "HEAD"], cancellationTokenSource.Token);
		string status = await lab.ReadGitAsync(["status", "--porcelain=v1"], cancellationTokenSource.Token);
		string unmergedEntries = await lab.ReadGitAsync(["ls-files", "-u"], cancellationTokenSource.Token);

		// Assert
		mcpCall.IsError.Should().NotBeTrue(
			because: "a semantic rename conflict is a domain result, not an MCP transport failure");
		cliResult.Status.Should().Be("conflicts-remain",
			because: "the CLI must not choose between two names for the same existing column");
		mcpResult.Status.Should().Be("conflicts-remain",
			because: "the agent-facing MCP call must expose the rename decision");
		mcpResult.Content.Should().Be(cliResult.Content,
			because: "CLI and MCP must expose byte-identical alternatives");
		mcpResult.Report.Should().BeEquivalentTo(cliResult.Report,
			because: "both entry points must report the same semantic conflict");
		mcpResult.Report.TrueConflicts.Should().Contain(path => path.EndsWith(".Body.A2", StringComparison.Ordinal),
			because: "the report must identify the existing column's name property as the only decision");
		mcpResult.Content.Should().Contain("UsrScenario2AName",
			because: "the agent must see Developer A's rename alternative");
		mcpResult.Content.Should().Contain("UsrScenario2BName",
			because: "the agent must see Developer B's rename alternative");
		mcpResult.Content.Should().Contain("UsrScenario2AAdded",
			because: "Developer A's independent addition must already be preserved");
		mcpResult.Content.Should().Contain("UsrScenario2BAdded",
			because: "Developer B's independent addition must already be preserved");
		afterPreview.Should().BeEquivalentTo(before,
			because: "CLI and MCP previews must not mutate the repository before the user decides");
		ReadColumnName(committedMetadata, "c066e869-c117-4780-84bb-fa428d00315b").Should().Be("UsrScenario2AName",
			because: "the user's Developer A choice must win for the shared existing column");
		CountOccurrences(committedMetadata, "UsrScenario2AAdded").Should().Be(1,
			because: "Developer A's new column must be retained exactly once");
		CountOccurrences(committedMetadata, "UsrScenario2BAdded").Should().Be(1,
			because: "Developer B's new column must be retained exactly once");
		committedMetadata.Should().NotContain("UsrScenario2BName",
			because: "the rejected rename must not leak into the final schema");
		parents.Split(' ', StringSplitOptions.RemoveEmptyEntries).Should().Equal(
			[lab.DeveloperACommit, lab.DeveloperBCommit],
			because: "the final commit must merge exactly the two simulated developer histories");
		unmergedEntries.Should().BeEmpty(because: "the selected result must clear every Git conflict stage");
		status.Should().BeEmpty(because: "the automated lab must finish with a clean integration workspace");
	}

	[Test]
	[Description("Preserves both independent additions while applying Developer B's rename when Developer A deleted the shared column.")]
	[AllureTag(CreatioArtifactMergeTool.ToolName)]
	[AllureName("Agent keeps both additions and Developer B rename over deletion")]
	[AllureDescription("Creates three Git workspaces from Creatio-authored delete-versus-rename fixtures, proves CLI and MCP expose the decision without mutation, then selects Developer B for every coordinated conflict block and commits the intended schema.")]
	public async Task MergeWorkflow_ShouldKeepBothAdditionsAndDeveloperBRename_WhenDeveloperADeletesSharedColumn() {
		// Arrange
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
		string fixtureRoot = Path.Combine(
			repositoryRoot,
			"lab",
			"creatio-three-way-merge",
			"fixtures",
			"entity-schema-delete-rename-conflict");
		CreatioMergeGitLabScenario scenario = new(
			"developer-a-metadata.json",
			"developer-b-metadata.json",
			"developer A: add a column and delete the shared column",
			"developer B: add a column and rename the shared column");
		CreatioMergeGitLab lab = await CreatioMergeGitLab.CreateAsync(
			CreateFixtureDirectory("creatio-delete-rename-merge-git-lab"),
			fixtureRoot,
			cancellationTokenSource.Token,
			scenario);
		GitStateSnapshot before = await lab.CaptureStateAsync(cancellationTokenSource.Token);
		McpE2ESettings settings = ResolveSettings();

		// Act
		CreatioArtifactMergeResult cliResult = await RunCliMergeAsync(
			settings,
			lab,
			lab.MetadataStages,
			CreatioMergeGitLab.MetadataRelativePath,
			lab.DescriptorPath,
			expectedExitCode: 1,
			cancellationTokenSource.Token);
		await using var context = Arrange(TimeSpan.FromMinutes(3));
		CallToolResult mcpCall = await context.Session.CallToolAsync(
			CreatioArtifactMergeTool.ToolName,
			CreateMcpRequest(lab),
			context.CancellationTokenSource.Token);
		CreatioArtifactMergeResult mcpResult = EntitySchemaStructuredResultParser
			.Extract<CreatioArtifactMergeResult>(mcpCall);
		GitStateSnapshot afterPreview = await lab.CaptureStateAsync(cancellationTokenSource.Token);
		string resolvedMetadata = SelectConflictAlternative(mcpResult.Content!, "Remote");
		string metadataPath = ResolveWorkspacePath(lab.MainWorkspace, CreatioMergeGitLab.MetadataRelativePath);
		await File.WriteAllTextAsync(metadataPath, resolvedMetadata, cancellationTokenSource.Token);
		await lab.RunGitAsync(["add", "--", CreatioMergeGitLab.MetadataRelativePath], cancellationTokenSource.Token);
		await lab.RunGitAsync(
			["commit", "-m", "merge: keep both additions and Developer B rename"],
			cancellationTokenSource.Token);
		string committedMetadata = await lab.ReadGitAsync(
			["show", $"HEAD:{CreatioMergeGitLab.MetadataRelativePath}"],
			cancellationTokenSource.Token);
		string parents = await lab.ReadGitAsync(["show", "-s", "--format=%P", "HEAD"], cancellationTokenSource.Token);
		string status = await lab.ReadGitAsync(["status", "--porcelain=v1"], cancellationTokenSource.Token);
		string unmergedEntries = await lab.ReadGitAsync(["ls-files", "-u"], cancellationTokenSource.Token);

		// Assert
		mcpCall.IsError.Should().NotBeTrue(
			because: "delete versus rename is a user decision, not an MCP transport failure");
		cliResult.Status.Should().Be("conflicts-remain",
			because: "the CLI must not silently choose deletion or rename");
		mcpResult.Status.Should().Be("conflicts-remain",
			because: "the agent-facing MCP result must require the same decision");
		mcpResult.Content.Should().Be(cliResult.Content,
			because: "CLI and MCP must expose byte-identical coordinated alternatives");
		mcpResult.Report.Should().BeEquivalentTo(cliResult.Report,
			because: "both entry points must report the same semantic conflicts");
		mcpResult.Report.TrueConflicts.Should().HaveCount(2,
			because: "the deleted item and its collection membership are one coordinated choice represented by two paths");
		CountOccurrences(mcpResult.Content!, "<<<<<<< Local").Should().Be(2,
			because: "the agent must receive selectable markers for both reported conflict paths");
		mcpResult.Content.Should().Contain("UsrScenario3AAdded",
			because: "Developer A's independent addition must already be preserved");
		mcpResult.Content.Should().Contain("UsrScenario3BAdded",
			because: "Developer B's independent addition must already be preserved");
		mcpResult.Content.Should().Contain("UsrScenario3BName",
			because: "the agent must see Developer B's rename alternative");
		afterPreview.Should().BeEquivalentTo(before,
			because: "CLI and MCP previews must not mutate Git before the user selects an outcome");
		ReadColumnName(committedMetadata, "c066e869-c117-4780-84bb-fa428d00315b").Should().Be("UsrScenario3BName",
			because: "the user's Developer B choice must retain the renamed shared column");
		CountOccurrences(committedMetadata, "UsrScenario3AAdded").Should().Be(1,
			because: "Developer A's new column must survive exactly once");
		CountOccurrences(committedMetadata, "UsrScenario3BAdded").Should().Be(1,
			because: "Developer B's new column must survive exactly once");
		CountOccurrences(committedMetadata, "bed10b34-7406-4f47-97d9-2f64fef0dcb8").Should().Be(2,
			because: "Developer A's column must have both an item and collection membership");
		CountOccurrences(committedMetadata, "65553d91-c21a-4614-a8f2-be429b5032fe").Should().Be(2,
			because: "Developer B's column must have both an item and collection membership");
		committedMetadata.Should().NotContain("UsrScenario2AName",
			because: "the old shared-column name must not leak into the selected schema");
		committedMetadata.Should().NotContain("<<<<<<<", because: "the committed artifact must be marker-free");
		committedMetadata.Should().NotContain("=======", because: "the committed artifact must be marker-free");
		committedMetadata.Should().NotContain(">>>>>>>", because: "the committed artifact must be marker-free");
		parents.Split(' ', StringSplitOptions.RemoveEmptyEntries).Should().Equal(
			[lab.DeveloperACommit, lab.DeveloperBCommit],
			because: "the final commit must merge exactly the two simulated developer histories");
		unmergedEntries.Should().BeEmpty(because: "the selected result must clear every Git conflict stage");
		status.Should().BeEmpty(because: "the automated lab must finish with a clean integration workspace");
	}

	private async Task<CreatioMergeGitLab> CreateLabAsync(CancellationToken cancellationToken) {
		string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
		string fixtureRoot = Path.Combine(
			repositoryRoot,
			"lab",
			"creatio-three-way-merge",
			"fixtures",
			"entity-schema-conflict");
		string labRoot = CreateFixtureDirectory("creatio-merge-git-lab");
		return await CreatioMergeGitLab.CreateAsync(labRoot, fixtureRoot, cancellationToken);
	}

	private McpE2ESettings ResolveSettings() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = string.IsNullOrWhiteSpace(settings.ClioProcessPath)
			? TestConfiguration.ResolveFreshClioProcessPath()
			: Path.GetFullPath(settings.ClioProcessPath);
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = _isolatedClioHome;
		return settings;
	}

	private static Dictionary<string, object?> CreateMcpRequest(CreatioMergeGitLab lab) => new() {
		["args"] = new Dictionary<string, object?> {
			["artifact-path"] = CreatioMergeGitLab.MetadataRelativePath,
			["base-content"] = File.ReadAllText(lab.MetadataStages.BasePath),
			["ours-content"] = File.ReadAllText(lab.MetadataStages.OursPath),
			["theirs-content"] = File.ReadAllText(lab.MetadataStages.TheirsPath),
			["descriptor-content"] = File.ReadAllText(lab.DescriptorPath)
		}
	};

	private static async Task<CreatioArtifactMergeResult> RunCliMergeAsync(
		McpE2ESettings settings,
		CreatioMergeGitLab lab,
		GitStageFiles stages,
		string artifactPath,
		string? descriptorPath,
		int expectedExitCode,
		CancellationToken cancellationToken) {
		List<string> arguments = [
			CreatioArtifactMergeTool.ToolName,
			"--artifact-path", artifactPath,
			"--base-file", stages.BasePath,
			"--ours-file", stages.OursPath,
			"--theirs-file", stages.TheirsPath
		];
		if (descriptorPath is not null) {
			arguments.Add("--descriptor-file");
			arguments.Add(descriptorPath);
		}

		ClioCliCommandResult commandResult = await ClioCliCommandRunner.RunAsync(
			settings,
			arguments,
			workingDirectory: lab.MainWorkspace,
			cancellationToken: cancellationToken);
		commandResult.ExitCode.Should().Be(expectedExitCode,
			because: $"the real CLI must return the expected domain exit code. stderr: {commandResult.StandardError}");
		CreatioArtifactMergeResult? result = JsonSerializer.Deserialize<CreatioArtifactMergeResult>(
			commandResult.StandardOutput);
		result.Should().NotBeNull(because: "the real CLI must return a structured merge result");
		return result!;
	}

	private static string SelectConflictAlternative(string content, string selectedMarkerSide) {
		List<string> lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
		bool selectedAny = false;
		while (true) {
			int start = lines.FindIndex(line => line.TrimStart().StartsWith("<<<<<<< Local", StringComparison.Ordinal));
			if (start < 0) {
				break;
			}
			int separator = lines.FindIndex(start + 1, line => line.Trim() == "=======");
			int end = lines.FindIndex(separator + 1, line => line.TrimStart().StartsWith(">>>>>>> Remote", StringComparison.Ordinal));
			if (separator < 0 || end < 0) {
				throw new InvalidOperationException("The Local/Remote conflict markers are incomplete.");
			}

			IEnumerable<string> selected = selectedMarkerSide switch {
				"Local" => lines.GetRange(start + 1, separator - start - 1),
				"Remote" => lines.GetRange(separator + 1, end - separator - 1),
				_ => throw new ArgumentOutOfRangeException(nameof(selectedMarkerSide), selectedMarkerSide, "Unknown conflict side.")
			};
			lines.RemoveRange(start, end - start + 1);
			lines.InsertRange(start, selected);
			selectedAny = true;
		}
		if (!selectedAny) {
			throw new InvalidOperationException("Expected at least one Local/Remote conflict block.");
		}
		return string.Join('\n', lines);
	}

	private static string ResolveWorkspacePath(string workspace, string relativePath) =>
		Path.Combine(workspace, relativePath.Replace('/', Path.DirectorySeparatorChar));

	private static string ReadColumnTypeUId(string content, string columnName) {
		string[] lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		int columnLine = Array.FindIndex(lines, line => line.Contains($"\"A2\": \"{columnName}\"", StringComparison.Ordinal));
		if (columnLine < 0) {
			throw new InvalidOperationException($"Column {columnName} was not found in merged metadata.");
		}
		int typeLine = Array.FindIndex(
			lines,
			columnLine + 1,
			line => line.TrimStart().StartsWith("\"S2\": \"", StringComparison.Ordinal));
		if (typeLine < 0) {
			throw new InvalidOperationException($"Column {columnName} has no S2 type in merged metadata.");
		}
		return lines[typeLine].Split('"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[2];
	}

	private static string ReadColumnName(string content, string columnUId) {
		string[] lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		int columnLine = Array.FindIndex(lines, line => line.Contains($"\"UId\": \"{columnUId}\"", StringComparison.Ordinal));
		if (columnLine < 0) {
			throw new InvalidOperationException($"Column {columnUId} was not found in merged metadata.");
		}
		int nameLine = Array.FindIndex(
			lines,
			columnLine + 1,
			line => line.TrimStart().StartsWith("\"A2\": \"", StringComparison.Ordinal));
		if (nameLine < 0) {
			throw new InvalidOperationException($"Column {columnUId} has no A2 name in merged metadata.");
		}
		return lines[nameLine].Split('"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[2];
	}

	private static int CountOccurrences(string content, string value) {
		int count = 0;
		int offset = 0;
		while ((offset = content.IndexOf(value, offset, StringComparison.Ordinal)) >= 0) {
			count++;
			offset += value.Length;
		}
		return count;
	}
}
