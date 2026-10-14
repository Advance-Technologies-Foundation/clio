using System.Collections.Generic;
using CommandLine;

namespace Clio.Command.McpServer;

/// <summary>
/// Options shared by the MCP host verbs (<c>mcp-server</c> and <c>mcp-http</c>).
/// </summary>
/// <remarks>
/// <para>
/// The MCP verbs used to inherit <see cref="BaseCommandOptions"/>, so <c>--fail-on-error</c> wrote the
/// process-global <see cref="GlobalContext.FailOnError"/> in the HOST. That made a package install strict only
/// when it ran in-process: a call relayed to a worker is spawned with <c>mcp-server --worker</c> alone and
/// never saw the flag. The strict mode itself is also unreliable — it requires the installation log to contain
/// "application installed successfully", which observed package installs do not emit, so it reports a
/// successful install as failed (see <c>docs/agent-instructions/bundled-packages.md</c>, Known gaps).
/// </para>
/// <para>
/// The flags are therefore UNSUPPORTED on the MCP verbs. They are still accepted, hidden, so an existing MCP
/// client configuration that passes them keeps starting, but they never touch <see cref="GlobalContext"/>:
/// every tool call behaves the same whether it runs in-process or in a worker. The host logs a warning
/// naming the ignored flags (<see cref="DescribeIgnoredFailOnOptions"/>).
/// </para>
/// <para>
/// Both spellings <see cref="BaseCommandOptions"/> accepts parse here too, <c>--fail-on-error</c> and the
/// legacy <c>----fail-on-error</c>, so the MCP verbs never reject a command line the other verbs take.
/// </para>
/// </remarks>
public abstract class McpHostCommandOptions
{
	/// <summary>
	/// Gets or sets whether <c>--fail-on-error</c> was passed. Accepted for compatibility only; it has no effect.
	/// </summary>
	[Option("fail-on-error", Required = false, Hidden = true,
		HelpText = "Not supported on MCP verbs; accepted for compatibility and ignored.")]
	public bool FailOnError { get; set; }

	/// <summary>
	/// Hidden legacy spelling <c>----fail-on-error</c> of <see cref="FailOnError"/>. The setter only turns the
	/// flag on, so an unset alias never clears a flag the main option set.
	/// </summary>
	[Option("--fail-on-error", Required = false, Hidden = true,
		HelpText = "Legacy ----fail-on-error spelling; not supported on MCP verbs and ignored.")]
	public bool FailOnErrorAlias {
		get => FailOnError;
		set { if (value) FailOnError = value; }
	}

	/// <summary>
	/// Gets or sets whether <c>--fail-on-warning</c> was passed. Accepted for compatibility only; it has no effect.
	/// </summary>
	[Option("fail-on-warning", Required = false, Hidden = true,
		HelpText = "Not supported on MCP verbs; accepted for compatibility and ignored.")]
	public bool FailOnWarning { get; set; }

	/// <summary>
	/// Hidden legacy spelling <c>----fail-on-warning</c> of <see cref="FailOnWarning"/>; see
	/// <see cref="FailOnErrorAlias"/>.
	/// </summary>
	[Option("--fail-on-warning", Required = false, Hidden = true,
		HelpText = "Legacy ----fail-on-warning spelling; not supported on MCP verbs and ignored.")]
	public bool FailOnWarningAlias {
		get => FailOnWarning;
		set { if (value) FailOnWarning = value; }
	}

	/// <summary>
	/// Builds the startup warning for fail-on flags that were passed to an MCP verb and are ignored.
	/// </summary>
	/// <param name="options">The parsed MCP host options.</param>
	/// <returns>The warning text, or <see langword="null"/> when no fail-on flag was passed.</returns>
	internal static string DescribeIgnoredFailOnOptions(McpHostCommandOptions options) {
		List<string> ignored = [];
		if (options.FailOnError) {
			ignored.Add("--fail-on-error");
		}
		if (options.FailOnWarning) {
			ignored.Add("--fail-on-warning");
		}
		if (ignored.Count == 0) {
			return null;
		}
		return $"{string.Join(" and ", ignored)} {(ignored.Count == 1 ? "is" : "are")} not supported by the MCP "
			+ "server and IGNORED: package installs made through MCP tools never use the strict install-log "
			+ "check. Remove the flag from the MCP client configuration.";
	}
}
