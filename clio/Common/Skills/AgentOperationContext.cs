namespace Clio.Common.Skills;

/// <summary>
/// The skill lifecycle operation an agent is asked to perform.
/// </summary>
public enum SkillOperationKind {
	/// <summary>Install the toolkit for the agent.</summary>
	Install,

	/// <summary>Update the toolkit for the agent.</summary>
	Update,

	/// <summary>Uninstall the toolkit from the agent.</summary>
	Delete
}

/// <summary>
/// Per-operation input handed to each <see cref="ICodingAgent"/>.
/// </summary>
/// <param name="Operation">The lifecycle operation being performed.</param>
/// <param name="RepositoryOverride">
/// The raw <c>--repo</c> value, or <c>null</c> to use the default toolkit source.
/// Interpreted by mechanism (decision O2): CLI agents treat it as the marketplace
/// git URL; Cursor treats it as a local path or git URL cloned for file-copy.
/// </param>
public sealed record AgentOperationContext(SkillOperationKind Operation, string RepositoryOverride) {
	/// <summary>
	/// Resolves the marketplace git URL for CLI-based agents: the <c>--repo</c>
	/// override when supplied, otherwise the default toolkit marketplace URL.
	/// </summary>
	public string MarketplaceUrl =>
		string.IsNullOrWhiteSpace(RepositoryOverride)
			? ToolkitDistribution.MarketplaceGitUrl
			: RepositoryOverride.Trim();
}
