namespace Clio.Common.Skills;

/// <summary>
/// Outcome of invoking an agent CLI (<c>claude</c>/<c>codex</c>/<c>copilot</c>).
/// </summary>
/// <param name="Succeeded">True when the process started and exited with code 0.</param>
/// <param name="ExitCode">Process exit code when available.</param>
/// <param name="StandardOutput">Captured standard output.</param>
/// <param name="StandardError">Captured standard error.</param>
public sealed record AgentCliResult(bool Succeeded, int? ExitCode, string StandardOutput, string StandardError) {
	/// <summary>
	/// Gets the most relevant error text (standard error, falling back to standard output).
	/// </summary>
	public string ErrorText {
		get {
			string stderr = StandardError?.Trim();
			return string.IsNullOrWhiteSpace(stderr) ? StandardOutput?.Trim() ?? string.Empty : stderr;
		}
	}
}

/// <summary>
/// Runs coding-agent CLIs for plugin/marketplace operations, abstracting PATH
/// resolution and the PowerShell-script shim away from the agent strategies.
/// </summary>
/// <remarks>
/// Mirrors the toolkit installer's <c>_resolve_cli_command</c>: a <c>.ps1</c>
/// launcher is invoked through <c>powershell -ExecutionPolicy Bypass -File</c>.
/// Injected into agents so unit tests substitute it and never spawn a real CLI.
/// </remarks>
public interface IAgentCliRunner {
	/// <summary>
	/// Determines whether the named CLI resolves on the current PATH.
	/// </summary>
	/// <param name="cliName">CLI executable name (e.g. <c>claude</c>).</param>
	/// <returns><c>true</c> when the CLI can be resolved; otherwise <c>false</c>.</returns>
	bool IsOnPath(string cliName);

	/// <summary>
	/// Runs the named CLI with the supplied arguments, waiting for completion.
	/// </summary>
	/// <param name="cliName">CLI executable name (e.g. <c>codex</c>).</param>
	/// <param name="args">Command arguments (quoted internally as needed).</param>
	/// <returns>The captured execution result.</returns>
	AgentCliResult Run(string cliName, params string[] args);
}
