namespace Clio.Common.Skills;

/// <summary>
/// Canonical identifiers for the Creatio AI App Development Toolkit (CAADT)
/// distribution that the skill commands install across coding agents.
/// </summary>
/// <remarks>
/// These values mirror the constants in the toolkit's Python installer
/// (<c>installer/install.py</c>) so clio's native installer stays in lockstep
/// with the canonical distribution: plugin name, marketplace name, git source,
/// composed plugin source, and the bundled skill name.
/// </remarks>
public static class ToolkitDistribution {
	/// <summary>
	/// Plugin package name as registered with each agent's plugin marketplace.
	/// </summary>
	public const string PluginName = "creatio-ai-app-development-toolkit";

	/// <summary>
	/// Marketplace name under which the plugin is registered.
	/// </summary>
	public const string MarketplaceName = "creatio";

	/// <summary>
	/// Public git marketplace URL used as the default install source for the
	/// CLI-based agents (Claude, Codex, Copilot) and the Cursor file-copy clone.
	/// </summary>
	public const string MarketplaceGitUrl =
		"https://github.com/Creatio-Platform/creatio-ai-app-development-toolkit.git";

	/// <summary>
	/// Branch the released plugin payload is pinned to. Cursor's file-copy clone
	/// targets this ref so it matches what the CLI agents fetch via the marketplace.
	/// </summary>
	public const string ReleaseRef = "release";

	/// <summary>
	/// Composed plugin source (<c>&lt;plugin&gt;@&lt;marketplace&gt;</c>) accepted by the
	/// agent CLIs' <c>plugin install</c> / <c>plugin add</c> verbs.
	/// </summary>
	public const string PluginSource = PluginName + "@" + MarketplaceName;

	/// <summary>
	/// Name of the skill bundled in the plugin (the orchestrator skill directory).
	/// </summary>
	public const string SkillName = "creatio-app-orchestrator";
}
