using Clio.Common.Skills;

namespace Clio.Common.Skills.Agents;

/// <summary>
/// Claude Code agent (<c>~/.claude</c>). Installs via the Claude plugin
/// marketplace and enables marketplace auto-update.
/// </summary>
public sealed class ClaudeAgent(
	IFileSystem fileSystem,
	IUserHomeProvider homeProvider,
	IAgentCliRunner cli,
	IJsonConfigEditor jsonConfigEditor)
	: MarketplaceAgentBase(fileSystem, homeProvider, cli) {
	private readonly IJsonConfigEditor _jsonConfigEditor = jsonConfigEditor;

	/// <inheritdoc />
	public override string AgentId => "claude";

	/// <inheritdoc />
	public override string DisplayName => "Claude Code";

	/// <inheritdoc />
	protected override string CliName => "claude";

	/// <inheritdoc />
	protected override string InstallVerb => "install";

	/// <inheritdoc />
	protected override string UninstallVerb => "uninstall";

	// Claude's `plugin marketplace add` silently updates in place on re-add, so an
	// unconditional remove-then-add is the only way to migrate off legacy entries.
	/// <inheritdoc />
	protected override bool PreRemoveMarketplace => true;

	/// <inheritdoc />
	public override AgentOutcome Install(AgentOperationContext context) =>
		SkipIfCliMissing() ?? Guarded(() => {
			RegisterMarketplaceAndInstall(context.MarketplaceUrl);
			_jsonConfigEditor.EnableMarketplaceAutoUpdate(SettingsPath(), ToolkitDistribution.MarketplaceName);
			return AgentOutcome.Succeeded(AgentId, $"Installed {ToolkitDistribution.PluginName} for {DisplayName}.");
		});

	/// <inheritdoc />
	public override AgentOutcome Update(AgentOperationContext context) =>
		SkipIfCliMissing() ?? Guarded(() => {
			RunOrThrow("plugin", "update", ToolkitDistribution.PluginSource);
			_jsonConfigEditor.EnableMarketplaceAutoUpdate(SettingsPath(), ToolkitDistribution.MarketplaceName);
			return AgentOutcome.Succeeded(AgentId, $"Updated {ToolkitDistribution.PluginName} for {DisplayName}.");
		});

	/// <inheritdoc />
	public override AgentOutcome Delete(AgentOperationContext context) =>
		SkipIfCliMissing() ?? Guarded(() => {
			UninstallAndRemoveMarketplace();
			_jsonConfigEditor.RemoveMarketplaceFromSettings(SettingsPath(), ToolkitDistribution.MarketplaceName);
			return AgentOutcome.Succeeded(AgentId, $"Removed {ToolkitDistribution.PluginName} from {DisplayName}.");
		});

	private string SettingsPath() => FileSystem.Combine(AgentHome, "settings.json");
}
