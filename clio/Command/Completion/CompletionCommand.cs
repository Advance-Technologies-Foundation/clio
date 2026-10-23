using System;
using CommandLine;

namespace Clio.Command.Completion;

#region Class: CompletionOptions

/// <summary>Options for the <c>completion</c> command.</summary>
[Verb("completion", Aliases = [], HelpText = "Print the shell tab-completion script for a shell")]
public class CompletionOptions {

	/// <summary>Gets or sets the target shell: bash, zsh, powershell, or fish.</summary>
	[Value(0, MetaName = "Shell", Required = true,
		HelpText = "Target shell: bash, zsh, powershell, fish (pwsh and ps are accepted for powershell)")]
	public string Shell { get; set; }

}

#endregion

#region Class: CompletionCommand

/// <summary>
/// Prints the shell-integration script that registers tab completion for clio.
///
/// The script is written straight to standard output because the caller loads it into the shell
/// (<c>eval "$(clio completion bash)"</c>), so the bytes must reach the shell unchanged — the logger's
/// queue and per-message decoration would corrupt the script.
/// </summary>
public class CompletionCommand : Command<CompletionOptions> {

	/// <inheritdoc />
	public override int Execute(CompletionOptions options) {
		string shell = ShellCompletionScripts.Normalize(options.Shell);
		if (shell is null) {
			Console.Error.WriteLine(
				$"'{options.Shell}' is not a supported shell. Use one of: {string.Join(", ", ShellCompletionScripts.Names)}.");
			return 1;
		}

		// CLIO002: direct Console use is intentional and must NOT be routed through ILogger here. The
		// caller loads this output into the shell (`eval "$(clio completion bash)"`), so it has to reach
		// STDOUT byte-exact and synchronously: the logger's queue is drained by a background print thread
		// and its messages are decorated, which would corrupt a script. Mirrors the machine-readable
		// console-output pattern in ShowAppListCommand and CreateServerToServerOAuthAppCommand.
#pragma warning disable CLIO002
		Console.Out.WriteLine(ShellCompletionScripts.GetScript(shell));
#pragma warning restore CLIO002
		return 0;
	}

}

#endregion
