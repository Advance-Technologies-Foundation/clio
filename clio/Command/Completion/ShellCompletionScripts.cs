using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Command.Completion;

/// <summary>
/// The shell-integration scripts printed by the <c>completion</c> verb.
///
/// Each script registers a completion function that shells out to
/// <c>clio __complete &lt;shell&gt; &lt;currentWord&gt; [wordsBefore...]</c>, so the command and option names
/// always come from the live assembly and never from a list maintained here. This type owns the canonical
/// shell names (the single source every other surface reads) and the scripts themselves.
/// </summary>
public static class ShellCompletionScripts {

	/// <summary>Canonical shell names, in the order completion offers them.</summary>
	public static readonly IReadOnlyList<string> Names = ["bash", "powershell", "zsh", "fish"];

	/// <summary>
	/// Maps a user-supplied shell token to a canonical name, accepting the common aliases
	/// (<c>pwsh</c>, <c>ps</c> for powershell).
	/// </summary>
	/// <param name="shell">Shell token as typed by the user.</param>
	/// <returns>The canonical name, or <see langword="null"/> when the token names no supported shell.</returns>
	public static string Normalize(string shell) {
		if (string.IsNullOrWhiteSpace(shell)) {
			return null;
		}

		return shell.Trim().ToLowerInvariant() switch {
			"bash" => "bash",
			"zsh" => "zsh",
			"fish" => "fish",
			"powershell" or "pwsh" or "ps" => "powershell",
			_ => null,
		};
	}

	/// <summary>Returns the completion script for a canonical shell name.</summary>
	/// <param name="canonicalShell">A name produced by <see cref="Normalize"/>.</param>
	/// <returns>The script, ready to be sourced by the target shell.</returns>
	public static string GetScript(string canonicalShell) =>
		canonicalShell switch {
			"bash" => BashScript,
			"zsh" => ZshScript,
			"powershell" => PowerShellScript,
			"fish" => FishScript,
			_ => throw new ArgumentOutOfRangeException(nameof(canonicalShell), canonicalShell,
				"Not a canonical shell name. Use Normalize first."),
		};

	/// <summary>Shell names whose name starts with <paramref name="prefix"/>, for completing the verb's argument.</summary>
	internal static IReadOnlyList<string> MatchingShells(string prefix) =>
		Names.Where(name => name.StartsWith(prefix ?? string.Empty, StringComparison.OrdinalIgnoreCase)).ToList();

	private const string BashScript = """
		# clio tab completion for bash. Install with:
		#   eval "$(clio completion bash)"
		# `eval` rather than sourcing a process substitution: bash 3.2, still /bin/bash on macOS,
		# cannot source a non-seekable fd, so that form silently defines nothing.
		_clio_completion() {
		    local cur i
		    local -a before
		    COMPREPLY=()
		    cur="${COMP_WORDS[COMP_CWORD]}"
		    # Every token before the cursor except the command name at index 0.
		    i=1
		    while [ "$i" -lt "$COMP_CWORD" ]; do
		        before+=("${COMP_WORDS[i]}")
		        i=$((i + 1))
		    done
		    # Expanding an empty array as "${before[@]}" is an unbound-variable error under `set -u`
		    # on bash 3.2 (the macOS default), so the bare form is used when nothing precedes the cursor.
		    if [ "${#before[@]}" -gt 0 ]; then
		        COMPREPLY=( $(clio __complete bash "$cur" "${before[@]}") )
		    else
		        COMPREPLY=( $(clio __complete bash "$cur") )
		    fi
		}
		complete -o default -F _clio_completion clio
		""";

	private const string ZshScript = """
		# clio tab completion for zsh. Install with:
		#   source <(clio completion zsh)
		# Requires compinit to have run (the default in an interactive zsh).
		_clio_completion() {
		    local -a before results
		    local output
		    # words[1] is the command name, so the words before the cursor are words[2..CURRENT-1].
		    before=("${(@)words[2,CURRENT-1]}")
		    output="$(clio __complete zsh "${words[CURRENT]:-}" "${before[@]}" 2>/dev/null)"
		    # Nothing matched: return non-zero so zsh falls back to its default (file) completion.
		    [[ -n "$output" ]] || return 1
		    results=("${(@f)output}")
		    # -- keeps option flags (e.g. --env) from being read as compadd's own options.
		    compadd -- "${results[@]}"
		}
		compdef _clio_completion clio
		""";

	private const string PowerShellScript = """
		# clio tab completion for PowerShell. Install with:
		#   clio completion powershell | Out-String | Invoke-Expression
		# Needs PowerShell 7+: `Register-ArgumentCompleter -Native`, which is what registers a
		# completer for a native executable, exists only there.
		if ($PSVersionTable.PSVersion.Major -ge 7) {
		    Register-ArgumentCompleter -Native -CommandName clio -ScriptBlock {
		        param($wordToComplete, $commandAst, $cursorPosition)

		        $words = @($commandAst.CommandElements | ForEach-Object { $_.Extent.Text })
		        if ($words.Count -gt 0 -and $words[0] -eq 'clio') {
		            $words = @($words | Select-Object -Skip 1)
		        }
		        if ($wordToComplete -and $words.Count -gt 0) {
		            # The partially typed token is the last element and is already passed as $wordToComplete.
		            $words = @($words | Select-Object -SkipLast 1)
		        }

		        try {
		            & clio __complete powershell "$wordToComplete" @words 2>$null
		        }
		        catch {
		            # A missing or failing clio must never break the prompt.
		        }
		    }
		}
		else {
		    Write-Warning 'clio tab completion needs PowerShell 7 or newer.'
		}
		""";

	private const string FishScript = """
		# clio tab completion for fish. Install with:
		#   clio completion fish | source
		function __clio_completion
		    set -l tokens (commandline -opc)
		    set -l cur (commandline -ct)
		    if test -z "$cur"
		        if test (count $tokens) -ge 2
		            clio __complete fish "" $tokens[2..-1]
		        else
		            clio __complete fish ""
		        end
		    else
		        # The partial token is the last one and is already passed as $cur; drop it.
		        set -l before
		        set -l i 2
		        set -l last (count $tokens)
		        while test $i -lt $last
		            set before $before $tokens[$i]
		            set i (math $i + 1)
		        end
		        clio __complete fish $cur $before
		    end
		end
		complete -c clio -f -a '(__clio_completion)'
		""";
}
