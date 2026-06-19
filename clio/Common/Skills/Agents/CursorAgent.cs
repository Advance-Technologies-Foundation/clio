using Clio.Common.Skills;

namespace Clio.Common.Skills.Agents;

/// <summary>
/// Cursor agent (<c>~/.cursor</c>). Unlike the CLI agents, Cursor is installed by
/// copying the plugin runtime surface into <c>plugins/local</c>, merging the shared
/// <c>clio</c> MCP server into <c>mcp.json</c>, and writing the orchestrator rule.
/// </summary>
public sealed class CursorAgent(
	IFileSystem fileSystem,
	IUserHomeProvider homeProvider,
	ISkillRepositoryResolver repositoryResolver,
	ICursorPluginRuntimeInstaller runtimeInstaller,
	IJsonConfigEditor jsonConfigEditor,
	ICursorRuleWriter ruleWriter)
	: CodingAgentBase(fileSystem, homeProvider) {
	private readonly ISkillRepositoryResolver _repositoryResolver = repositoryResolver;
	private readonly ICursorPluginRuntimeInstaller _runtimeInstaller = runtimeInstaller;
	private readonly IJsonConfigEditor _jsonConfigEditor = jsonConfigEditor;
	private readonly ICursorRuleWriter _ruleWriter = ruleWriter;

	/// <inheritdoc />
	public override string AgentId => "cursor";

	/// <inheritdoc />
	public override string DisplayName => "Cursor";

	/// <inheritdoc />
	public override AgentOutcome Install(AgentOperationContext context) =>
		Guarded(() => {
			InstallOrUpdate(context);
			return AgentOutcome.Succeeded(AgentId, $"Installed {ToolkitDistribution.PluginName} for {DisplayName}.");
		});

	/// <inheritdoc />
	public override AgentOutcome Update(AgentOperationContext context) =>
		Guarded(() => {
			InstallOrUpdate(context);
			return AgentOutcome.Succeeded(AgentId, $"Updated {ToolkitDistribution.PluginName} for {DisplayName}.");
		});

	/// <inheritdoc />
	public override AgentOutcome Delete(AgentOperationContext context) =>
		Guarded(() => {
			FileSystem.DeleteDirectoryIfExists(LocalPluginDir());
			FileSystem.DeleteFileIfExists(RulePath());
			// Decision O1: leave the clio server in mcp.json (shared infrastructure).
			return AgentOutcome.Succeeded(AgentId, $"Removed {ToolkitDistribution.PluginName} from {DisplayName}.");
		});

	/// <summary>
	/// Runs <paramref name="body"/>, converting a known installer failure (missing
	/// release manifest, unreadable config, git/clone error) into a curated
	/// <see cref="AgentOutcomeStatus.Failed"/> outcome instead of leaking a raw exception.
	/// </summary>
	private AgentOutcome Guarded(System.Func<AgentOutcome> body) {
		try {
			return body();
		}
		catch (System.InvalidOperationException failure) {
			return AgentOutcome.Failed(AgentId, $"{DisplayName}: {failure.Message}");
		}
	}

	private void InstallOrUpdate(AgentOperationContext context) {
		bool useDefaultSource = string.IsNullOrWhiteSpace(context.RepositoryOverride);
		string sourceLocator = useDefaultSource ? ToolkitDistribution.MarketplaceGitUrl : context.RepositoryOverride.Trim();
		// Pin the default source to the release branch so Cursor's file-copy matches
		// the released plugin the CLI agents fetch. A custom --repo uses its default branch.
		string gitRef = useDefaultSource ? ToolkitDistribution.ReleaseRef : null;

		using ResolvedSkillRepository repository = _repositoryResolver.Resolve(sourceLocator, gitRef);
		string localPluginDir = LocalPluginDir();
		FileSystem.DeleteDirectoryIfExists(localPluginDir);
		_runtimeInstaller.Install(repository.RepositoryRootPath, localPluginDir);

		string mcpPath = McpConfigPath();
		_jsonConfigEditor.MergeClioMcpServer(mcpPath);
		_ruleWriter.WriteOrchestratorRule(RulePath(), localPluginDir, mcpPath);
	}

	private string LocalPluginDir() =>
		FileSystem.Combine(AgentHome, "plugins", "local", ToolkitDistribution.PluginName);

	private string McpConfigPath() => FileSystem.Combine(AgentHome, "mcp.json");

	private string RulePath() =>
		FileSystem.Combine(AgentHome, "rules", $"{ToolkitDistribution.SkillName}.mdc");
}
