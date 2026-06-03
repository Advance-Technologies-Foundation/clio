namespace Clio.Common.Skills;

/// <summary>
/// Resolves user-level directories used by the multi-agent skill installer.
/// </summary>
/// <remarks>
/// Mirrors the toolkit installer's <c>Path.home()</c>-based probing. Replaces the
/// former codex-only <c>IAgentHomePathProvider</c>. Injected everywhere so unit
/// tests can substitute a temp home and never touch the developer's real agent
/// directories.
/// </remarks>
public interface IUserHomeProvider {
	/// <summary>
	/// Gets the absolute current-user home directory.
	/// </summary>
	string GetUserHome();

	/// <summary>
	/// Gets the absolute home directory for a coding agent (e.g. <c>~/.codex</c>).
	/// </summary>
	/// <param name="agentId">Agent id (<c>claude</c>|<c>codex</c>|<c>cursor</c>|<c>copilot</c>).</param>
	string GetAgentHome(string agentId);

	/// <summary>
	/// Gets the absolute <c>~/.agents</c> directory (cross-agent plugin/skill state).
	/// </summary>
	string GetAgentsDir();

	/// <summary>
	/// Gets the absolute <c>~/.clio</c> directory (clio-owned state such as the managed-skills manifest).
	/// </summary>
	string GetClioDir();
}
