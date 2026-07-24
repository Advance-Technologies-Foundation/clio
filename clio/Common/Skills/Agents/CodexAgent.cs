using System.IO;
using System.IO.Abstractions;
using Clio.Common.Skills;

namespace Clio.Common.Skills.Agents;

/// <summary>
/// Codex agent (<c>~/.codex</c>). Installs via the Codex plugin marketplace,
/// cleans up legacy file-copy state, and merges the shared <c>clio</c> MCP server
/// into <c>config.toml</c> (Codex does not auto-promote plugin-bundled MCP entries).
/// </summary>
public sealed class CodexAgent(
	IFileSystem fileSystem,
	IUserHomeProvider homeProvider,
	IAgentCliRunner cli,
	ICodexTomlConfigEditor tomlConfigEditor,
	IJsonConfigEditor jsonConfigEditor)
	: MarketplaceAgentBase(fileSystem, homeProvider, cli) {
	private readonly ICodexTomlConfigEditor _tomlConfigEditor = tomlConfigEditor;
	private readonly IJsonConfigEditor _jsonConfigEditor = jsonConfigEditor;

	/// <inheritdoc />
	public override string AgentId => "codex";

	/// <inheritdoc />
	public override string DisplayName => "Codex";

	/// <inheritdoc />
	protected override string CliName => "codex";

	/// <inheritdoc />
	protected override string InstallVerb => "add";

	/// <inheritdoc />
	protected override string UninstallVerb => "remove";

	/// <inheritdoc />
	protected override bool PreRemoveMarketplace => true;

	/// <inheritdoc />
	public override AgentOutcome Install(AgentOperationContext context) =>
		SkipIfCliMissing() ?? Guarded(() => {
			InstallOrUpdate(context);
			return AgentOutcome.Succeeded(AgentId, $"Installed {ToolkitDistribution.PluginName} for {DisplayName}.");
		});

	/// <inheritdoc />
	public override AgentOutcome Update(AgentOperationContext context) =>
		SkipIfCliMissing() ?? Guarded(() => {
			InstallOrUpdate(context);
			return AgentOutcome.Succeeded(AgentId, $"Updated {ToolkitDistribution.PluginName} for {DisplayName}.");
		});

	/// <inheritdoc />
	public override AgentOutcome Delete(AgentOperationContext context) =>
		SkipIfCliMissing() ?? Guarded(() => {
			UninstallAndRemoveMarketplace();
			// Decision O1: leave [mcp_servers.clio] in config.toml (shared infrastructure).
			return AgentOutcome.Succeeded(AgentId, $"Removed {ToolkitDistribution.PluginName} from {DisplayName}.");
		});

	private void InstallOrUpdate(AgentOperationContext context) {
		CleanLegacyState();
		RegisterMarketplaceAndInstall(context.MarketplaceUrl);
		_tomlConfigEditor.MergeClioMcpServer(ConfigPath());
	}

	/// <summary>
	/// Removes on-disk and config artifacts left by the legacy file-copy installer
	/// so a fresh install matches a clean state.
	/// </summary>
	private void CleanLegacyState() {
		string marketplace = ToolkitDistribution.MarketplaceName;
		DeleteLegacyDirectoryIfExists(FileSystem.Combine(AgentHome, "plugins", "marketplaces", marketplace));
		DeleteLegacyDirectoryIfExists(FileSystem.Combine(AgentHome, "plugins", "cache", marketplace));
		DeleteLegacyDirectoryIfExists(FileSystem.Combine(HomeProvider.GetAgentsDir(), "plugins", ToolkitDistribution.PluginName));
		DeleteLegacyDirectoryIfExists(FileSystem.Combine(AgentHome, "skills", ToolkitDistribution.SkillName));

		_jsonConfigEditor.RemovePersonalMarketplacePluginEntry(
			FileSystem.Combine(HomeProvider.GetAgentsDir(), "plugins", "marketplace.json"),
			marketplace,
			ToolkitDistribution.PluginName);

		string configPath = ConfigPath();
		_tomlConfigEditor.RemoveMarketplaceSection(configPath, marketplace);
		_tomlConfigEditor.RemovePluginSection(configPath, ToolkitDistribution.PluginName, marketplace);
		_tomlConfigEditor.RemoveSkillConfigOverride(configPath,
			$"{ToolkitDistribution.PluginName}:{ToolkitDistribution.SkillName}");
	}

	private void DeleteLegacyDirectoryIfExists(string path) {
		try {
			if (!FileSystem.ExistsDirectory(path)) {
				return;
			}

			ClearReadOnlyAttributes(path);
			FileSystem.DeleteDirectoryIfExists(path);
		}
		catch (DirectoryNotFoundException) {
			// Another installer process already removed this legacy path.
		}
		catch (FileNotFoundException) {
			// Another installer process already removed an entry from this legacy path.
		}
	}

	private void ClearReadOnlyAttributes(string path) {
		try {
			IDirectoryInfo directory = FileSystem.GetDirectoryInfo(path);
			if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) {
				return;
			}

			foreach (IFileInfo file in FileSystem.GetFilesInfos(path, "*", SearchOption.TopDirectoryOnly)) {
				try {
					if ((file.Attributes & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint))
						== FileAttributes.ReadOnly) {
						FileSystem.ResetFileReadOnlyAttribute(file.FullName);
					}
				}
				catch (FileNotFoundException) {
					// Another installer process removed this file; continue with its siblings.
				}
			}

			foreach (string childDirectory in FileSystem.GetDirectories(path)) {
				ClearReadOnlyAttributes(childDirectory);
			}

			if ((directory.Attributes & FileAttributes.ReadOnly) != 0) {
				directory.Attributes &= ~FileAttributes.ReadOnly;
			}
		}
		catch (DirectoryNotFoundException) {
			// A concurrently running installer already removed this directory.
		}
		catch (FileNotFoundException) {
			// A concurrently running installer already removed an entry from this directory.
		}
	}

	private string ConfigPath() => FileSystem.Combine(AgentHome, "config.toml");
}
