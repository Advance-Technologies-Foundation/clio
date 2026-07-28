namespace ClioRing.ViewModels;

/// <summary>How a workflow command supplies the selected environment to its clio tool.</summary>
public enum EnvArgKind {
	/// <summary>No environment argument (for example the handshake-only <c>version</c> action).</summary>
	None,

	/// <summary>Passes the environment as <c>{"environment-name": &lt;env&gt;}</c>.</summary>
	EnvironmentName,

	/// <summary>Passes the environment as <c>{"environmentName": &lt;env&gt;}</c> (legacy-cased tools).</summary>
	EnvironmentNameCamel
}

/// <summary>
/// A single command action shown in the workflow UI. Pure descriptor; the view-model builds the
/// clio-run request and gates destructive ones through a typed confirm.
/// </summary>
public sealed record WorkflowCommand {
	/// <summary>Stable key (for example <c>list-packages</c>).</summary>
	public required string Key { get; init; }

	/// <summary>Button label.</summary>
	public required string Title { get; init; }

	/// <summary>One-line description shown under the label.</summary>
	public required string Description { get; init; }

	/// <summary>
	/// The clio MCP tool name to invoke, or null for the special <c>version</c> action (which reads the
	/// handshake serverInfo and makes no call).
	/// </summary>
	public string? Tool { get; init; }

	/// <summary>How the selected environment is passed to the tool.</summary>
	public EnvArgKind EnvArg { get; init; } = EnvArgKind.None;

	/// <summary>True when the command mutates the target and must pass a typed confirm before running.</summary>
	public bool Destructive { get; init; }
}
