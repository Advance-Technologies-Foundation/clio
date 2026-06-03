using Clio.Common.Skills;

namespace Clio.Common.Skills.Agents;

/// <summary>
/// Shared base for <see cref="ICodingAgent"/> implementations: agent-home
/// resolution and presence detection.
/// </summary>
public abstract class CodingAgentBase(IFileSystem fileSystem, IUserHomeProvider homeProvider) : ICodingAgent {
	/// <summary>
	/// Gets the file system abstraction.
	/// </summary>
	protected IFileSystem FileSystem { get; } = fileSystem;

	/// <summary>
	/// Gets the user-home provider.
	/// </summary>
	protected IUserHomeProvider HomeProvider { get; } = homeProvider;

	/// <inheritdoc />
	public abstract string AgentId { get; }

	/// <inheritdoc />
	public abstract string DisplayName { get; }

	/// <summary>
	/// Gets this agent's home directory (e.g. <c>~/.codex</c>).
	/// </summary>
	protected string AgentHome => HomeProvider.GetAgentHome(AgentId);

	/// <inheritdoc />
	public virtual bool Detect() => FileSystem.ExistsDirectory(AgentHome);

	/// <inheritdoc />
	public abstract AgentOutcome Install(AgentOperationContext context);

	/// <inheritdoc />
	public abstract AgentOutcome Update(AgentOperationContext context);

	/// <inheritdoc />
	public abstract AgentOutcome Delete(AgentOperationContext context);
}
