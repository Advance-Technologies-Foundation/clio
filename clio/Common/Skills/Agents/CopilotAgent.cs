using Clio.Common.Skills;

namespace Clio.Common.Skills.Agents;

/// <summary>
/// GitHub Copilot CLI agent (<c>~/.copilot</c>). Installs via the Copilot plugin
/// marketplace; a pre-existing marketplace is detached with <c>--force</c> on conflict.
/// </summary>
public sealed class CopilotAgent(
	IFileSystem fileSystem,
	IUserHomeProvider homeProvider,
	IAgentCliRunner cli)
	: MarketplaceAgentBase(fileSystem, homeProvider, cli) {
	/// <inheritdoc />
	public override string AgentId => "copilot";

	/// <inheritdoc />
	public override string DisplayName => "GitHub Copilot CLI";

	/// <inheritdoc />
	protected override string CliName => "copilot";

	/// <inheritdoc />
	protected override string InstallVerb => "install";

	/// <inheritdoc />
	protected override string UninstallVerb => "uninstall";

	// Copilot rejects re-add with "already registered"; --force detaches installed plugins.
	/// <inheritdoc />
	protected override string[] MarketplaceRemoveFlags => ["--force"];

	/// <inheritdoc />
	public override AgentOutcome Install(AgentOperationContext context) =>
		SkipIfCliMissing() ?? Guarded(() => {
			CleanLegacyState();
			RegisterMarketplaceAndInstall(context.MarketplaceUrl);
			return AgentOutcome.Succeeded(AgentId, $"Installed {ToolkitDistribution.PluginName} for {DisplayName}.");
		});

	/// <inheritdoc />
	public override AgentOutcome Update(AgentOperationContext context) =>
		SkipIfCliMissing() ?? Guarded(() => {
			CleanLegacyState();
			RegisterMarketplaceAndInstall(context.MarketplaceUrl);
			return AgentOutcome.Succeeded(AgentId, $"Updated {ToolkitDistribution.PluginName} for {DisplayName}.");
		});

	/// <inheritdoc />
	public override AgentOutcome Delete(AgentOperationContext context) =>
		SkipIfCliMissing() ?? Guarded(() => {
			UninstallAndRemoveMarketplace();
			return AgentOutcome.Succeeded(AgentId, $"Removed {ToolkitDistribution.PluginName} from {DisplayName}.");
		});

	private void CleanLegacyState() {
		FileSystem.DeleteDirectoryIfExists(
			FileSystem.Combine(HomeProvider.GetAgentsDir(), "plugins", ToolkitDistribution.PluginName));
		FileSystem.DeleteDirectoryIfExists(
			FileSystem.Combine(HomeProvider.GetAgentsDir(), "skills", ToolkitDistribution.SkillName));
	}
}
