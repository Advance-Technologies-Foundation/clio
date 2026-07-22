namespace Clio.Common;

/// <summary>
/// Carries ambient, process-wide run-mode facts that cross-cutting core services (for example the
/// <see cref="ConsoleLogger"/>) need in order to adapt their behavior without reaching into the CLI
/// entry point (<c>Program</c>). Keeping run-mode behind this Core-owned abstraction lets core logging
/// decide console-output suppression from an injected value instead of reading a static flag on the
/// executable — a prerequisite for extracting the MCP server into its own assembly.
/// </summary>
public interface IRuntimeMode {

	/// <summary>
	/// Gets a value indicating whether the current process is running as the MCP stdio server.
	/// When <see langword="true"/>, human-oriented console output must be suppressed so that
	/// <c>stdout</c> carries only Model Context Protocol traffic; when <see langword="false"/>
	/// (the default for an ordinary CLI invocation) console output is emitted normally.
	/// </summary>
	bool IsMcpServerMode { get; }
}
